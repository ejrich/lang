using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lang.Parsing;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        private readonly Dictionary<string, FunctionAst> _functions = new();
        private readonly Dictionary<string, StructAst> _structs = new();
        private FunctionAst _currentFunction;

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors)
        {
            errors = new List<TranslationError>();

            // 1. First load the user defined structs
            var definedStructs = new HashSet<string>();
            var structs = parseResult.SyntaxTrees.Where(ast => ast is StructAst).Cast<StructAst>().ToList();
            foreach (var structAst in structs)
            {
                if (!definedStructs.Add(structAst.Name))
                {
                    errors.Add(CreateError($"Multiple definitions of struct '{structAst.Name}'", structAst));
                }
            }
            // 2. Verify struct bodies
            foreach (var structAst in structs)
            {
                VerifyStruct(structAst, definedStructs, errors);
            }

            // 3. Load and verify function return types and arguments
            foreach (var function in parseResult.SyntaxTrees.Where(ast => ast is FunctionAst).Cast<FunctionAst>())
            {
                VerifyFunctionDefinition(function, errors);
            }

            var graph = new ProgramGraph
            {
                Data = new Data
                {
                    Structs = _structs.Values.ToList()
                }
            };
            // 4. Verify function bodies
            foreach (var syntaxTree in parseResult.SyntaxTrees)
            {
                switch (syntaxTree)
                {
                    case FunctionAst function:
                        _currentFunction = function;
                        var main = function.Name.Equals("main", StringComparison.OrdinalIgnoreCase);
                        VerifyFunction(function, main, errors);
                        if (main)
                        {
                            if (graph.Main != null)
                            {
                                errors.Add(CreateError("Only one main function can be defined", function));
                            }
                            graph.Main = function;
                        }
                        else
                        {
                            graph.Functions.Add(function);
                        }
                        break;
                    // TODO Handle more type of ASTs
                }
            }

            return graph;
        }

        private void VerifyStruct(StructAst structAst, HashSet<string> definedStructs, List<TranslationError> errors)
        {
            // 1. Verify struct fields have valid types
            var fieldNames = new HashSet<string>();
            foreach (var structField in structAst.Fields)
            {
                // 1a. Check if the field has been previously defined
                if (!fieldNames.Add(structField.Name))
                {
                    errors.Add(CreateError($"Struct '{structAst.Name}' already contains field '{structField.Name}'", structField));
                }

                // 1b. Check for errored or undefined field types
                var type = VerifyType(structField.Type, errors, false);

                if (type == Type.Error || (type == Type.Other && !definedStructs.Contains(structField.Type.Name)))
                {
                    errors.Add(CreateError($"Type '{PrintTypeDefinition(structField.Type)}' of field {structAst.Name}.{structField.Name} is not defined", structField));
                }

                // 1c. Check if the default value has the correct type
                if (structField.DefaultValue != null)
                {
                    if (!TypeEquals(structField.Type, structField.DefaultValue.Type))
                    {
                        errors.Add(CreateError($"Type of field {structAst.Name}.{structField.Name} is '{type}', but default value is type '{structField.DefaultValue.Type}'", structField.DefaultValue));
                    }
                }
            }

            // 2. Load the struct into the dictionary
            _structs.TryAdd(structAst.Name, structAst);
        }

        private void VerifyFunctionDefinition(FunctionAst function, List<TranslationError> errors)
        {
            // 1. Verify the return type of the function is valid
            var returnType = VerifyType(function.ReturnType, errors);
            if (returnType == Type.Error)
            {
                errors.Add(CreateError($"Return type '{function.ReturnType.Name}' of function '{function.Name}' is not defined", function.ReturnType));
            }

            // 2. Verify the argument types
            var argumentNames = new HashSet<string>();
            foreach (var argument in function.Arguments)
            {
                // 1a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    errors.Add(CreateError($"Function '{function.Name}' already contains argument '{argument.Name}'", argument));
                }

                // 1b. Check for errored or undefined field types
                var type = VerifyType(argument.Type, errors);

                if (type == Type.Error)
                {
                    errors.Add(CreateError($"Type '{PrintTypeDefinition(argument.Type)}' of argument '{argument.Name}' in function '{function.Name}' is not defined", argument.Type));
                }
            }
            
            // 3. Load the function into the dictionary 
            if (!_functions.TryAdd(function.Name, function))
            {
                errors.Add(CreateError($"Multiple definitions of function '{function.Name}'", function));
            }
        }

        private void VerifyFunction(FunctionAst function, bool main, List<TranslationError> errors)
        {
            var localVariables = function.Arguments.ToDictionary(arg=> arg.Name, arg => arg.Type);

            if (main)
            {
                var type = VerifyType(function.ReturnType, errors);
                if (!(type == Type.Void || type == Type.Int))
                {
                    errors.Add(CreateError("The main function should return type 'int' or 'void'", function));
                }
            }

            VerifyScope(function.Children, localVariables, errors);
        }

        private void VerifyScope(List<IAst> syntaxTrees, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            var scopeVariables = new Dictionary<string, TypeDefinition>(localVariables);
            foreach (var syntaxTree in syntaxTrees)
            {
                VerifyAst(syntaxTree, scopeVariables, errors);
            }
        }

        private void VerifyAst(IAst syntaxTree, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            switch (syntaxTree)
            {
                case ReturnAst returnAst:
                    VerifyReturnStatement(returnAst, localVariables, _currentFunction.ReturnType, errors);
                    break;
                case DeclarationAst declaration:
                    VerifyDeclaration(declaration, localVariables, errors);
                    break;
                case AssignmentAst assignment:
                    VerifyAssignment(assignment, localVariables, errors);
                    break;
                case ScopeAst scope:
                    VerifyScope(scope.Children, localVariables, errors);
                    break;
                case ConditionalAst conditional:
                    VerifyConditional(conditional, localVariables, errors);
                    break;
                case WhileAst whileAst:
                    VerifyWhile(whileAst, localVariables, errors);
                    break;
                case EachAst each:
                    VerifyEach(each, localVariables, errors);
                    break;
                default:
                    VerifyExpression(syntaxTree, localVariables, errors);
                    break;
            }
        }

        private void VerifyReturnStatement(ReturnAst returnAst, IDictionary<string, TypeDefinition> localVariables,
            TypeDefinition functionReturnType, List<TranslationError> errors)
        {
            // 1. Infer the return type of the function
            var returnType = VerifyType(functionReturnType, errors);

            // 2. Handle void case since it's the easiest to interpret
            if (returnType == Type.Void)
            {
                if (returnAst.Value != null)
                {
                    errors.Add(CreateError("Function return should be void", returnAst));
                }
                return;
            }

            // 3. Determine if the expression returns the correct value
            var returnValueType = VerifyExpression(returnAst.Value, localVariables, errors);
            if (returnValueType == null)
            {
                errors.Add(CreateError($"Expected to return type '{functionReturnType.Name}'", returnAst));
            }
            else
            {
                if (!TypeEquals(functionReturnType, returnValueType))
                {
                    errors.Add(CreateError($"Expected to return type '{PrintTypeDefinition(functionReturnType)}', but returned type '{PrintTypeDefinition(returnValueType)}'", returnAst.Value));
                }
            }
        }

        private void VerifyDeclaration(DeclarationAst declaration, IDictionary<string, TypeDefinition> localVariables,
            List<TranslationError> errors)
        {
            // 1. Verify the variable is already defined
            if (localVariables.ContainsKey(declaration.Name))
            {
                errors.Add(CreateError($"Variable '{declaration.Name}' already defined", declaration));
                return;
            }

            // 2. Verify the assignment value
            var valueType = VerifyExpression(declaration.Value, localVariables, errors);

            // 3. Verify the assignment value matches the type definition if it has been defined
            if (declaration.Type == null)
            {
                declaration.Type = valueType;
            }
            else
            {
                var type = VerifyType(declaration.Type, errors);
                if (type == Type.Error)
                {
                    errors.Add(CreateError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type));
                }

                // Verify the type is correct
                if (valueType != null)
                {
                    if (!TypeEquals(declaration.Type, valueType))
                    {
                        errors.Add(CreateError($"Expected declaration value to be type '{PrintTypeDefinition(declaration.Type)}', " +
                            $"but got '{PrintTypeDefinition(valueType)}'", declaration.Type));
                    }
                    else if (declaration.Type.PrimitiveType != null && declaration.Value is ConstantAst constant)
                    {
                        constant.Type.Name = declaration.Type.Name;
                        constant.Type.PrimitiveType = declaration.Type.PrimitiveType;
                        VerifyConstant(constant, errors);
                    }
                }
            }

            localVariables.Add(declaration.Name, declaration.Type);
        }

        private void VerifyAssignment(AssignmentAst assignment, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the variable is already defined
            var variableName = assignment.Variable switch
            {
                VariableAst variable => variable.Name,
                StructFieldRefAst fieldRef => fieldRef.Name,
                _ => string.Empty
            };
            if (!localVariables.TryGetValue(variableName, out var variableTypeDefinition))
            {
                errors.Add(CreateError($"Variable '{variableName}' not defined", assignment));
                return;
            }

            // 2. Verify the assignment value
            var valueType = VerifyExpression(assignment.Value, localVariables, errors);

            // 3. Verify the assignment value matches the variable type definition
            if (assignment.Variable is StructFieldRefAst structField)
            {
                variableTypeDefinition = VerifyStructFieldRef(structField, variableTypeDefinition, errors);
                if (variableTypeDefinition == null) return;
            }
            if (valueType != null)
            {
                // 3a. Verify the operator is valid
                if (assignment.Operator != Operator.None)
                {
                    var type = VerifyType(variableTypeDefinition, errors);
                    var nextType = VerifyType(valueType, errors);
                    switch (assignment.Operator)
                    {
                        // Both need to be bool and returns bool
                        case Operator.And:
                        case Operator.Or:
                            if (type != Type.Boolean || nextType != Type.Boolean)
                            {
                                errors.Add(CreateError($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
                                    $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value));
                            }
                            break;
                        // Invalid assignment operators
                        case Operator.Equality:
                        case Operator.GreaterThan:
                        case Operator.LessThan:
                        case Operator.GreaterThanEqual:
                        case Operator.LessThanEqual:
                            errors.Add(CreateError($"Invalid operator '{PrintOperator(assignment.Operator)}' in assignment", assignment));
                            break;
                        // Requires same types and returns more precise type
                        case Operator.Add:
                        case Operator.Subtract:
                        case Operator.Multiply:
                        case Operator.Divide:
                        case Operator.Modulus:
                            if (!(type == Type.Int && nextType == Type.Int) &&
                                !(type == Type.Float && (nextType == Type.Float || nextType == Type.Int)))
                            {
                                errors.Add(CreateError($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
                                    $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value));
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (!(type == Type.Boolean && nextType == Type.Boolean) &&
                                !(type == Type.Int && nextType == Type.Int))
                            {
                                errors.Add(CreateError($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
                                    $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value));
                            }
                            break;
                    }
                }
                else if (!TypeEquals(variableTypeDefinition, valueType))
                {
                    errors.Add(CreateError($"Expected assignment value to be type '{PrintTypeDefinition(variableTypeDefinition)}'", assignment.Value));
                }
                else if (variableTypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                {
                    constant.Type.Name = variableTypeDefinition.Name;
                    constant.Type.PrimitiveType = variableTypeDefinition.PrimitiveType;
                    VerifyConstant(constant, errors);
                }
            }
        }

        private void VerifyConditional(ConditionalAst conditional, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the condition expression
            var conditionalType = VerifyExpression(conditional.Condition, localVariables, errors);
            switch (VerifyType(conditionalType, errors))
            {
                case Type.Int:
                case Type.Float:
                case Type.Boolean:
                    // Valid types
                    break;
                default:
                    errors.Add(CreateError($"Expected condition to be int, float, or bool, but got '{PrintTypeDefinition(conditionalType)}'", conditional.Condition));
                    break;
            }

            // 2. Verify the conditional scope
            VerifyScope(conditional.Children, localVariables, errors);

            // 3. Verify the else block if necessary
            if (conditional.Else != null)
            {
                VerifyAst(conditional.Else, localVariables, errors);
            }
        }

        private void VerifyWhile(WhileAst whileAst, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the condition expression
            var conditionalType = VerifyExpression(whileAst.Condition, localVariables, errors);
            switch (VerifyType(conditionalType, errors))
            {
                case Type.Int:
                case Type.Float:
                case Type.Boolean:
                    // Valid types
                    break;
                default:
                    errors.Add(CreateError($"Expected condition to be int, float, or bool, but got '{PrintTypeDefinition(conditionalType)}'", whileAst.Condition));
                    break;
            }

            // 2. Verify the scope of the while block
            VerifyScope(whileAst.Children, localVariables, errors);
        }

        private void VerifyEach(EachAst each, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            var eachVariables = new Dictionary<string, TypeDefinition>(localVariables);
            // 1. Verify the iterator or range
            if (each.Iteration != null)
            {
                // TODO Implement iterators
            }
            else
            {
                var beginType = VerifyExpression(each.RangeBegin, localVariables, errors);
                if (VerifyType(beginType, errors) != Type.Int)
                {
                    errors.Add(CreateError($"Expected range to begin with 'int', but got '{PrintTypeDefinition(beginType)}'", each.RangeBegin));
                }
                var endType = VerifyExpression(each.RangeEnd, localVariables, errors);
                if (VerifyType(endType, errors) != Type.Int)
                {
                    errors.Add(CreateError($"Expected range to end with 'int', but got '{PrintTypeDefinition(endType)}'", each.RangeEnd));
                }
                if (!eachVariables.TryAdd(each.IterationVariable, new TypeDefinition {Name = "int"}))
                {
                    errors.Add(CreateError($"Iteration variable '{each.IterationVariable}' already exists in scope", each));
                };
            }

            // 2. Verify the scope of the each block
            VerifyScope(each.Children, eachVariables, errors);
        }

        private TypeDefinition VerifyExpression(IAst ast, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the expression value
            switch (ast)
            {
                case ConstantAst constant:
                    return constant.Type;
                case StructFieldRefAst structField:
                {
                    if (!localVariables.TryGetValue(structField.Name, out var structType))
                    {
                        errors.Add(CreateError($"Variable '{structField.Name}' not defined", ast));
                        return null;
                    }
                    return VerifyStructFieldRef(structField, structType, errors);
                }
                case VariableAst variable:
                    if (!localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    {
                        errors.Add(CreateError($"Variable '{variable.Name}' not defined", ast));
                    }
                    return typeDefinition;
                case ChangeByOneAst changeByOne:
                    switch (changeByOne.Variable)
                    {
                        case VariableAst variable:
                            if (localVariables.TryGetValue(variable.Name, out var variableType))
                            {
                                var type = VerifyType(variableType, errors);
                                if (type == Type.Int || type == Type.Float) return variableType;

                                var op = changeByOne.Positive ? "increment" : "decrement";
                                errors.Add(CreateError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(variableType)}'", variable));
                                return null;
                            }
                            else
                            {
                                errors.Add(CreateError($"Variable '{variable.Name}' not defined", variable));
                                return null;
                            }
                        case StructFieldRefAst structField:
                            if (localVariables.TryGetValue(structField.Name, out var structType))
                            {
                                var fieldType = VerifyStructFieldRef(structField, structType, errors);
                                if (fieldType == null) return null;

                                var type = VerifyType(fieldType, errors);
                                if (type == Type.Int || type == Type.Float) return fieldType;

                                var op = changeByOne.Positive ? "increment" : "decrement";
                                errors.Add(CreateError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(fieldType)}'", structField));
                                return null;
                            }
                            else
                            {
                                errors.Add(CreateError($"Variable '{structField.Name}' not defined", structField));
                                return null;
                            }
                        default:
                            var operand = changeByOne.Positive ? "increment" : "decrement";
                            errors.Add(CreateError($"Expected to {operand} variable", changeByOne));
                            return null;
                    }
                case UnaryAst unary:
                {
                    var valueType = VerifyExpression(unary.Value, localVariables, errors);
                    var type = VerifyType(valueType, errors);
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not when type == Type.Boolean:
                            return valueType;
                        case UnaryOperator.Minus when (type == Type.Int || type == Type.Float):
                            return valueType;
                        default:
                            errors.Add(CreateError($"Expected type 'bool', but got type '{PrintTypeDefinition(valueType)}'", unary.Value));
                            return null;
                    }
                }
                case CallAst call:
                    if (_functions.TryGetValue(call.Function, out var function))
                    {
                        // Verify function arguments
                        if (function.Arguments.Count != call.Arguments.Count)
                        {
                            errors.Add(CreateError($"Call to function '{function.Name}' expected {function.Arguments.Count} arguments, but got {call.Arguments.Count}", call));
                            return null;
                        }

                        for (var i = 0; i < function.Arguments.Count; i++)
                        {
                            var functionType = function.Arguments[i].Type;
                            var callType = VerifyExpression(call.Arguments[i], localVariables, errors);
                            if (callType != null)
                            {
                                if (!TypeEquals(functionType, callType))
                                {
                                    errors.Add(CreateError($"Call to function '{function.Name}' expected '{PrintTypeDefinition(functionType)}', but got '{PrintTypeDefinition(callType)}'", call.Arguments[i]));
                                }
                            }
                        }
                    }
                    else
                    {
                        errors.Add(CreateError($"Call to undefined function '{call.Function}'", call));
                    }
                    return function?.ReturnType;
                case ExpressionAst expression:
                    return VerifyExpressionType(expression, localVariables, errors);
                case null:
                    return null;
                default:
                    errors.Add(CreateError($"Unexpected Ast '{ast}'", ast));
                    return null;
            }
        }

        private TypeDefinition VerifyConstant(ConstantAst constant, List<TranslationError> errors)
        {
            var type = constant.Type;
            switch (type.PrimitiveType)
            {
                case IntegerType integer:
                    if (!integer.Signed && constant.Value[0] == '-')
                    {
                        errors.Add(CreateError($"Unsigned type '{PrintTypeDefinition(constant.Type)}' cannot be negative", constant));
                        break;
                    }

                    var success = integer.Bytes switch
                    {
                        1 => integer.Signed ? sbyte.TryParse(constant.Value, out _) : byte.TryParse(constant.Value, out _),
                        2 => integer.Signed ? short.TryParse(constant.Value, out _) : ushort.TryParse(constant.Value, out _),
                        4 => integer.Signed ? int.TryParse(constant.Value, out _) : uint.TryParse(constant.Value, out _),
                        8 => integer.Signed ? long.TryParse(constant.Value, out _) : ulong.TryParse(constant.Value, out _),
                        _ => integer.Signed ? int.TryParse(constant.Value, out _) : uint.TryParse(constant.Value, out _),
                    };
                    if (!success)
                    {
                        errors.Add(CreateError($"Value '{constant.Value}' out of range for type '{PrintTypeDefinition(constant.Type)}'", constant));
                    }
                    break;
            }

            return type;
        }

        private TypeDefinition VerifyExpressionType(ExpressionAst expression, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Get the type of the initial child
            expression.Type = VerifyExpression(expression.Children[0], localVariables, errors);
            for (var i = 1; i < expression.Children.Count; i++)
            {
                // 2. Get the next operator and expression type
                var op = expression.Operators[i - 1];
                var nextExpressionType = VerifyExpression(expression.Children[i], localVariables, errors);
                if (nextExpressionType == null) return null;

                // 3. Verify the operator and expression types are compatible and convert the expression type if necessary
                var type = VerifyType(expression.Type, errors);
                var nextType = VerifyType(nextExpressionType, errors);
                switch (op)
                {
                    // Both need to be bool and returns bool
                    case Operator.And:
                    case Operator.Or:
                        if (type != Type.Boolean || nextType != Type.Boolean)
                        {
                            errors.Add(CreateError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]));
                            expression.Type = new TypeDefinition {Name = "bool"};
                        }
                        break;
                    // Requires same types and returns bool
                    case Operator.Equality:
                    case Operator.GreaterThan:
                    case Operator.LessThan:
                    case Operator.GreaterThanEqual:
                    case Operator.LessThanEqual:
                        if (!(type == Type.Int || type == Type.Float) &&
                            !(nextType == Type.Int || nextType == Type.Float))
                        {
                            errors.Add(CreateError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]));
                        }
                        expression.Type = new TypeDefinition {Name = "bool"};
                        break;
                    // Requires same types and returns more precise type
                    case Operator.Add:
                    case Operator.Subtract:
                    case Operator.Multiply:
                    case Operator.Divide:
                    case Operator.Modulus:
                        if ((type == Type.Int || type == Type.Float) ||
                            (nextType == Type.Int || nextType == Type.Float))
                        {
                            // For integer operations, use the larger size and convert to signed if one type is signed
                            if (type == Type.Int && nextType == Type.Int)
                            {
                                var currentIntegerType = expression.Type.PrimitiveType;
                                var nextIntegerType = nextExpressionType.PrimitiveType;
                                if (currentIntegerType.Bytes == nextIntegerType.Bytes &&
                                    currentIntegerType.Signed == nextIntegerType.Signed)
                                    break;

                                var integerType = new IntegerType
                                {
                                    Bytes = currentIntegerType.Bytes > nextIntegerType.Bytes ? currentIntegerType.Bytes : nextIntegerType.Bytes,
                                    Signed = currentIntegerType.Signed || nextIntegerType.Signed
                                };
                                expression.Type = new TypeDefinition
                                {
                                    Name = $"{(integerType.Signed ? "s" : "u")}{integerType.Bytes * 8}",
                                    PrimitiveType = integerType
                                };
                            }
                            // For floating point operations, convert to the larger size
                            else if (type == Type.Float && nextType == Type.Float)
                            {
                                if (expression.Type.PrimitiveType.Bytes < nextExpressionType.PrimitiveType.Bytes)
                                {
                                    expression.Type = nextExpressionType;
                                }
                            }
                            // For an int lhs and float rhs, convert to the floating point type
                            // Note that float lhs and int rhs are covered since the floating point is already selected
                            else if (nextType == Type.Float)
                            {
                                expression.Type = nextExpressionType;
                            }
                        }
                        else
                        {
                            errors.Add(CreateError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]));
                        }
                        break;
                    // Requires both integer or bool types and returns more same type
                    case Operator.BitwiseAnd:
                    case Operator.BitwiseOr:
                    case Operator.Xor:
                        if (type == Type.Int && nextType == Type.Int)
                        {
                            var currentIntegerType = expression.Type.PrimitiveType;
                            var nextIntegerType = nextExpressionType.PrimitiveType;
                            if (currentIntegerType.Bytes == nextIntegerType.Bytes &&
                                currentIntegerType.Signed == nextIntegerType.Signed)
                                break;

                            var integerType = new IntegerType
                            {
                                Bytes = currentIntegerType.Bytes > nextIntegerType.Bytes ? currentIntegerType.Bytes : nextIntegerType.Bytes,
                                Signed = currentIntegerType.Signed || nextIntegerType.Signed
                            };
                            expression.Type = new TypeDefinition
                            {
                                Name = $"{(integerType.Signed ? "s" : "u")}{integerType.Bytes * 8}",
                                PrimitiveType = integerType
                            };
                        }
                        else if (!(type == Type.Boolean && nextType == Type.Boolean))
                        {
                            errors.Add(CreateError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]));
                            if (nextType == Type.Boolean || nextType == Type.Int)
                            {
                                expression.Type = nextExpressionType;
                            }
                            else if (!(type == Type.Boolean || type == Type.Int))
                            {
                                // If the type can't be determined, default to int
                                expression.Type = new TypeDefinition {Name = "int"};
                            }
                        }
                        break;
                }
                expression.ResultingTypes.Add(expression.Type);
            }
            return expression.Type;
        }

        private TypeDefinition VerifyStructFieldRef(StructFieldRefAst structField, TypeDefinition structType,
            List<TranslationError> errors)
        {
            // 1. Load the struct definition in typeDefinition
            structField.StructName = structType.Name;
            if (!_structs.TryGetValue(structType.Name, out var structDefinition))
            {
                errors.Add(CreateError($"Struct '{structType.Name}' not defined", structField));
                return null;
            }

            // 2. If the type of the field is other, recurse and return
            var value = structField.Value;
            StructFieldAst field = null;
            for (var i = 0; i < structDefinition.Fields.Count; i++)
            {
                if (structDefinition.Fields[i].Name == value.Name)
                {
                    structField.ValueIndex = i;
                    field = structDefinition.Fields[i];
                    break;
                }
            }
            if (field == null)
            {
                errors.Add(CreateError($"Struct '{structType.Name}' does not contain field '{value.Name}'", structField));
                return null;
            }

            return value.Value == null ? field.Type : VerifyStructFieldRef(value, field.Type, errors);
        }

        private static bool TypeEquals(TypeDefinition a, TypeDefinition b)
        {
            // Check by primitive type
            switch (a.PrimitiveType)
            {
                case IntegerType:
                    return b.PrimitiveType is IntegerType;
                case FloatType:
                    return b.PrimitiveType is FloatType;
                default:
                    if (b.PrimitiveType != null) return false;
                    break;
            }

            // Check by name
            if (a.Name != b.Name) return false;
            if (a.Generics.Count != b.Generics.Count) return false;
            for (var i = 0; i < a.Generics.Count; i++)
            {
                var ai = a.Generics[i];
                var bi = b.Generics[i];
                if (!TypeEquals(ai, bi)) return false;
            }
            return true;
        }

        private Type VerifyType(TypeDefinition typeDef, List<TranslationError> errors, bool verifyStruct = true)
        {
            if (typeDef == null) return Type.Error;

            var hasGenerics = typeDef.Generics.Any();
            switch (typeDef.Name)
            {
                case "int":
                case "u64":
                case "s64":
                case "u32":
                case "s32":
                case "u16":
                case "s16":
                case "u8":
                case "s8":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError($"Type '{typeDef.Name}' cannot have generics", typeDef));
                        return Type.Error;
                    }
                    return VerifyIntegerType(typeDef);
                case "float":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("Type 'float' cannot have generics", typeDef));
                        return Type.Error;
                    }
                    typeDef.PrimitiveType = new FloatType {Bytes = 4};
                    return Type.Float;
                case "float64":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("Type 'float64' cannot have generics", typeDef));
                        return Type.Error;
                    }
                    typeDef.PrimitiveType = new FloatType {Bytes = 8};
                    return Type.Float;
                case "bool":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("boolean type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    return Type.Boolean;
                case "string":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("string type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    return Type.String;
                case "List":
                    return Type.List;
                case "void":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("void type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    return Type.Void;
                default:
                    if (!verifyStruct) return Type.Other;
                    return _structs.ContainsKey(typeDef.Name) ? Type.Other : Type.Error;
            }
        }

        private static Type VerifyIntegerType(TypeDefinition typeDef)
        {
            if (typeDef.Name == "int")
            {
                typeDef.PrimitiveType = new IntegerType {Bytes = 4, Signed = true};
            }
            else
            {
                var bytes = ushort.Parse(typeDef.Name[1..]) / 8;
                typeDef.PrimitiveType = new IntegerType {Bytes = (ushort) bytes, Signed = typeDef.Name[0] == 's'};
            }

            return Type.Int;
        }

        private static string PrintTypeDefinition(TypeDefinition type)
        {
            if (type == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append(type.Name);
            if (type.Generics.Any())
            {
                sb.Append($"<{string.Join(", ", type.Generics.Select(PrintTypeDefinition))}>");
            }
            return sb.ToString();
        }

        private static string PrintOperator(Operator op)
        {
            return op switch
            {
                Operator.And => "&&",
                Operator.Or => "||",
                Operator.Equality => "==",
                Operator.NotEqual => "!=",
                Operator.GreaterThanEqual => ">=",
                Operator.LessThanEqual => "<=",
                _ => ((char)op).ToString()
            };
        }

        private static TranslationError CreateError(string error, IAst ast)
        {
            return new()
            {
                Error = error,
                FileIndex = ast.FileIndex,
                Line = ast.Line,
                Column = ast.Column
            };
        }
    }
}
