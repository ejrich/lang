using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IDictionary<string, FunctionAst> _functions = new Dictionary<string, FunctionAst>();
        private readonly IDictionary<string, StructAst> _structs = new Dictionary<string, StructAst>();
        private readonly List<CallAst> _undefinedCalls = new();
        private readonly List<string> _undefinedTypes = new();

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors)
        {
            errors = new List<TranslationError>();
            var graph = new ProgramGraph();

            foreach (var syntaxTree in parseResult.SyntaxTrees)
            {
                switch (syntaxTree)
                {
                    case FunctionAst function:
                        var main = function.Name.Equals("main", StringComparison.OrdinalIgnoreCase);
                        var functionErrors = VerifyFunction(function, main);
                        if (functionErrors.Any())
                        {
                            errors.AddRange(functionErrors);
                        }
                        else
                        {
                            if (main)
                            {
                                graph.Main = function;
                            }
                            else
                            {
                                _functions.Add(function.Name, function);
                            }
                        }
                        break;
                    case StructAst structAst:
                        var structErrors = VerifyStruct(structAst);
                        if (structErrors.Any())
                        {
                            errors.AddRange(structErrors);
                        }
                        else
                        {
                            _structs.Add(structAst.Name, structAst);
                        }
                        break;
                    // TODO Handle more type of ASTs
                }
            }

            // TODO Verify undefined calls and types

            return graph;
        }

        private List<TranslationError> VerifyFunction(FunctionAst function, bool main)
        {
            var translationErrors = new List<TranslationError>();

            var localVariables = new Dictionary<string, TypeDefinition>();

            if (main)
            {
                var type = InferType(function.ReturnType, translationErrors);
                if (!(type == Type.Void || type == Type.Int))
                {
                    translationErrors.Add(new TranslationError
                    {
                        Error = "The main function should be of type 'int' or 'void'"
                    });
                }
            }

            foreach (var syntaxTree in function.Children)
            {
                switch (syntaxTree)
                {
                    case ReturnAst returnAst:
                        VerifyReturnStatement(returnAst, localVariables, function.ReturnType, translationErrors);
                        break;
                    case DeclarationAst declaration:
                        VerifyDeclaration(declaration, localVariables, translationErrors);
                        break;
                    case AssignmentAst assignment:
                        VerifyAssignment(assignment, localVariables, translationErrors);
                        break;
                    // TODO Handle more syntax trees
                }
            }

            return translationErrors;
        }

        private List<TranslationError> VerifyStruct(StructAst structAst)
        {
            var translationErrors = new List<TranslationError>();

            // 1. Verify the struct hasn't been defined previously
            if (_structs.ContainsKey(structAst.Name))
            {
                translationErrors.Add(new TranslationError {Error = $"Previously defined struct '{structAst.Name}'"});
            }

            // 2. Verify struct fields have valid types
            var fieldNames = new HashSet<string>();
            foreach (var structField in structAst.Fields)
            {
                if (fieldNames.Contains(structField.Name))
                {
                    translationErrors.Add(new TranslationError {Error = $"Struct '{structAst.Name}' already contains field '{structField.Name}'"});
                }
                else
                {
                    fieldNames.Add(structField.Name);
                }

                var type = InferType(structField.Type, translationErrors);

                if (type == Type.Other)
                {
                    // TODO Lookup struct definition
                }

                if (structField.DefaultValue != null && type != structField.DefaultValue.Type)
                {
                    translationErrors.Add(new TranslationError
                    {
                        Error = $"Type of field {structAst.Name}.{structField.Name} is '{type}', but default value is type '{structField.DefaultValue.Type}'"
                    });
                }
            }

            return translationErrors;
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
                        errors.Add(new TranslationError {Error = $"Expected to return type '{functionReturnType.Type}', but returned type '{constant.Type}'"});
                    }
                    break;
                case VariableAst variable:
                    if (localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    {
                        var variableType = InferType(typeDefinition, errors);
                        if (variableType != returnType || (variableType == Type.Other && functionReturnType.Type != typeDefinition.Type))
                        {
                            errors.Add(new TranslationError {Error = $"Expected to return type '{functionReturnType.Type}', but returned type '{typeDefinition.Type}'"});
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
                    errors.Add(new TranslationError {Error = $"Expected to return type: {functionReturnType.Type}"});
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
                declaration.Type = new TypeDefinition {Type = valueType.ToString()};
            }
            else
            {
                var type = InferType(declaration.Type, errors);
                // Verify the type is correct
                if (valueType != null)
                {
                    if (type != valueType)
                    {
                        errors.Add(new TranslationError {Error = $"Expected declaration value to be type '{declaration.Type.Type}'"});
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
                StructFieldRefAst fieldRef => fieldRef.Name
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
            // TODO Implement struct field ref type traversing verification
            var type = InferType(variableTypeDefinition, errors);
            if (assignment.Variable is StructFieldRefAst structField)
            {
                type = InferType(VerifyStructFieldRef(structField, variableTypeDefinition, errors), errors);
            }
            if (valueType != null)
            {
                if (type != valueType)
                {
                    errors.Add(new TranslationError {Error = $"Expected assignment value to be type '{variableTypeDefinition.Type}'"});
                }
            }
        }

        private TypeDefinition VerifyStructFieldRef(StructFieldRefAst structField, TypeDefinition structType,
            List<TranslationError> errors)
        {
            // 1. Load the struct definition in typeDefinition
            if (!_structs.TryGetValue(structType.Type, out var structDefinition))
            {
                errors.Add(new TranslationError());
                return null;
            }

            // 2. If the type of the field is other, recurse and return
            var value = structField.Value;
            var field = structDefinition.Fields.FirstOrDefault(_ => _.Name == value.Name);
            if (field == null)
            {
                errors.Add(new TranslationError {Error = $"Struct '{structType.Type}' does not contain field '{value.Name}'"});
                return null;
            }

            return value.Value == null ? field.Type : VerifyStructFieldRef(value, field.Type, errors);
        }

        private static Type InferType(TypeDefinition typeDef, List<TranslationError> errors)
        {
            var hasGenerics = typeDef.Generics.Any();
            switch (typeDef.Type.ToLower())
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
    }
}
