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
                    errors.Add(new TranslationError {Error = $"Multiple definitions of struct '{structAst.Name}'"});
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
                    errors.Add(new TranslationError {Error = $"Struct '{structAst.Name}' already contains field '{structField.Name}'"});
                }

                // 1b. Check for errored or undefined field types
                var type = VerifyType(structField.Type, errors, false);

                if (type == Type.Error || (type == Type.Other && !definedStructs.Contains(structField.Type.Name)))
                {
                    errors.Add(new TranslationError
                    {
                        Error = $"Type '{PrintTypeDefinition(structField.Type)}' of field {structAst.Name}.{structField.Name} is not defined"
                    });
                }

                // 1c. Check if the default value has the correct type
                if (structField.DefaultValue != null)
                {
                    if (!TypeEquals(structField.Type, structField.DefaultValue.Type))
                    {
                        errors.Add(new TranslationError
                        {
                            Error = $"Type of field {structAst.Name}.{structField.Name} is '{type}', but default value is type '{structField.DefaultValue.Type}'"
                        });
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
                errors.Add(new TranslationError
                {
                    Error = $"Return type '{function.ReturnType.Name}' of function '{function.Name}' is not defined"
                });
            }

            // 2. Verify the argument types
            var argumentNames = new HashSet<string>();
            foreach (var argument in function.Arguments)
            {
                // 1a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    errors.Add(new TranslationError
                    {
                        Error = $"Function '{function.Name}' already contains argument '{argument.Name}'"
                    });
                }

                // 1b. Check for errored or undefined field types
                var type = VerifyType(argument.Type, errors);

                if (type == Type.Error)
                {
                    errors.Add(new TranslationError
                    {
                        Error = $"Type '{PrintTypeDefinition(argument.Type)}' of argument '{argument.Name}' in function '{function.Name}' is not defined"
                    });
                }
            }
            
            // 3. Load the function into the dictionary 
            if (!_functions.TryAdd(function.Name, function))
            {
                errors.Add(new TranslationError {Error = $"Multiple definitions of function '{function.Name}'"});
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
                    errors.Add(new TranslationError
                    {
                        Error = "The main function should return type 'int' or 'void'"
                    });
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
                    errors.Add(new TranslationError {Error = "Function return should be void"});
                }
                return;
            }

            // 3. Determine if the expression returns the correct value
            var returnValueType = VerifyExpression(returnAst.Value, localVariables, errors);
            if (returnValueType == null)
            {
                errors.Add(new TranslationError {Error = $"Expected to return type '{functionReturnType.Name}'"});
            }
            else
            {
                if (!TypeEquals(functionReturnType, returnValueType))
                {
                    errors.Add(new TranslationError
                    {
                        Error = $"Expected to return type '{PrintTypeDefinition(functionReturnType)}', but returned type '{PrintTypeDefinition(returnValueType)}'"
                    });
                }
            }
        }

        private void VerifyDeclaration(DeclarationAst declaration, IDictionary<string, TypeDefinition> localVariables,
            List<TranslationError> errors)
        {
            // 1. Verify the variable is already defined
            if (localVariables.ContainsKey(declaration.Name))
            {
                errors.Add(new TranslationError {Error = $"Variable '{declaration.Name}' already defined"});
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
                    errors.Add(new TranslationError {Error = $"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'"});
                }

                // Verify the type is correct
                if (valueType != null)
                {
                    if (!TypeEquals(declaration.Type, valueType))
                    {
                        errors.Add(new TranslationError {Error = $"Expected declaration value to be type '{PrintTypeDefinition(declaration.Type)}'"});
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
                errors.Add(new TranslationError {Error = $"Variable '{variableName}' not defined"});
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
                if (!TypeEquals(variableTypeDefinition, valueType))
                {
                    errors.Add(new TranslationError {Error = $"Expected assignment value to be type '{PrintTypeDefinition(variableTypeDefinition)}'"});
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
                    errors.Add(new TranslationError {Error = $"Expected condition to be int, float, or bool, but got '{PrintTypeDefinition(conditionalType)}'"});
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
                    errors.Add(new TranslationError {Error = $"Expected condition to be int, float, or bool, but got '{PrintTypeDefinition(conditionalType)}'"});
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
                    errors.Add(new TranslationError {Error = $"Expected range to begin with an int, but got '{PrintTypeDefinition(beginType)}'"});
                }
                var endType = VerifyExpression(each.RangeBegin, localVariables, errors);
                if (VerifyType(beginType, errors) != Type.Int)
                {
                    errors.Add(new TranslationError {Error = $"Expected range to end with an int, but got '{PrintTypeDefinition(beginType)}'"});
                }
                eachVariables.Add(each.IterationVariable, new TypeDefinition {Name = "int"});
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
                        errors.Add(new TranslationError {Error = $"Variable '{structField.Name}' not defined"});
                        return null;
                    }
                    return VerifyStructFieldRef(structField, structType, errors);
                }
                case VariableAst variable:
                    if (!localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    {
                        errors.Add(new TranslationError {Error = $"Variable '{variable.Name}' not defined"});
                    }
                    return typeDefinition;
                case ChangeByOneAst changeByOne:
                    var op = changeByOne.Operator == Operator.Increment ? "increment" : "decrement";
                    switch (changeByOne.Variable)
                    {
                        case VariableAst variable:
                            if (localVariables.TryGetValue(variable.Name, out var variableType))
                            {
                                var type = VerifyType(variableType, errors);
                                if (type == Type.Int || type == Type.Float) return variableType;
                                
                                errors.Add(new TranslationError {Error = $"Expected to {op} int or float, but got type '{PrintTypeDefinition(variableType)}'"});
                                return null;
                            }
                            else
                            {
                                errors.Add(new TranslationError {Error = $"Variable '{variable.Name}' not defined"});
                                return null;
                            }
                        case StructFieldRefAst structField:
                            if (localVariables.TryGetValue(structField.Name, out var structType))
                            {
                                var fieldType = VerifyStructFieldRef(structField, structType, errors);
                                if (fieldType == null) return null;

                                var type = VerifyType(fieldType, errors);
                                if (type == Type.Int || type == Type.Float) return fieldType;

                                errors.Add(new TranslationError {Error = $"Expected to {op} int or float, but got type '{PrintTypeDefinition(fieldType)}'"});
                                return null;
                            }
                            else
                            {
                                errors.Add(new TranslationError {Error = $"Variable '{structField.Name}' not defined"});
                                return null;
                            }
                        default:
                            errors.Add(new TranslationError {Error = $"Expected to {op} variable"});
                            return null;
                    }
                case CallAst call:
                    if (_functions.TryGetValue(call.Function, out var function))
                    {
                        // Verify function arguments
                        if (function.Arguments.Count != call.Arguments.Count)
                        {
                            errors.Add(new TranslationError
                            {
                                Error = $"Call to function '{function.Name}' expected {function.Arguments.Count} arguments, but got {call.Arguments.Count}"
                            });
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
                                    errors.Add(new TranslationError
                                    {
                                        Error = $"Call to function '{function.Name}' expected '{PrintTypeDefinition(functionType)}', but got '{PrintTypeDefinition(callType)}'"
                                    });
                                }
                            }
                        }
                    }
                    else
                    {
                        errors.Add(new TranslationError {Error = $"Call to undefined function '{call.Function}'"});
                    }
                    return function?.ReturnType;
                case ExpressionAst expression:
                    var expressionType = VerifyExpression(expression.Children[0], localVariables, errors);
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var nextType = VerifyExpression(expression.Children[i], localVariables, errors);
                        if (nextType == null) return null;

                        if (TypeEquals(expressionType, nextType)) continue;

                        errors.Add(new TranslationError {Error = $"Type mismatch between '{PrintTypeDefinition(expressionType)}' and '{PrintTypeDefinition(nextType)}'"});
                        return null;
                        // TODO Implement type interpretation based on the operators
                    }
                    return expressionType;
                case null:
                    return null;
                default:
                    errors.Add(new TranslationError {Error = $"Unexpected Ast '{ast}'"});
                    return null;
            }
        }

        private TypeDefinition VerifyStructFieldRef(StructFieldRefAst structField, TypeDefinition structType,
            List<TranslationError> errors)
        {
            // 1. Load the struct definition in typeDefinition
            if (!_structs.TryGetValue(structType.Name, out var structDefinition))
            {
                errors.Add(new TranslationError {Error = $"Struct '{structType.Name}' not defined"});
                return null;
            }

            // 2. If the type of the field is other, recurse and return
            var value = structField.Value;
            var field = structDefinition.Fields.FirstOrDefault(_ => _.Name == value.Name);
            if (field == null)
            {
                errors.Add(new TranslationError {Error = $"Struct '{structType.Name}' does not contain field '{value.Name}'"});
                return null;
            }

            return value.Value == null ? field.Type : VerifyStructFieldRef(value, field.Type, errors);
        }

        private static bool TypeEquals(TypeDefinition a, TypeDefinition b)
        {
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
                    if (hasGenerics)
                    {
                        errors.Add(new TranslationError {Error = "int type cannot have generics"});
                        return Type.Error;
                    }
                    return Type.Int;
                case "float":
                    if (hasGenerics)
                    {
                        errors.Add(new TranslationError {Error = "float type cannot have generics"});
                        return Type.Error;
                    }
                    return Type.Float;
                case "bool":
                    if (hasGenerics)
                    {
                        errors.Add(new TranslationError {Error = "boolean type cannot have generics"});
                        return Type.Error;
                    }
                    return Type.Boolean;
                case "string":
                    if (hasGenerics)
                    {
                        errors.Add(new TranslationError {Error = "string type cannot have generics"});
                        return Type.Error;
                    }
                    return Type.String;
                case "List":
                    return Type.List;
                case "void":
                    if (hasGenerics)
                    {
                        errors.Add(new TranslationError {Error = "void type cannot have generics"});
                        return Type.Error;
                    }
                    return Type.Void;
                default:
                    if (!verifyStruct) return Type.Other;
                    return _structs.ContainsKey(typeDef.Name) ? Type.Other : Type.Error;
            }
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
    }
}
