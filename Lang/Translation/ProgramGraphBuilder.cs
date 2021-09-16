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
        private readonly Dictionary<string, StructAst> _polymorphicStructs = new();
        private FunctionAst _currentFunction;

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors)
        {
            errors = new List<TranslationError>();

            // 1. First load the user defined structs
            foreach (var structAst in parseResult.Structs)
            {
                if (_structs.ContainsKey(structAst.Name))
                {
                    errors.Add(CreateError($"Multiple definitions of struct '{structAst.Name}'", structAst));
                }
                else if (structAst.Generics.Any())
                {
                    _polymorphicStructs.Add(structAst.Name, structAst);
                }
                else
                {
                    _structs.Add(structAst.Name, structAst);
                }
            }
            var graph = new ProgramGraph
            {
                Data = new Data()
            };

            // 2. Verify struct bodies
            foreach (var structAst in parseResult.Structs)
            {
                VerifyStruct(structAst, errors);
            }

            // 3. Verify global variables
            var globalVariables = new Dictionary<string, TypeDefinition>();
            foreach (var globalVariable in parseResult.GlobalVariables)
            {
                if (globalVariable.Value != null && globalVariable.Value is not ConstantAst)
                {
                    errors.Add(CreateError("Global variable must either not be initialized or be initialized to a constant value", globalVariable.Value));
                }
                VerifyDeclaration(globalVariable, globalVariables, errors);
                graph.Data.Variables.Add(globalVariable);
            }

            // 4. Load and verify function return types and arguments
            foreach (var function in parseResult.Functions)
            {
                VerifyFunctionDefinition(function, errors);
            }

            // 5. Verify function bodies
            foreach (var function in parseResult.Functions)
            {
                _currentFunction = function;
                var main = function.Name.Equals("main", StringComparison.OrdinalIgnoreCase);
                VerifyFunction(function, main, globalVariables, errors);
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
            }

            graph.Data.Structs = _structs.Values.ToList();

            return graph;
        }

        private void VerifyStruct(StructAst structAst, List<TranslationError> errors)
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
                var type = VerifyType(structField.Type, errors);

                if (type == Type.Error)
                {
                    errors.Add(CreateError($"Type '{PrintTypeDefinition(structField.Type)}' of field {structAst.Name}.{structField.Name} is not defined", structField));
                }
                else if (structField.Type.Count != null)
                {
                    if (structField.Type.Count is ConstantAst constant)
                    {
                        if (!uint.TryParse(constant.Value, out _))
                        {
                            errors.Add(CreateError($"Expected type count to be positive integer, but got '{constant.Value}'", constant));
                        }
                    }
                    else
                    {
                        errors.Add(CreateError("Type count should be a constant value", structField.Type.Count));
                    }
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
        }

        private void VerifyFunctionDefinition(FunctionAst function, List<TranslationError> errors)
        {
            // 1. Verify the return type of the function is valid
            var returnType = VerifyType(function.ReturnType, errors);
            if (returnType == Type.Error)
            {
                errors.Add(CreateError($"Return type '{function.ReturnType.Name}' of function '{function.Name}' is not defined", function.ReturnType));
            }
            else if (function.ReturnType.Count != null)
            {
                if (function.ReturnType.Count is ConstantAst constant)
                {
                    if (!uint.TryParse(constant.Value, out _))
                    {
                        errors.Add(CreateError($"Expected type count to be positive integer, but got '{constant.Value}'", constant));
                    }
                }
                else
                {
                    errors.Add(CreateError("Type count should be a constant value", function.ReturnType.Count));
                }
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

                switch (type)
                {
                    case Type.VarArgs:
                        if (function.Varargs || function.Params)
                        {
                            errors.Add(CreateError($"Function '{function.Name}' cannot have multiple varargs", argument.Type));
                        }
                        function.Varargs = true;
                        break;
                    case Type.Params:
                        if (function.Varargs || function.Params)
                        {
                            errors.Add(CreateError($"Function '{function.Name}' cannot have multiple varargs", argument.Type));
                        }
                        function.Params = true;
                        break;
                    case Type.Error:
                        errors.Add(CreateError($"Type '{PrintTypeDefinition(argument.Type)}' of argument '{argument.Name}' in function '{function.Name}' is not defined", argument.Type));
                        break;
                    default:
                        if (function.Varargs)
                        {
                            errors.Add(CreateError($"Cannot declare argument '{argument.Name}' following varargs", argument));
                        }
                        else if (function.Params)
                        {
                            errors.Add(CreateError($"Cannot declare argument '{argument.Name}' following params", argument));
                        }
                        break;
                }
            }
            
            // 3. Load the function into the dictionary 
            if (!_functions.TryAdd(function.Name, function))
            {
                errors.Add(CreateError($"Multiple definitions of function '{function.Name}'", function));
            }
        }

        private void VerifyFunction(FunctionAst function, bool main, IDictionary<string, TypeDefinition> globals, List<TranslationError> errors)
        {
            // 1. Initialize local variables
            var localVariables = new Dictionary<string, TypeDefinition>(globals);
            foreach (var argument in function.Arguments)
            {
                // Arguments with the same name as a global variable will be used instead of the global
                localVariables[argument.Name] = argument.Type;
            }
            var returnType = VerifyType(function.ReturnType, errors);

            // 2. Verify main function return type and arguments
            if (main)
            {
                if (!(returnType == Type.Void || returnType == Type.Int))
                {
                    errors.Add(CreateError("The main function should return type 'int' or 'void'", function));
                }

                var argument = function.Arguments.FirstOrDefault();
                if (argument != null && !(function.Arguments.Count == 1 && argument.Type.Name == "List" && argument.Type.Generics.FirstOrDefault()?.Name == "string"))
                {
                    errors.Add(CreateError("The main function should either have 0 arguments or 'List<string>' argument", function));
                }
            }

            // 3. For extern functions, simply verify there is no body and return
            if (function.Extern)
            {
                if (function.Children.Any())
                {
                    errors.Add(CreateError("Extern function cannot have a body", function));
                }
                return;
            }

            // 4. Loop through function body and verify all ASTs
            var returned = false;
            foreach (var ast in function.Children)
            {
                if (VerifyAst(ast, localVariables, errors))
                {
                    returned = true;
                }
            }

            // 5. Verify the function returns on all paths
            if (!returned && returnType != Type.Void)
            {
                errors.Add(CreateError($"Function '{function.Name}' does not return type '{PrintTypeDefinition(function.ReturnType)}' on all paths", function));
            }
        }

        private bool VerifyScope(List<IAst> syntaxTrees, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, TypeDefinition>(localVariables);

            // 2. Verify function lines
            var returned = false;
            foreach (var syntaxTree in syntaxTrees)
            {
                if (VerifyAst(syntaxTree, scopeVariables, errors))
                {
                    returned = true;
                }
            }
            return returned;
        }

        private bool VerifyAst(IAst syntaxTree, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            switch (syntaxTree)
            {
                case ReturnAst returnAst:
                    VerifyReturnStatement(returnAst, localVariables, _currentFunction.ReturnType, errors);
                    return true;
                case DeclarationAst declaration:
                    VerifyDeclaration(declaration, localVariables, errors);
                    break;
                case AssignmentAst assignment:
                    VerifyAssignment(assignment, localVariables, errors);
                    break;
                case ScopeAst scope:
                    return VerifyScope(scope.Children, localVariables, errors);
                case ConditionalAst conditional:
                    return VerifyConditional(conditional, localVariables, errors);
                case WhileAst whileAst:
                    return VerifyWhile(whileAst, localVariables, errors);
                case EachAst each:
                    return VerifyEach(each, localVariables, errors);
                default:
                    VerifyExpression(syntaxTree, localVariables, errors);
                    break;
            }

            return false;
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
            var variableTypeDefinition = GetVariable(assignment.Variable, localVariables, errors);
            if (variableTypeDefinition == null) return;

            // 2. Verify the assignment value
            var valueType = VerifyExpression(assignment.Value, localVariables, errors);

            // 3. Verify the assignment value matches the variable type definition
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
                    errors.Add(CreateError($"Expected assignment value to be type '{PrintTypeDefinition(variableTypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", assignment.Value));
                }
                else if (variableTypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                {
                    constant.Type.Name = variableTypeDefinition.Name;
                    constant.Type.PrimitiveType = variableTypeDefinition.PrimitiveType;
                    VerifyConstant(constant, errors);
                }
            }
        }

        private TypeDefinition GetVariable(IAst ast, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 2. Get the variable name
            var variableName = ast switch
            {
                VariableAst variable => variable.Name,
                StructFieldRefAst fieldRef => fieldRef.Name,
                IndexAst index => index.Variable switch
                {
                    VariableAst variable => variable.Name,
                    StructFieldRefAst fieldRef => fieldRef.Name,
                    _ => string.Empty
                },
                _ => string.Empty
            };
            if (!localVariables.TryGetValue(variableName, out var variableTypeDefinition))
            {
                errors.Add(CreateError($"Variable '{variableName}' not defined", ast));
                return null;
            }

            // 2. Get the exact type definition
            if (ast is StructFieldRefAst structField)
            {
                variableTypeDefinition = VerifyStructFieldRef(structField, variableTypeDefinition, errors);
                if (variableTypeDefinition == null) return null;
            }
            else if (ast is IndexAst index)
            {
                if (index.Variable is StructFieldRefAst indexStructField)
                {
                    variableTypeDefinition = VerifyStructFieldRef(indexStructField, variableTypeDefinition, errors);
                    if (variableTypeDefinition == null) return null;
                }
                variableTypeDefinition = VerifyIndex(index, variableTypeDefinition, localVariables, errors);
                if (variableTypeDefinition == null) return null;
            }

            return variableTypeDefinition;
        }

        private bool VerifyConditional(ConditionalAst conditional, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
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
            var ifReturned = VerifyScope(conditional.Children, localVariables, errors);

            // 3. Verify the else block if necessary
            if (conditional.Else != null)
            {
                var elseReturned = VerifyAst(conditional.Else, localVariables, errors);
                return ifReturned && elseReturned;
            }

            return false;
        }

        private bool VerifyWhile(WhileAst whileAst, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
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
            return VerifyScope(whileAst.Children, localVariables, errors);
        }

        private bool VerifyEach(EachAst each, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            var eachVariables = new Dictionary<string, TypeDefinition>(localVariables);
            // 1. Verify the iterator or range
            if (eachVariables.ContainsKey(each.IterationVariable))
            {
                errors.Add(CreateError($"Iteration variable '{each.IterationVariable}' already exists in scope", each));
            };
            if (each.Iteration != null)
            {
                var variableTypeDefinition = GetVariable(each.Iteration, localVariables, errors);
                if (variableTypeDefinition == null) return false;
                var iteratorType = variableTypeDefinition.Generics.FirstOrDefault();

                switch (variableTypeDefinition.Name)
                {
                    case "List":
                        each.IteratorType = iteratorType;
                        eachVariables.TryAdd(each.IterationVariable, iteratorType);
                        break;
                    case "Params":
                        if (variableTypeDefinition.Generics.Any())
                        {
                            each.IteratorType = iteratorType;
                            eachVariables.TryAdd(each.IterationVariable, iteratorType);
                        }
                        else
                        {
                            var anyType = new TypeDefinition {Name = "Any"};
                            each.IteratorType = anyType;
                            eachVariables.TryAdd(each.IterationVariable, anyType);
                        }
                        break;
                    default:
                        errors.Add(CreateError($"Type {PrintTypeDefinition(variableTypeDefinition)} cannot be used as an iterator", each.Iteration));
                        break;
                }
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
                var iterType = new TypeDefinition {Name = "int", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};
                if (!eachVariables.TryAdd(each.IterationVariable, iterType))
                {
                    errors.Add(CreateError($"Iteration variable '{each.IterationVariable}' already exists in scope", each));
                };
            }

            // 2. Verify the scope of the each block
            var returned = false;
            foreach (var ast in each.Children)
            {
                if (VerifyAst(ast, eachVariables, errors))
                {
                    returned = true;
                }
            }

            return returned;
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
                        case IndexAst index:
                            var indexType = VerifyIndexType(index, localVariables, errors, out var variableAst);
                            if (indexType != null)
                            {
                                var type = VerifyType(indexType, errors);
                                if (type == Type.Int || type == Type.Float) return indexType;

                                var op = changeByOne.Positive ? "increment" : "decrement";
                                errors.Add(CreateError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(indexType)}'", variableAst));
                            }
                            return null;
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
                        case UnaryOperator.Not:
                            if (type == Type.Boolean)
                            {
                                return valueType;
                            }
                            errors.Add(CreateError($"Expected type 'bool', but got type '{PrintTypeDefinition(valueType)}'", unary.Value));
                            return null;
                        case UnaryOperator.Negate:
                            if (type == Type.Int || type == Type.Float)
                            {
                                return valueType;
                            }
                            errors.Add(CreateError($"Negation not compatible with type '{PrintTypeDefinition(valueType)}'", unary.Value));
                            return null;
                        case UnaryOperator.Dereference:
                            if (type == Type.Pointer)
                            {
                                return valueType.Generics[0];
                            }
                            errors.Add(CreateError($"Cannot dereference type '{PrintTypeDefinition(valueType)}'", unary.Value));
                            return null;
                        case UnaryOperator.Reference:
                            if (unary.Value is VariableAst || unary.Value is StructFieldRefAst || unary.Value is IndexAst || type == Type.Pointer)
                            {
                                var pointerType = new TypeDefinition {Name = "*"};
                                pointerType.Generics.Add(valueType);
                                return pointerType;
                            }
                            errors.Add(CreateError("Can only reference variables, structs, or struct fields", unary.Value));
                            return null;
                        default:
                            errors.Add(CreateError($"Unexpected unary operator '{unary.Operator}'", unary.Value));
                            return null;
                    }
                }
                case CallAst call:
                    if (_functions.TryGetValue(call.Function, out var function))
                    {
                        var argumentCount = function.Varargs || function.Params ? function.Arguments.Count - 1 : function.Arguments.Count;
                        var callArgumentCount = call.Arguments.Count;

                        // Verify function argument count
                        if (function.Varargs || function.Params)
                        {
                            if (argumentCount > callArgumentCount)
                            {
                                errors.Add(CreateError($"Call to function '{function.Name}' expected at least {argumentCount} arguments, but got {callArgumentCount}", call));
                                return null;
                            }
                        }
                        else if (argumentCount != callArgumentCount)
                        {
                            errors.Add(CreateError($"Call to function '{function.Name}' expected {argumentCount} arguments, but got {callArgumentCount}", call));
                            return null;
                        }

                        // Verify call arguments match the types of the function arguments
                        for (var i = 0; i < argumentCount; i++)
                        {
                            var functionType = function.Arguments[i].Type;
                            var argument = call.Arguments[i];
                            var callType = VerifyExpression(argument, localVariables, errors);
                            if (callType != null)
                            {
                                if (!TypeEquals(functionType, callType))
                                {
                                    errors.Add(CreateError($"Call to function '{function.Name}' expected '{PrintTypeDefinition(functionType)}', but got '{PrintTypeDefinition(callType)}'", argument));
                                }
                            }
                        }

                        // Verify varargs call arguments
                        if (function.Varargs || function.Params)
                        {
                            var varargsType = function.Arguments[argumentCount].Type.Generics.FirstOrDefault();

                            // If no generic type, simply verify the rest of the arguments do not have errors
                            if (varargsType == null)
                            {
                                for (var i = argumentCount; i < callArgumentCount; i++)
                                {
                                    VerifyExpression(call.Arguments[i], localVariables, errors);
                                }
                            }
                            else
                            {
                                for (var i = argumentCount; i < callArgumentCount; i++)
                                {
                                    var argument = call.Arguments[i];
                                    var callType = VerifyExpression(argument, localVariables, errors);
                                    if (callType != null)
                                    {
                                        if (!TypeEquals(varargsType, callType))
                                        {
                                            errors.Add(CreateError($"Call to function '{function.Name}' expected '{PrintTypeDefinition(varargsType)}', but got '{PrintTypeDefinition(callType)}'", argument));
                                        }
                                    }
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
                case IndexAst index:
                    return VerifyIndexType(index, localVariables, errors, out _);
                case null:
                    return null;
                default:
                    errors.Add(CreateError($"Unexpected Ast '{ast}'", ast));
                    return null;
            }
        }

        private static void VerifyConstant(ConstantAst constant, List<TranslationError> errors)
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
                        if (((type == Type.Pointer && nextType == Type.Int) ||
                            (type == Type.Int && nextType == Type.Pointer)) &&
                            (op == Operator.Add || op == Operator.Subtract))
                        {
                            if (nextType == Type.Pointer)
                            {
                                expression.Type = nextExpressionType;
                            }
                        }
                        else if ((type == Type.Int || type == Type.Float) &&
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

        private TypeDefinition VerifyIndexType(IndexAst index, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors,
            out IAst variableAst)
        {
            switch (index.Variable)
            {
                case VariableAst variable:
                    variableAst = variable;
                    if (localVariables.TryGetValue(variable.Name, out var variableType))
                    {
                        return VerifyIndex(index, variableType, localVariables, errors);
                    }
                    else
                    {
                        errors.Add(CreateError($"Variable '{variable.Name}' not defined", variable));
                        return null;
                    }
                case StructFieldRefAst structField:
                    variableAst = structField;
                    if (localVariables.TryGetValue(structField.Name, out var structType))
                    {
                        var fieldType = VerifyStructFieldRef(structField, structType, errors);
                        return fieldType == null ? null : VerifyIndex(index, fieldType, localVariables, errors);
                    }
                    else
                    {
                        errors.Add(CreateError($"Variable '{structField.Name}' not defined", structField));
                        return null;
                    }
                default:
                    variableAst = null;
                    errors.Add(CreateError("Expected to index a variable", index));
                    return null;
            }
        }
        
        
        private TypeDefinition VerifyIndex(IndexAst index, TypeDefinition typeDef, IDictionary<string, TypeDefinition> localVariables,
            List<TranslationError> errors)
        {
            // 1. Verify the variable is a list
            var type = VerifyType(typeDef, errors);
            if (type != Type.List && type != Type.Params)
            {
                errors.Add(CreateError($"Cannot index type '{PrintTypeDefinition(typeDef)}'", index.Variable));
                return null;
            }

            // 2. Load the list element type definition
            var indexType = typeDef.Generics.FirstOrDefault();
            if (indexType == null)
            {
                errors.Add(CreateError("Unable to determine element type of the List", index));
            }

            // 3. Verify the count expression is an integer
            var countType = VerifyExpression(index.Index, localVariables, errors);
            if (VerifyType(countType, errors) != Type.Int)
            {
                errors.Add(CreateError($"Expected List index to be type 'int', but got '{PrintTypeDefinition(countType)}'", index));
            }

            return indexType;
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

        private Type VerifyType(TypeDefinition typeDef, List<TranslationError> errors)
        {
            if (typeDef == null) return Type.Error;

            if (typeDef.IsGeneric)
            {
                var hasError = false;
                foreach (var generic in typeDef.Generics)
                {
                    if (VerifyType(generic, errors) == Type.Error)
                    {
                        hasError = true;
                    }
                }
                return hasError ? Type.Error : Type.Other;
            }

            var hasGenerics = typeDef.Generics.Any();
            var hasCount = typeDef.Count != null;
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
                    if (hasCount)
                    {
                        errors.Add(CreateError($"Type '{typeDef.Name}' cannot have count", typeDef));
                        return Type.Error;
                    }
                    return Type.Int;
                case "float":
                case "float64":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("Type 'float' cannot have generics", typeDef));
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        errors.Add(CreateError($"Type '{typeDef.Name}' cannot have count", typeDef));
                        return Type.Error;
                    }
                    return Type.Float;
                case "bool":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("boolean type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        errors.Add(CreateError($"Type '{typeDef.Name}' cannot have count", typeDef));
                        return Type.Error;
                    }
                    return Type.Boolean;
                case "string":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("string type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        errors.Add(CreateError($"Type '{typeDef.Name}' cannot have count", typeDef));
                        return Type.Error;
                    }
                    return Type.String;
                case "List":
                {
                    if (typeDef.Generics.Count != 1)
                    {
                        errors.Add(CreateError($"List type should have 1 generic type, but got {typeDef.Generics.Count}", typeDef));
                        return Type.Error;
                    }
                    return VerifyList(typeDef, errors) ? Type.List : Type.Error; 
                }
                case "void":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("void type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        errors.Add(CreateError($"Type '{typeDef.Name}' cannot have count", typeDef));
                        return Type.Error;
                    }
                    return Type.Void;
                case "*":
                    if (typeDef.Generics.Count != 1)
                    {
                        errors.Add(CreateError($"pointer type should have reference to 1 type, but got {typeDef.Generics.Count}", typeDef));
                        return Type.Error;
                    }
                    return VerifyType(typeDef.Generics[0], errors) == Type.Error ? Type.Error : Type.Pointer;
                case "...":
                    if (hasGenerics)
                    {
                        errors.Add(CreateError("Varargs type cannot have generics", typeDef));
                        return Type.Error;
                    }
                    return Type.VarArgs;
                case "Params":
                {
                    if (typeDef.Generics.Count == 1)
                    {
                        return VerifyList(typeDef, errors) ? Type.Params : Type.Error; 
                    }
                    if (typeDef.Generics.Count > 1)
                    {
                        errors.Add(CreateError($"Params type should have 0 or 1 generic type, but got {typeDef.Generics.Count}", typeDef));
                        return Type.Error;
                    }
                    return Type.Params;
                }
                default:
                    if (typeDef.Generics.Any())
                    {
                        if (!_polymorphicStructs.TryGetValue(typeDef.Name, out var structDef))
                        {
                            errors.Add(CreateError($"No polymorphic structs with name '{typeDef.Name}'", typeDef));
                            return Type.Error;
                        }
                        // TODO Create new struct by copying the polymorphic struct
                        return Type.Error;
                    }
                    return _structs.ContainsKey(typeDef.Name) ? Type.Other : Type.Error;
            }
        }

        private bool VerifyList(TypeDefinition typeDef, List<TranslationError> errors)
        {
            var listType = typeDef.Generics[0];
            var genericType = VerifyType(listType, errors);
            if (genericType == Type.Error)
            {
                return false;
            }

            if (_structs.TryGetValue($"List.{listType.Name}", out _))
            {
                return true;
            }
            if (!_polymorphicStructs.TryGetValue("List", out var structDef))
            {
                errors.Add(CreateError($"No polymorphic structs with name '{typeDef.Name}'", typeDef));
                return false;
            }

            CreatePolymorphedStruct(structDef, listType);
            return true;
        }

        private void CreatePolymorphedStruct(StructAst structAst, params TypeDefinition[] genericTypes)
        {
            var polyStruct = new StructAst
            {
                Name = structAst.Name
            };
            foreach (var field in structAst.Fields)
            {
                if (field.HasGeneric)
                {
                    polyStruct.Fields.Add(new StructFieldAst
                    {
                        Type = CopyType(field.Type, genericTypes), Name = field.Name, DefaultValue = field.DefaultValue
                    });
                }
                else
                {
                    polyStruct.Fields.Add(field);
                }
            }

            foreach (var generic in genericTypes)
            {
                polyStruct.Name += $".{generic.Name}"; 
            }
            _structs.Add(polyStruct.Name, polyStruct);
        }
        
        private static TypeDefinition CopyType(TypeDefinition type, TypeDefinition[] genericTypes)
        {
            if (type.IsGeneric)
            {
                return genericTypes[type.GenericIndex];
            }
            var copyType = new TypeDefinition
            {
                Name = type.Name, IsGeneric = type.IsGeneric, PrimitiveType = type.PrimitiveType, Count = type.Count
            };

            foreach (var generic in type.Generics)
            {
                copyType.Generics.Add(CopyType(generic, genericTypes));
            }

            return copyType;
        }

        private static string PrintTypeDefinition(TypeDefinition type)
        {
            if (type == null) return string.Empty;

            var sb = new StringBuilder();

            if (type.Name == "*")
            {
                sb.Append($"{PrintTypeDefinition(type.Generics[0])}*");
                return sb.ToString();
            }

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
