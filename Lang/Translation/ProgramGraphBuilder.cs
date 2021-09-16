using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lang.Parsing;
using Lang.Runner;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        private readonly IProgramRunner _programRunner;

        private readonly ProgramGraph _programGraph = new();
        private readonly Dictionary<string, StructAst> _polymorphicStructs = new();
        private readonly Dictionary<string, TypeDefinition> _globalVariables = new();

        public ProgramGraphBuilder(IProgramRunner programRunner)
        {
            _programRunner = programRunner;
        }

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors)
        {
            errors = new List<TranslationError>();
            var mainDefined = false;
            bool verifyAdditional;

            do
            {
                // 1. Verify enum and struct definitions
                for (var i = 0; i < parseResult.SyntaxTrees.Count; i++)
                {
                    switch (parseResult.SyntaxTrees[i])
                    {
                        case EnumAst enumAst:
                            VerifyEnum(enumAst, errors);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case StructAst structAst:
                            if (_programGraph.Types.ContainsKey(structAst.Name))
                            {
                                errors.Add(CreateError($"Multiple definitions of struct '{structAst.Name}'", structAst));
                            }
                            else if (structAst.Generics.Any())
                            {
                                _polymorphicStructs.Add(structAst.Name, structAst);
                            }
                            else
                            {
                                _programGraph.Types.Add(structAst.Name, structAst);
                            }

                            break;
                    }
                }

                // 2. Verify struct bodies, global variables, and function return types and arguments
                for (var i = 0; i < parseResult.SyntaxTrees.Count; i++)
                {
                    switch (parseResult.SyntaxTrees[i])
                    {
                        case StructAst structAst:
                            VerifyStruct(structAst, errors);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case DeclarationAst globalVariable:
                            if (globalVariable.Value != null && globalVariable.Value is not ConstantAst)
                            {
                                errors.Add(CreateError(
                                    "Global variable must either not be initialized or be initialized to a constant value", globalVariable.Value));
                            }

                            VerifyDeclaration(globalVariable, null, _globalVariables, errors);
                            _programGraph.Variables.Add(globalVariable);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case FunctionAst function:
                            var main = function.Name == "main";
                            if (main)
                            {
                                if (mainDefined)
                                {
                                    errors.Add(CreateError("Only one main function can be defined", function));
                                }

                                mainDefined = true;
                            }
                            else if (function.Name == "__start")
                            {
                                _programGraph.Start = function;
                            }

                            VerifyFunctionDefinition(function, main, errors);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                    }
                }

                verifyAdditional = false;
                var additionalAsts = new List<IAst>();
                // 3. Verify and run top-level static ifs
                for (int i = 0; i < parseResult.SyntaxTrees.Count; i++)
                {
                    switch (parseResult.SyntaxTrees[i])
                    {
                        case CompilerDirectiveAst directive:
                            if (directive.Type == DirectiveType.If)
                            {
                                var conditional = directive.Value as ConditionalAst;
                                if (VerifyCondition(conditional!.Condition, null, _globalVariables, errors))
                                {
                                    // Initialize program runner
                                    _programRunner.Init(_programGraph);
                                    // Run the condition
                                    if (_programRunner.ExecuteCondition(conditional!.Condition, _programGraph))
                                    {
                                        additionalAsts.AddRange(conditional.Children);
                                    }
                                    else if (conditional.Else.Any())
                                    {
                                        additionalAsts.AddRange(conditional.Else);
                                    }
                                }
                                parseResult.SyntaxTrees.RemoveAt(i--);
                            }
                            break;
                    }
                }
                if (additionalAsts.Any())
                {
                    parseResult.SyntaxTrees.AddRange(additionalAsts);
                    verifyAdditional = true;
                }
            } while (verifyAdditional);

            // 4. Verify function bodies
            foreach (var (_, function) in _programGraph.Functions)
            {
                if (function.Verified) continue;
                VerifyFunction(function, errors);
            }
            VerifyFunction(_programGraph.Start, errors);

            // 5. Execute any other compiler directives
            foreach (var ast in parseResult.SyntaxTrees)
            {
                switch (ast)
                {
                    case CompilerDirectiveAst compilerDirective:
                        VerifyTopLevelDirective(compilerDirective, errors);
                        _programGraph.Directives.Add(compilerDirective);
                        break;
                }
            }

            if (!mainDefined)
            {
                // @Cleanup allow errors to be reported without having a file/line/column
                errors.Add(new TranslationError { Error = "'main' function of the program is not defined" });
            }

            return _programGraph;
        }

        private void VerifyEnum(EnumAst enumAst, List<TranslationError> errors)
        {
            // 1. Verify enum has not already been defined
            if (!_programGraph.Types.TryAdd(enumAst.Name, enumAst))
            {
                errors.Add(CreateError($"Multiple definitions of enum '{enumAst.Name}'", enumAst));
            }

            // 2. Verify enums don't have repeated values
            var valueNames = new HashSet<string>();
            var values = new HashSet<int>();
            var largestValue = -1;
            foreach (var value in enumAst.Values)
            {
                // 2a. Check if the value has been previously defined
                if (!valueNames.Add(value.Name))
                {
                    errors.Add(CreateError($"Enum '{enumAst.Name}' already contains value '{value.Name}'", value));
                }

                // 2b. Check if the value has been previously used
                if (value.Defined)
                {
                    if (!values.Add(value.Value))
                    {
                        errors.Add(CreateError($"Value '{value.Value}' previously defined in enum '{enumAst.Name}'", value));
                    }
                    else if (value.Value > largestValue)
                    {
                        largestValue = value.Value;
                    }
                }
                // 2c. Assign the value if not specified
                else
                {
                    value.Value = ++largestValue;
                }
            }
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
                switch (structField.DefaultValue)
                {
                    case ConstantAst constant:
                        if (!TypeEquals(structField.Type, constant.Type))
                        {
                            errors.Add(CreateError($"Type of field {structAst.Name}.{structField.Name} is '{PrintTypeDefinition(structField.Type)}', but default value is type '{PrintTypeDefinition(constant.Type)}'", constant));
                        }
                        break;
                    case StructFieldRefAst structFieldRef:
                        if (_programGraph.Types.TryGetValue(structFieldRef.Name, out var fieldType))
                        {
                            if (fieldType is EnumAst enumAst)
                            {
                                var enumType = VerifyEnumValue(structFieldRef, enumAst, errors);
                                if (enumType != null && !TypeEquals(structField.Type, enumType))
                                {
                                    errors.Add(CreateError($"Type of field {structAst.Name}.{structField.Name} is '{PrintTypeDefinition(structField.Type)}', but default value is type '{PrintTypeDefinition(enumType)}'", structFieldRef));
                                }
                            }
                            else
                            {
                                errors.Add(CreateError($"Default value must be constant or enum value, but got field of '{structFieldRef.Name}'", structFieldRef));
                            }
                        }
                        else
                        {
                            errors.Add(CreateError($"Type '{structFieldRef.Name}' not defined", structFieldRef));
                        }
                        break;
                }
            }
        }

        private void VerifyFunctionDefinition(FunctionAst function, bool main, List<TranslationError> errors)
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

            // 3. Verify the argument types
            var argumentNames = new HashSet<string>();
            foreach (var argument in function.Arguments)
            {
                // 3a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    errors.Add(CreateError($"Function '{function.Name}' already contains argument '{argument.Name}'", argument));
                }

                // 3b. Check for errored or undefined field types
                var type = VerifyType(argument.Type, errors);

                switch (type)
                {
                    case Type.VarArgs:
                        if (function.Varargs || function.Params)
                        {
                            errors.Add(CreateError($"Function '{function.Name}' cannot have multiple varargs", argument.Type));
                        }
                        function.Varargs = true;
                        function.VarargsCalls = new List<List<TypeDefinition>>();
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

                // 3c. Check for default arguments
                if (argument.DefaultValue != null)
                {
                    switch (argument.DefaultValue)
                    {
                        case ConstantAst constantAst:
                            if (!TypeEquals(argument.Type, constantAst.Type))
                            {
                                errors.Add(CreateError($"Default function argument expected type '{PrintTypeDefinition(argument.Type)}', but got '{PrintTypeDefinition(constantAst.Type)}'", argument.DefaultValue));
                            }
                            break;
                        case NullAst nullAst:
                            if (type != Type.Pointer)
                            {
                                errors.Add(CreateError("Default function argument can only be null for pointers", argument.DefaultValue));
                            }
                            nullAst.TargetType = argument.Type;
                            break;
                        default:
                            errors.Add(CreateError("Default function argument should be a constant or null", argument.DefaultValue));
                            break;
                    }
                }
            }

            // 4. Load the function into the dictionary
            if (!_programGraph.Functions.TryAdd(function.Name, function))
            {
                errors.Add(CreateError($"Multiple definitions of function '{function.Name}'", function));
            }
        }

        private void VerifyFunction(FunctionAst function, List<TranslationError> errors)
        {
            // 1. Initialize local variables
            var localVariables = new Dictionary<string, TypeDefinition>(_globalVariables);
            foreach (var argument in function.Arguments)
            {
                // Arguments with the same name as a global variable will be used instead of the global
                localVariables[argument.Name] = argument.Type;
            }
            var returnType = VerifyType(function.ReturnType, errors);

            // 2. For extern functions, simply verify there is no body and return
            if (function.Extern)
            {
                if (function.Children.Any())
                {
                    errors.Add(CreateError("Extern function cannot have a body", function));
                }
                function.Verified = true;
                return;
            }

            // 3. Resolve the compiler directives in the function
            ResolveCompilerDirectives(function.Children, errors);

            // 4. Loop through function body and verify all ASTs
            var returned = VerifyAsts(function.Children, function, localVariables, errors);

            // 5. Verify the function returns on all paths
            if (!returned && returnType != Type.Void)
            {
                errors.Add(CreateError($"Function '{function.Name}' does not return type '{PrintTypeDefinition(function.ReturnType)}' on all paths", function));
            }
            function.Verified = true;
        }

        private void ResolveCompilerDirectives(List<IAst> asts, List<TranslationError> errors)
        {
            for (int i = 0; i < asts.Count; i++)
            {
                var ast = asts[i];
                switch (ast)
                {
                    case ScopeAst:
                    case WhileAst:
                    case EachAst:
                        ResolveCompilerDirectives(ast.Children, errors);
                        break;
                    case ConditionalAst conditional:
                        ResolveCompilerDirectives(conditional.Children, errors);
                        ResolveCompilerDirectives(conditional.Else, errors);
                        break;
                    case CompilerDirectiveAst directive:
                        switch (directive.Type)
                        {
                            case DirectiveType.If:
                                asts.RemoveAt(i);

                                var conditional = directive.Value as ConditionalAst;
                                if (VerifyCondition(conditional!.Condition, null, _globalVariables, errors))
                                {
                                    // Initialize program runner
                                    _programRunner.Init(_programGraph);
                                    // Run the condition
                                    if (_programRunner.ExecuteCondition(conditional!.Condition, _programGraph))
                                    {
                                        asts.InsertRange(i, conditional.Children);
                                    }
                                    else if (conditional.Else.Any())
                                    {
                                        asts.InsertRange(i, conditional.Else);
                                    }
                                }
                                i--;
                                break;
                        }
                        break;
                }
            }
        }

        private bool VerifyAsts(List<IAst> asts, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            var returns = false;
            foreach (var ast in asts)
            {
                if (VerifyAst(ast, currentFunction, localVariables, errors))
                {
                    returns = true;
                }
            }
            return returns;
        }

        private bool VerifyScope(List<IAst> syntaxTrees, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, TypeDefinition>(localVariables);

            // 2. Verify function lines
            return VerifyAsts(syntaxTrees, currentFunction, scopeVariables, errors);
        }

        private bool VerifyAst(IAst syntaxTree, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            switch (syntaxTree)
            {
                case ReturnAst returnAst:
                    VerifyReturnStatement(returnAst, currentFunction, localVariables, errors);
                    return true;
                case DeclarationAst declaration:
                    VerifyDeclaration(declaration, currentFunction, localVariables, errors);
                    break;
                case AssignmentAst assignment:
                    VerifyAssignment(assignment, currentFunction, localVariables, errors);
                    break;
                case ScopeAst scope:
                    return VerifyScope(scope.Children, currentFunction, localVariables, errors);
                case ConditionalAst conditional:
                    return VerifyConditional(conditional, currentFunction, localVariables, errors);
                case WhileAst whileAst:
                    return VerifyWhile(whileAst, currentFunction, localVariables, errors);
                case EachAst each:
                    return VerifyEach(each, currentFunction, localVariables, errors);
                case CompilerDirectiveAst directive:
                    return VerifyCompilerDirective(directive, currentFunction, localVariables, errors);
                default:
                    VerifyExpression(syntaxTree, currentFunction, localVariables, errors);
                    break;
            }

            return false;
        }

        private void VerifyReturnStatement(ReturnAst returnAst, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables,
            List<TranslationError> errors)
        {
            // 1. Infer the return type of the function
            var returnType = VerifyType(currentFunction.ReturnType, errors);

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
            var returnValueType = VerifyExpression(returnAst.Value, currentFunction, localVariables, errors);
            if (returnValueType == null)
            {
                errors.Add(CreateError($"Expected to return type '{PrintTypeDefinition(currentFunction.ReturnType)}'", returnAst));
            }
            else
            {
                if (!TypeEquals(currentFunction.ReturnType, returnValueType))
                {
                    errors.Add(CreateError($"Expected to return type '{PrintTypeDefinition(currentFunction.ReturnType)}', but returned type '{PrintTypeDefinition(returnValueType)}'", returnAst.Value));
                }
            }
        }

        private void VerifyDeclaration(DeclarationAst declaration, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables,
            List<TranslationError> errors)
        {
            // 1. Verify the variable is already defined
            if (localVariables.ContainsKey(declaration.Name))
            {
                errors.Add(CreateError($"Variable '{declaration.Name}' already defined", declaration));
                return;
            }

            // 2. Verify the null values
            if (declaration.Value is NullAst nullAst)
            {
                // 2a. Verify null can be assigned
                if (declaration.Type == null)
                {
                    errors.Add(CreateError("Cannot assign null value without declaring a type", declaration.Value));
                }
                else
                {
                    var type = VerifyType(declaration.Type, errors);
                    if (type == Type.Error)
                    {
                        errors.Add(CreateError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type));
                    }
                    else if (type != Type.Pointer)
                    {
                        errors.Add(CreateError("Cannot assign null to non-pointer type", declaration.Value));
                    }

                    nullAst.TargetType = declaration.Type;
                }
            }
            // 3. Verify object initializers
            else if (declaration.Assignments.Any())
            {
                if (declaration.Type == null)
                {
                    errors.Add(CreateError("Struct literals are not yet supported", declaration));
                }
                else
                {
                    var type = VerifyType(declaration.Type, errors);
                    if (type != Type.Struct)
                    {
                        errors.Add(CreateError($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.Type)}'", declaration.Type));
                        return;
                    }

                    var structDef = _programGraph.Types[declaration.Type.GenericName] as StructAst;
                    var fields = structDef!.Fields.ToDictionary(_ => _.Name);
                    foreach (var assignment in declaration.Assignments)
                    {
                        StructFieldAst field = null;
                        if (assignment.Variable is not VariableAst variableAst)
                        {
                            errors.Add(CreateError("Expected to get field in object initializer", assignment.Variable));
                        }
                        else if (!fields.TryGetValue(variableAst.Name, out field))
                        {
                            errors.Add(CreateError($"Field '{variableAst.Name}' not present in struct '{PrintTypeDefinition(declaration.Type)}'", assignment.Variable));
                        }

                        if (assignment.Operator != Operator.None)
                        {
                            errors.Add(CreateError("Cannot have operator assignments in object initializers", assignment.Variable));
                        }

                        var valueType = VerifyExpression(assignment.Value, currentFunction, localVariables, errors);
                        if (valueType != null && field != null)
                        {
                            if (!TypeEquals(field.Type, valueType))
                            {
                                errors.Add(CreateError($"Expected field value to be type '{PrintTypeDefinition(field.Type)}', " +
                                    $"but got '{PrintTypeDefinition(valueType)}'", field.Type));
                            }
                            else if (field.Type.PrimitiveType != null && assignment.Value is ConstantAst constant)
                            {
                                VerifyConstant(constant, field.Type, errors);
                            }
                        }
                    }
                }
            }
            // 4. Verify declaration values
            else
            {
                var valueType = VerifyExpression(declaration.Value, currentFunction, localVariables, errors);

                // 4a. Verify the assignment value matches the type definition if it has been defined
                if (declaration.Type == null)
                {
                    if (valueType.Name == "void")
                    {
                        errors.Add(CreateError($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value));
                        return;
                    }
                    declaration.Type = valueType;
                }
                else
                {
                    var type = VerifyType(declaration.Type, errors);
                    if (type == Type.Error)
                    {
                        errors.Add(CreateError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type));
                    }
                    else if (type == Type.Void)
                    {
                        errors.Add(CreateError($"Variables cannot be assigned type 'void'", declaration.Type));
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
                            VerifyConstant(constant, declaration.Type, errors);
                        }
                    }
                }
            }

            // 5. Verify the type definition count if necessary
            if (declaration.Type?.Count != null)
            {
                VerifyExpression(declaration.Type.Count, currentFunction, localVariables, errors);
            }

            // 6. Verify constant values
            if (declaration.Constant)
            {
                if (declaration.Value == null || declaration.Value is not ConstantAst)
                {
                    errors.Add(CreateError($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration));
                }
                if (declaration.Type != null)
                {
                    declaration.Type.Constant = true;
                }
            }

            localVariables.Add(declaration.Name, declaration.Type);
        }

        private void VerifyAssignment(AssignmentAst assignment, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the variable is already defined and that it is not a constant
            var variableTypeDefinition = GetVariable(assignment.Variable, currentFunction, localVariables, errors);
            if (variableTypeDefinition == null) return;

            if (variableTypeDefinition.Constant)
            {
                var variable = assignment.Variable as VariableAst;
                errors.Add(CreateError($"Cannot reassign value of constant variable '{variable?.Name}'", assignment));
            }

            // 2. Verify the assignment value
            if (assignment.Value is NullAst nullAst)
            {
                if (assignment.Operator != Operator.None)
                {
                    errors.Add(CreateError("Cannot assign null value with operator assignment", assignment.Value));
                }
                if (variableTypeDefinition.Name != "*")
                {
                    errors.Add(CreateError("Cannot assign null to non-pointer type", assignment.Value));
                }
                nullAst.TargetType = variableTypeDefinition;
                return;
            }
            var valueType = VerifyExpression(assignment.Value, currentFunction, localVariables, errors);

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
                                errors.Add(CreateError($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types " +
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
                    VerifyConstant(constant, variableTypeDefinition, errors);
                }
            }
        }

        private TypeDefinition GetVariable(IAst ast, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
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
                variableTypeDefinition = VerifyIndex(index, variableTypeDefinition, currentFunction, localVariables, errors);
                if (variableTypeDefinition == null) return null;
            }

            return variableTypeDefinition;
        }

        private bool VerifyConditional(ConditionalAst conditional, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the condition expression
            VerifyCondition(conditional.Condition, currentFunction, localVariables, errors);

            // 2. Verify the conditional scope
            var ifReturned = VerifyScope(conditional.Children, currentFunction, localVariables, errors);

            // 3. Verify the else block if necessary
            if (conditional.Else.Any())
            {
                var elseReturned = VerifyScope(conditional.Else, currentFunction, localVariables, errors);
                return ifReturned && elseReturned;
            }

            return false;
        }

        private bool VerifyWhile(WhileAst whileAst, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the condition expression
            VerifyCondition(whileAst.Condition, currentFunction, localVariables, errors);

            // 2. Verify the scope of the while block
            return VerifyScope(whileAst.Children, currentFunction, localVariables, errors);
        }

        private bool VerifyCondition(IAst ast, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            var conditionalType = VerifyExpression(ast, currentFunction, localVariables, errors);
            switch (VerifyType(conditionalType, errors))
            {
                case Type.Int:
                case Type.Float:
                case Type.Boolean:
                case Type.Pointer:
                    // Valid types
                    return true;
                default:
                    errors.Add(CreateError($"Expected condition to be bool, int, float, or pointer, but got '{PrintTypeDefinition(conditionalType)}'", ast));
                    return false;
            }
        }

        private bool VerifyEach(EachAst each, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            var eachVariables = new Dictionary<string, TypeDefinition>(localVariables);
            // 1. Verify the iterator or range
            if (eachVariables.ContainsKey(each.IterationVariable))
            {
                errors.Add(CreateError($"Iteration variable '{each.IterationVariable}' already exists in scope", each));
            };
            if (each.Iteration != null)
            {
                var variableTypeDefinition = VerifyExpression(each.Iteration, currentFunction, localVariables, errors);
                if (variableTypeDefinition == null) return false;
                var iteratorType = variableTypeDefinition.Generics.FirstOrDefault();

                switch (variableTypeDefinition.Name)
                {
                    case "List":
                        each.IteratorType = iteratorType;
                        eachVariables.TryAdd(each.IterationVariable, iteratorType);
                        break;
                    case "Params":
                        each.IteratorType = iteratorType;
                        eachVariables.TryAdd(each.IterationVariable, iteratorType);
                        break;
                    default:
                        errors.Add(CreateError($"Type {PrintTypeDefinition(variableTypeDefinition)} cannot be used as an iterator", each.Iteration));
                        break;
                }
            }
            else
            {
                var beginType = VerifyExpression(each.RangeBegin, currentFunction, localVariables, errors);
                if (VerifyType(beginType, errors) != Type.Int)
                {
                    errors.Add(CreateError($"Expected range to begin with 'int', but got '{PrintTypeDefinition(beginType)}'", each.RangeBegin));
                }
                var endType = VerifyExpression(each.RangeEnd, currentFunction, localVariables, errors);
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
            return VerifyAsts(each.Children, currentFunction, eachVariables, errors);
        }

        private void VerifyTopLevelDirective(CompilerDirectiveAst directive, List<TranslationError> errors)
        {
            switch (directive.Type)
            {
                case DirectiveType.Run:
                    VerifyAst(directive.Value, null, _globalVariables, errors);
                    break;
                default:
                    errors.Add(CreateError("Compiler directive not supported", directive.Value));
                    break;
            }
        }

        private bool VerifyCompilerDirective(CompilerDirectiveAst directive, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            currentFunction.HasDirectives = true;
            switch (directive.Type)
            {
                case DirectiveType.If:
                    var conditional = directive.Value as ConditionalAst;
                    VerifyExpression(conditional!.Condition, currentFunction, localVariables, errors);
                    break;
                default:
                    errors.Add(CreateError("Compiler directive not supported", directive.Value));
                    break;
            }

            return false;
        }

        private TypeDefinition VerifyExpression(IAst ast, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Verify the expression value
            switch (ast)
            {
                case ConstantAst constant:
                    return constant.Type;
                case NullAst:
                    return null;
                case StructFieldRefAst structField:
                {
                    if (!localVariables.TryGetValue(structField.Name, out var structType))
                    {
                        if (_programGraph.Types.TryGetValue(structField.Name, out var type))
                        {
                            if (type is EnumAst enumAst)
                            {
                                return VerifyEnumValue(structField, enumAst, errors);
                            }
                            errors.Add(CreateError($"Cannot reference static field of type '{structField.Name}'", ast));
                            return null;
                        }
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
                            var indexType = VerifyIndexType(index, currentFunction, localVariables, errors, out var variableAst);
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
                    var valueType = VerifyExpression(unary.Value, currentFunction, localVariables, errors);
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
                    if (!_programGraph.Functions.TryGetValue(call.Function, out var function))
                    {
                        errors.Add(CreateError($"Call to undefined function '{call.Function}'", call));
                    }

                    var arguments = call.Arguments.Select(arg => VerifyExpression(arg, currentFunction, localVariables, errors)).ToList();

                    if (function != null)
                    {
                        if (!function.Verified && function != currentFunction)
                        {
                            VerifyFunction(function, errors);
                        }

                        call.Params = function.Params;
                        var argumentCount = function.Varargs || function.Params ? function.Arguments.Count - 1 : function.Arguments.Count;
                        var callArgumentCount = arguments.Count;

                        // Verify function argument count
                        if (function.Varargs || function.Params)
                        {
                            if (argumentCount > callArgumentCount)
                            {
                                errors.Add(CreateError($"Call to function '{function.Name}' expected arguments (" +
                                    $"{string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", call));
                                return function.ReturnType;
                            }
                        }
                        else if (argumentCount < callArgumentCount)
                        {
                            errors.Add(CreateError($"Call to function '{function.Name}' expected arguments (" +
                                $"{string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", call));
                            return function.ReturnType;
                        }

                        // Verify call arguments match the types of the function arguments
                        var callError = false;
                        for (var i = 0; i < argumentCount; i++)
                        {
                            var functionArg = function.Arguments[i];
                            var argumentAst = call.Arguments.ElementAtOrDefault(i);
                            if (argumentAst == null)
                            {
                                if (functionArg.DefaultValue == null)
                                {
                                    callError = true;
                                }
                                else
                                {
                                    call.Arguments.Insert(i, functionArg.DefaultValue);
                                }
                            }
                            else if (argumentAst is NullAst nullAst)
                            {
                                if (functionArg.Type.Name != "*")
                                {
                                    callError = true;
                                }
                                nullAst.TargetType = functionArg.Type;
                            }
                            else
                            {
                                var argument = arguments[i];
                                if (argument != null)
                                {
                                    if (!TypeEquals(functionArg.Type, argument))
                                    {
                                        callError = true;
                                    }
                                    else if (argument.PrimitiveType != null && call.Arguments[i] is ConstantAst constant)
                                    {
                                        VerifyConstant(constant, functionArg.Type, errors);
                                    }
                                }
                            }
                        }

                        // Verify varargs call arguments
                        if (function.Params)
                        {
                            var paramsType = function.Arguments[argumentCount].Type.Generics.FirstOrDefault();

                            if (paramsType != null)
                            {
                                for (var i = argumentCount; i < callArgumentCount; i++)
                                {
                                    var argumentAst = call.Arguments[i];
                                    if (argumentAst is NullAst nullAst)
                                    {
                                        if (paramsType.Name != "*")
                                        {
                                            callError = true;
                                        }
                                        nullAst.TargetType = paramsType;
                                    }
                                    else
                                    {
                                        var argument = arguments[i];
                                        if (argument != null)
                                        {
                                            if (!TypeEquals(paramsType, argument))
                                            {
                                                callError = true;
                                            }
                                            else if (argument.PrimitiveType != null && argumentAst is ConstantAst constant)
                                            {
                                                VerifyConstant(constant, paramsType, errors);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (function.Varargs)
                        {
                            var found = false;
                            for (var index = 0; index < function.VarargsCalls.Count; index++)
                            {
                                var callTypes = function.VarargsCalls[index];
                                if (callTypes.Count == arguments.Count)
                                {
                                    var callMatches = true;
                                    for (var i = 0; i < callTypes.Count; i++)
                                    {
                                        var argument = arguments[i];
                                        // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                                        // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                                        if (argument.PrimitiveType is FloatType {Bytes: 4})
                                        {
                                            arguments[i] = argument = new TypeDefinition
                                            {
                                                Name = "float64", PrimitiveType = new FloatType {Bytes = 8}
                                            };
                                        }
                                        if (!TypeEquals(callTypes[i], argument, true))
                                        {
                                            callMatches = false;
                                            break;
                                        }
                                    }

                                    if (callMatches)
                                    {
                                        found = true;
                                        call.VarargsIndex = index;
                                        break;
                                    }
                                }
                            }

                            if (!found)
                            {
                                call.VarargsIndex = function.VarargsCalls.Count;
                                function.VarargsCalls.Add(arguments);
                            }
                        }

                        if (callError)
                        {
                            errors.Add(CreateError($"Call to function '{function.Name}' expected arguments (" +
                                $"{string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", call));
                        }
                    }
                    return function?.ReturnType;
                case ExpressionAst expression:
                    return VerifyExpressionType(expression, currentFunction, localVariables, errors);
                case IndexAst index:
                    return VerifyIndexType(index, currentFunction, localVariables, errors, out _);
                case null:
                    return null;
                default:
                    errors.Add(CreateError($"Unexpected Ast '{ast}'", ast));
                    return null;
            }
        }

        private static void VerifyConstant(ConstantAst constant, TypeDefinition typeDef, List<TranslationError> errors)
        {
            constant.Type.Name = typeDef.Name;
            constant.Type.PrimitiveType = typeDef.PrimitiveType;

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

        private TypeDefinition VerifyExpressionType(ExpressionAst expression, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors)
        {
            // 1. Get the type of the initial child
            expression.Type = VerifyExpression(expression.Children[0], currentFunction, localVariables, errors);
            for (var i = 1; i < expression.Children.Count; i++)
            {
                // 2. Get the next operator and expression type
                var op = expression.Operators[i - 1];
                var next = expression.Children[i];
                if (next is NullAst nullAst)
                {
                    if (expression.Type.Name != "*" || (op != Operator.Equality && op != Operator.NotEqual))
                    {
                        errors.Add(CreateError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and null", next));
                    }

                    nullAst.TargetType = expression.Type;
                    expression.Type = new TypeDefinition {Name = "bool"};
                    expression.ResultingTypes.Add(expression.Type);
                    continue;
                }

                var nextExpressionType = VerifyExpression(next, currentFunction, localVariables, errors);
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
                    case Operator.NotEqual:
                    case Operator.GreaterThan:
                    case Operator.LessThan:
                    case Operator.GreaterThanEqual:
                    case Operator.LessThanEqual:
                        if (type == Type.Enum && nextType == Type.Enum)
                        {
                            if ((op != Operator.Equality && op != Operator.NotEqual) || !TypeEquals(expression.Type, nextExpressionType))
                            {
                                errors.Add(CreateError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]));
                            }
                        }
                        else if (!(type == Type.Int || type == Type.Float) &&
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

        private static TypeDefinition VerifyEnumValue(StructFieldRefAst enumRef, EnumAst enumAst, List<TranslationError> errors)
        {
            enumRef.IsEnum = true;
            var value = enumRef.Value;

            if (value.Value != null)
            {
                errors.Add(CreateError("Cannot get a value of an enum value", value.Value));
                return null;
            }

            EnumValueAst enumValue = null;
            for (var i = 0; i < enumAst.Values.Count; i++)
            {
                if (enumAst.Values[i].Name == value.Name)
                {
                    enumRef.ValueIndex = i;
                    enumValue = enumAst.Values[i];
                    break;
                }
            }

            if (enumValue == null)
            {
                errors.Add(CreateError($"Enum '{enumAst.Name}' does not contain value '{value.Name}'", value));
                return null;
            }

            return new TypeDefinition {Name = enumAst.Name, PrimitiveType = new EnumType()};
        }

        private TypeDefinition VerifyStructFieldRef(StructFieldRefAst structField, TypeDefinition structType,
            List<TranslationError> errors)
        {
            // 1. Load the struct definition in typeDefinition
            var genericName = structType.GenericName;
            if (structType.Name == "*")
            {
                genericName = structType.Generics[0].GenericName;
                structField.IsPointer = true;
            }
            structField.StructName = genericName;
            if (!_programGraph.Types.TryGetValue(genericName, out var typeDefinition))
            {
                errors.Add(CreateError($"Struct '{PrintTypeDefinition(structType)}' not defined", structField));
                return null;
            }
            if (typeDefinition is not StructAst)
            {
                errors.Add(CreateError($"Type '{PrintTypeDefinition(structType)}' is not a struct", structField));
                return null;
            }
            var structDefinition = (StructAst) typeDefinition;

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
                errors.Add(CreateError($"Struct '{PrintTypeDefinition(structType)}' does not contain field '{value.Name}'", structField));
                return null;
            }

            return value.Value == null ? field.Type : VerifyStructFieldRef(value, field.Type, errors);
        }

        private TypeDefinition VerifyIndexType(IndexAst index, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables, List<TranslationError> errors,
            out IAst variableAst)
        {
            switch (index.Variable)
            {
                case VariableAst variable:
                    variableAst = variable;
                    if (localVariables.TryGetValue(variable.Name, out var variableType))
                    {
                        return VerifyIndex(index, variableType, currentFunction, localVariables, errors);
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
                        return fieldType == null ? null : VerifyIndex(index, fieldType, currentFunction, localVariables, errors);
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


        private TypeDefinition VerifyIndex(IndexAst index, TypeDefinition typeDef, FunctionAst currentFunction, IDictionary<string, TypeDefinition> localVariables,
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
            var countType = VerifyExpression(index.Index, currentFunction, localVariables, errors);
            if (VerifyType(countType, errors) != Type.Int)
            {
                errors.Add(CreateError($"Expected List index to be type 'int', but got '{PrintTypeDefinition(countType)}'", index));
            }

            return indexType;
        }

        private static bool TypeEquals(TypeDefinition a, TypeDefinition b, bool checkPrimitives = false)
        {
            // Check by primitive type
            switch (a?.PrimitiveType)
            {
                case IntegerType aInt:
                    if (b?.PrimitiveType is IntegerType bInt)
                    {
                        if (!checkPrimitives) return true;
                        return aInt.Bytes == bInt.Bytes && aInt.Signed == bInt.Signed;
                    }
                    return false;
                case FloatType aFloat:
                    if (b?.PrimitiveType is FloatType bFloat)
                    {
                        if (!checkPrimitives) return true;
                        return aFloat.Bytes == bFloat.Bytes;
                    }
                    return false;
                case null:
                    if (b?.PrimitiveType != null) return false;
                    break;
            }

            // Check by name
            if (a?.Name != b?.Name) return false;
            if (a?.Generics.Count != b?.Generics.Count) return false;
            for (var i = 0; i < a?.Generics.Count; i++)
            {
                var ai = a.Generics[i];
                var bi = b.Generics[i];
                if (!TypeEquals(ai, bi, checkPrimitives)) return false;
            }
            return true;
        }

        private Type VerifyType(TypeDefinition typeDef, List<TranslationError> errors)
        {
            if (typeDef == null) return Type.Error;

            if (typeDef.IsGeneric)
            {
                if (typeDef.Generics.Any())
                {
                    errors.Add(CreateError("Generic type cannot have additional generic types", typeDef));
                }
                return Type.Struct;
            }

            var hasGenerics = typeDef.Generics.Any();
            var hasCount = typeDef.Count != null;
            switch (typeDef.PrimitiveType)
            {
                case IntegerType:
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
                case FloatType:
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
                    return Type.Float;
            }

            switch (typeDef.Name)
            {
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
                    if (typeDef.Generics.Count != 1)
                    {
                        errors.Add(CreateError($"Params type should have 1 generic type, but got {typeDef.Generics.Count}", typeDef));
                        return Type.Error;
                    }
                    return VerifyList(typeDef, errors) ? Type.Params : Type.Error;
                }
                default:
                    if (typeDef.Generics.Any())
                    {
                        var genericName = typeDef.GenericName;
                        if (_programGraph.Types.ContainsKey(genericName))
                        {
                            return Type.Struct;
                        }
                        if (!_polymorphicStructs.TryGetValue(typeDef.Name, out var structDef))
                        {
                            errors.Add(CreateError($"No polymorphic structs of type '{typeDef.Name}'", typeDef));
                            return Type.Error;
                        }
                        if (structDef.Generics.Count != typeDef.Generics.Count)
                        {
                            errors.Add(CreateError($"Expected type '{typeDef.Name}' to have {structDef.Generics.Count} generic(s), but got {typeDef.Generics.Count}", typeDef));
                            return Type.Error;
                        }
                        CreatePolymorphedStruct(structDef, genericName, typeDef.Generics.ToArray());
                        return Type.Struct;
                    }
                    if (!_programGraph.Types.TryGetValue(typeDef.Name, out var type))
                    {
                        return Type.Error;
                    }

                    if (type is StructAst)
                    {
                        return Type.Struct;
                    }

                    typeDef.PrimitiveType ??= new EnumType();
                    return Type.Enum;
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

            var genericName = $"List.{listType.GenericName}";
            if (_programGraph.Types.ContainsKey(genericName))
            {
                return true;
            }
            if (!_polymorphicStructs.TryGetValue("List", out var structDef))
            {
                errors.Add(CreateError($"No polymorphic structs with name '{typeDef.Name}'", typeDef));
                return false;
            }

            CreatePolymorphedStruct(structDef, genericName, listType);
            return true;
        }

        private void CreatePolymorphedStruct(StructAst structAst, string name, params TypeDefinition[] genericTypes)
        {
            var polyStruct = new StructAst {Name = name};
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

            _programGraph.Types.Add(name, polyStruct);
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
