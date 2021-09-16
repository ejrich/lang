using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lang.Parsing;
using Type = Lang.Parsing.Type;

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

            // TODO Verify undefined calls and types

            return graph;
        }

        private void VerifyFunction(FunctionAst function, bool main, List<TranslationError> errors)
        {
            var localVariables = new Dictionary<string, TypeDefinition>();

            if (main)
            {
                var type = InferType(function.ReturnType, errors);
                if (!(type == Type.Void || type == Type.Int))
                {
                    errors.Add(new TranslationError
                    {
                        Error = "The main function should return type 'int' or 'void'"
                    });
                }
            }

            foreach (var syntaxTree in function.Children)
            {
                switch (syntaxTree)
                {
                    case ReturnAst returnAst:
                        VerifyReturnStatement(returnAst, localVariables, function.ReturnType, errors);
                        break;
                    case DeclarationAst declaration:
                        VerifyDeclaration(declaration, localVariables, errors);
                        break;
                    case AssignmentAst assignment:
                        VerifyAssignment(assignment, localVariables, errors);
                        break;
                    // TODO Handle more syntax trees
                    // default:
                    //     errors.Add(new TranslationError {Error = $"Unexpected syntax tree '{syntaxTree}'"});
                    //     break;
                }
            }
        }

        private void VerifyFunctionDefinition(FunctionAst function, List<TranslationError> errors)
        {
            // 1. Verify the return type of the function is valid
            var returnType = InferType(function.ReturnType, errors);
            if (returnType == Type.Error || (returnType == Type.Other && !_structs.ContainsKey(function.ReturnType.Name)))
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
                var type = InferType(argument.Type, errors);

                if (type == Type.Error || (type == Type.Other && !_structs.ContainsKey(argument.Type.Name)))
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
                var type = InferType(structField.Type, errors);

                if (type == Type.Error || (type == Type.Other && !definedStructs.Contains(structField.Type.Name)))
                {
                    errors.Add(new TranslationError
                    {
                        Error = $"Type '{PrintTypeDefinition(structField.Type)}' of field {structAst.Name}.{structField.Name} is not defined"
                    });
                }

                // 1c. Check if the default value has the correct type
                if (structField.DefaultValue != null && type != structField.DefaultValue.Type)
                {
                    errors.Add(new TranslationError
                    {
                        Error = $"Type of field {structAst.Name}.{structField.Name} is '{type}', but default value is type '{structField.DefaultValue.Type}'"
                    });
                }
            }

            // 2. Load the struct into the dictionary
            _structs.TryAdd(structAst.Name, structAst);
        }

        private void VerifyReturnStatement(ReturnAst returnAst, IDictionary<string, TypeDefinition> localVariables,
            TypeDefinition functionReturnType, List<TranslationError> errors)
        {
            // 1. Infer the return type of the function
            var returnType = InferType(functionReturnType, errors);

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
            switch (returnAst.Value)
            {
                case ConstantAst constant:
                    if (constant.Type != returnType)
                    {
                        errors.Add(new TranslationError {Error = $"Expected to return type '{functionReturnType.Name}', but returned type '{constant.Type}'"});
                    }
                    break;
                case VariableAst variable:
                    if (localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    {
                        var variableType = InferType(typeDefinition, errors);
                        if (variableType != returnType || (variableType == Type.Other && functionReturnType.Name != typeDefinition.Name))
                        {
                            errors.Add(new TranslationError {Error = $"Expected to return type '{PrintTypeDefinition(functionReturnType)}', but returned type '{PrintTypeDefinition(typeDefinition)}'"});
                        }
                    }
                    else
                    {
                        errors.Add(new TranslationError {Error = $"Variable '{variable.Name}' not defined"});
                    }
                    break;
                // TODO Implement these branches
                case CallAst call:
                    break;
                case ExpressionAst expression:
                    break;
                case null:
                    errors.Add(new TranslationError {Error = $"Expected to return type: {functionReturnType.Name}"});
                    break;
            }
        }

        private static void VerifyDeclaration(DeclarationAst declaration, IDictionary<string, TypeDefinition> localVariables,
            List<TranslationError> errors)
        {
            // 1. Verify the variable is already defined
            if (localVariables.ContainsKey(declaration.Name))
            {
                errors.Add(new TranslationError {Error = $"Variable '{declaration.Name}' already defined"});
                return;
            }

            // 2. Verify the assignment value
            Type? valueType = null;
            switch (declaration.Value)
            {
                case ConstantAst constant:
                    valueType = constant.Type;
                    break;
                case VariableAst variable:
                    if (localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    {
                        var variableType = InferType(typeDefinition, errors);
                        if (variableType != Type.Other)
                        {
                            // TODO Lookup struct definition
                        }
                        valueType = variableType;
                    }
                    else
                    {
                        errors.Add(new TranslationError {Error = $"Variable '{variable.Name}' not found"});
                    }
                    break;
                // TODO Implement these branches
                case CallAst call:
                    break;
                case ExpressionAst expression:
                    break;
            }

            // 3. Verify the assignment value matches the type definition if it has been defined
            if (declaration.Type == null)
            {
                // TODO Assign this to the type field on assignment
                declaration.Type = new TypeDefinition {Name = valueType.ToString().ToLower()};
            }
            else
            {
                var type = InferType(declaration.Type, errors);
                // Verify the type is correct
                if (valueType != null)
                {
                    if (type != valueType)
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
            Type? valueType = null;
            switch (assignment.Value)
            {
                case ConstantAst constant:
                    valueType = constant.Type;
                    break;
                case VariableAst variable:
                    if (localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    {
                        var variableType = InferType(typeDefinition, errors);
                        if (variableType != Type.Other)
                        {
                            // TODO Lookup struct definition
                        }
                        valueType = variableType;
                    }
                    else
                    {
                        errors.Add(new TranslationError {Error = $"Variable '{variable.Name}' not found"});
                    }
                    break;
                // TODO Implement these branches
                case CallAst call:
                    break;
                case ExpressionAst expression:
                    break;
            }

            // 3. Verify the assignment value matches the variable type definition
            var type = InferType(variableTypeDefinition, errors);
            if (assignment.Variable is StructFieldRefAst structField)
            {
                type = InferType(VerifyStructFieldRef(structField, variableTypeDefinition, errors), errors);
            }
            if (valueType != null)
            {
                if (type != valueType)
                {
                    errors.Add(new TranslationError {Error = $"Expected assignment value to be type '{PrintTypeDefinition(variableTypeDefinition)}'"});
                }
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

        private static Type InferType(TypeDefinition typeDef, List<TranslationError> errors)
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
                    return Type.Other;
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
