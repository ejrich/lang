using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lang.Parsing;
using Lang.Runner;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(ParseResult parseResult, ProjectFile project, BuildSettings buildSettings);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        private readonly IProgramRunner _programRunner;
        private readonly ProgramGraph _programGraph = new();
        private BuildSettings _buildSettings;
        private readonly Dictionary<string, StructAst> _polymorphicStructs = new();
        private readonly Dictionary<string, IAst> _globalIdentifiers = new();
        private int _typeIndex;

        public ProgramGraphBuilder(IProgramRunner programRunner)
        {
            _programRunner = programRunner;
        }

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, ProjectFile project, BuildSettings buildSettings)
        {
            _programGraph.Name = project.Name;
            _programGraph.Dependencies = project.Dependencies;

            _buildSettings = buildSettings;
            var mainDefined = false;
            bool verifyAdditional;

            // Add primitive types to global identifiers
            AddPrimitive("void", TypeKind.Void);
            AddPrimitive("bool", TypeKind.Boolean);
            AddPrimitive("s8", TypeKind.Integer, new IntegerType {Signed = true, Bytes = 1});
            AddPrimitive("u8", TypeKind.Integer, new IntegerType {Bytes = 1});
            AddPrimitive("s16", TypeKind.Integer, new IntegerType {Signed = true, Bytes = 2});
            AddPrimitive("u16", TypeKind.Integer, new IntegerType {Bytes = 2});
            AddPrimitive("s32", TypeKind.Integer, new IntegerType {Signed = true, Bytes = 4});
            AddPrimitive("u32", TypeKind.Integer, new IntegerType {Bytes = 4});
            AddPrimitive("s64", TypeKind.Integer, new IntegerType {Signed = true, Bytes = 8});
            AddPrimitive("u64", TypeKind.Integer, new IntegerType {Bytes = 8});
            AddPrimitive("float", TypeKind.Float, new FloatType {Bytes = 4});
            AddPrimitive("float64", TypeKind.Float, new FloatType {Bytes = 8});

            do
            {
                // 1. Verify enum and struct definitions
                for (var i = 0; i < parseResult.SyntaxTrees.Count; i++)
                {
                    switch (parseResult.SyntaxTrees[i])
                    {
                        case EnumAst enumAst:
                            VerifyEnum(enumAst);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case StructAst structAst:
                            if (_programGraph.Types.ContainsKey(structAst.Name))
                            {
                                AddError($"Multiple definitions of struct '{structAst.Name}'", structAst);
                            }
                            else if (structAst.Generics.Any())
                            {
                                _polymorphicStructs.Add(structAst.Name, structAst);
                                _globalIdentifiers.Add(structAst.Name, structAst);
                            }
                            else
                            {
                                structAst.TypeIndex = _typeIndex++;
                                structAst.TypeKind = structAst.Name == "string" ? TypeKind.String : TypeKind.Struct;
                                _programGraph.Types.Add(structAst.Name, structAst);
                                _globalIdentifiers.Add(structAst.Name, structAst);
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
                            VerifyStruct(structAst);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case DeclarationAst globalVariable:
                            if (globalVariable.Value != null && globalVariable.Value is not ConstantAst)
                            {
                                AddError("Global variable must either not be initialized or be initialized to a constant value", globalVariable.Value);
                            }

                            VerifyDeclaration(globalVariable, null, _globalIdentifiers);
                            _programGraph.Variables.Add(globalVariable);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case FunctionAst function:
                            var main = function.Name == "main";
                            if (main)
                            {
                                if (mainDefined)
                                {
                                    AddError("Only one main function can be defined", function);
                                }

                                mainDefined = true;
                            }
                            else if (function.Name == "__start")
                            {
                                _programGraph.Start = function;
                            }

                            VerifyFunctionDefinition(function, main);
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
                            switch (directive.Type)
                            {
                                case DirectiveType.If:
                                    var conditional = directive.Value as ConditionalAst;
                                    if (VerifyCondition(conditional!.Condition, null, _globalIdentifiers))
                                    {
                                        _programRunner.Init(_programGraph);
                                        if (_programRunner.ExecuteCondition(conditional!.Condition))
                                        {
                                            additionalAsts.AddRange(conditional.Children);
                                        }
                                        else if (conditional.Else.Any())
                                        {
                                            additionalAsts.AddRange(conditional.Else);
                                        }
                                    }
                                    parseResult.SyntaxTrees.RemoveAt(i--);
                                    break;
                                case DirectiveType.Assert:
                                    if (VerifyCondition(directive.Value, null, _globalIdentifiers))
                                    {
                                        _programRunner.Init(_programGraph);
                                        if (!_programRunner.ExecuteCondition(directive.Value))
                                        {
                                            AddError("Assertion failed", directive.Value);
                                        }
                                    }
                                    parseResult.SyntaxTrees.RemoveAt(i--);
                                    break;
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
                VerifyFunction(function);
            }
            VerifyFunction(_programGraph.Start);

            // 5. Execute any other compiler directives
            foreach (var ast in parseResult.SyntaxTrees)
            {
                switch (ast)
                {
                    case CompilerDirectiveAst compilerDirective:
                        VerifyTopLevelDirective(compilerDirective);
                        break;
                }
            }

            if (!mainDefined)
            {
                // @Cleanup allow errors to be reported without having a file/line/column
                AddError("'main' function of the program is not defined");
            }

            return _programGraph;
        }

        private void AddPrimitive(string name, TypeKind typeKind, IPrimitive primitive = null)
        {
            var primitiveAst = new PrimitiveAst {Name = name, TypeIndex = _typeIndex++, TypeKind = typeKind, Primitive = primitive};
            _globalIdentifiers.Add(name, primitiveAst);
            _programGraph.Types.Add(name, primitiveAst);
        }

        private void VerifyEnum(EnumAst enumAst)
        {
            // 1. Verify enum has not already been defined
            if (!_programGraph.Types.TryAdd(enumAst.Name, enumAst))
            {
                AddError($"Multiple definitions of enum '{enumAst.Name}'", enumAst);
            }
            enumAst.TypeIndex = _typeIndex++;
            _globalIdentifiers.Add(enumAst.Name, enumAst);

            // 2. Verify enums don't have repeated values
            var valueNames = new HashSet<string>();
            var values = new HashSet<int>();
            var largestValue = -1;
            foreach (var value in enumAst.Values)
            {
                // 2a. Check if the value has been previously defined
                if (!valueNames.Add(value.Name))
                {
                    AddError($"Enum '{enumAst.Name}' already contains value '{value.Name}'", value);
                }

                // 2b. Check if the value has been previously used
                if (value.Defined)
                {
                    if (!values.Add(value.Value))
                    {
                        AddError($"Value '{value.Value}' previously defined in enum '{enumAst.Name}'", value);
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

        private void VerifyStruct(StructAst structAst)
        {
            // 1. Verify struct fields have valid types
            var fieldNames = new HashSet<string>();
            foreach (var structField in structAst.Fields)
            {
                // 1a. Check if the field has been previously defined
                if (!fieldNames.Add(structField.Name))
                {
                    AddError($"Struct '{structAst.Name}' already contains field '{structField.Name}'", structField);
                }

                // 1b. Check for errored or undefined field types
                var type = VerifyType(structField.Type);

                if (type == Type.Error)
                {
                    AddError($"Type '{PrintTypeDefinition(structField.Type)}' of field {structAst.Name}.{structField.Name} is not defined", structField);
                }
                else if (structField.Type.Count != null)
                {
                    if (structField.Type.Count is ConstantAst constant)
                    {
                        if (!uint.TryParse(constant.Value, out _))
                        {
                            AddError($"Expected type count to be positive integer, but got '{constant.Value}'", constant);
                        }
                    }
                    else
                    {
                        AddError("Type count should be a constant value", structField.Type.Count);
                    }
                }

                // 1c. Check if the default value has the correct type
                switch (structField.DefaultValue)
                {
                    case ConstantAst constant:
                        if (!TypeEquals(structField.Type, constant.Type))
                        {
                            AddError($"Type of field {structAst.Name}.{structField.Name} is '{PrintTypeDefinition(structField.Type)}', but default value is type '{PrintTypeDefinition(constant.Type)}'", constant);
                        }
                        break;
                    // case StructFieldRefAst structFieldRef:
                    //     if (_programGraph.Types.TryGetValue(structFieldRef.Value, out var fieldType))
                    //     {
                    //         if (fieldType is EnumAst enumAst)
                    //         {
                    //             var enumType = VerifyEnumValue(structFieldRef, enumAst);
                    //             if (enumType != null && !TypeEquals(structField.Type, enumType))
                    //             {
                    //                 AddError($"Type of field {structAst.Name}.{structField.Name} is '{PrintTypeDefinition(structField.Type)}', but default value is type '{PrintTypeDefinition(enumType)}'", structFieldRef);
                    //             }
                    //         }
                    //         else
                    //         {
                    //             AddError($"Default value must be constant or enum value, but got field of '{structFieldRef.Value}'", structFieldRef);
                    //         }
                    //     }
                    //     else
                    //     {
                    //         AddError($"Type '{structFieldRef.Value}' not defined", structFieldRef);
                    //     }
                    //     break;
                }
            }
        }

        private void VerifyFunctionDefinition(FunctionAst function, bool main)
        {
            // 1. Verify the return type of the function is valid
            var returnType = VerifyType(function.ReturnType);
            if (returnType == Type.Error)
            {
                AddError($"Return type '{function.ReturnType.Name}' of function '{function.Name}' is not defined", function.ReturnType);
            }
            else if (function.ReturnType.Count != null)
            {
                if (function.ReturnType.Count is ConstantAst constant)
                {
                    if (!uint.TryParse(constant.Value, out _))
                    {
                        AddError($"Expected type count to be positive integer, but got '{constant.Value}'", constant);
                    }
                }
                else
                {
                    AddError("Type count should be a constant value", function.ReturnType.Count);
                }
            }

            // 2. Verify main function return type and arguments
            if (main)
            {
                if (!(returnType == Type.Void || returnType == Type.Int))
                {
                    AddError("The main function should return type 'int' or 'void'", function);
                }

                var argument = function.Arguments.FirstOrDefault();
                if (argument != null && !(function.Arguments.Count == 1 && argument.Type.Name == "List" && argument.Type.Generics.FirstOrDefault()?.Name == "string"))
                {
                    AddError("The main function should either have 0 arguments or 'List<string>' argument", function);
                }
            }

            // 3. Verify the argument types
            var argumentNames = new HashSet<string>();
            foreach (var argument in function.Arguments)
            {
                // 3a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    AddError($"Function '{function.Name}' already contains argument '{argument.Name}'", argument);
                }

                // 3b. Check for errored or undefined field types
                var type = VerifyType(argument.Type);

                switch (type)
                {
                    case Type.VarArgs:
                        if (function.Varargs || function.Params)
                        {
                            AddError($"Function '{function.Name}' cannot have multiple varargs", argument.Type);
                        }
                        function.Varargs = true;
                        function.VarargsCalls = new List<List<TypeDefinition>>();
                        break;
                    case Type.Params:
                        if (function.Varargs || function.Params)
                        {
                            AddError($"Function '{function.Name}' cannot have multiple varargs", argument.Type);
                        }
                        function.Params = true;
                        break;
                    case Type.Error:
                        AddError($"Type '{PrintTypeDefinition(argument.Type)}' of argument '{argument.Name}' in function '{function.Name}' is not defined", argument.Type);
                        break;
                    default:
                        if (function.Varargs)
                        {
                            AddError($"Cannot declare argument '{argument.Name}' following varargs", argument);
                        }
                        else if (function.Params)
                        {
                            AddError($"Cannot declare argument '{argument.Name}' following params", argument);
                        }
                        break;
                }

                // 3c. Check for default arguments
                if (argument.Value != null)
                {
                    switch (argument.Value)
                    {
                        case ConstantAst constantAst:
                            if (!TypeEquals(argument.Type, constantAst.Type))
                            {
                                AddError($"Default function argument expected type '{PrintTypeDefinition(argument.Type)}', but got '{PrintTypeDefinition(constantAst.Type)}'", argument.Value);
                            }
                            break;
                        case NullAst nullAst:
                            if (type != Type.Pointer)
                            {
                                AddError("Default function argument can only be null for pointers", argument.Value);
                            }
                            nullAst.TargetType = argument.Type;
                            break;
                        default:
                            AddError("Default function argument should be a constant or null", argument.Value);
                            break;
                    }
                }
            }

            // 4. Load the function into the dictionary
            if (!_programGraph.Functions.TryAdd(function.Name, function))
            {
                AddError($"Multiple definitions of function '{function.Name}'", function);
            }
            // TODO Add functions to global identifiers
        }

        private void VerifyFunction(FunctionAst function)
        {
            // 1. Initialize local variables
            var scopeIdentifiers = new Dictionary<string, IAst>(_globalIdentifiers);
            foreach (var argument in function.Arguments)
            {
                // Arguments with the same name as a global variable will be used instead of the global
                if (scopeIdentifiers.TryGetValue(argument.Name, out var identifier))
                {
                    if (identifier is not DeclarationAst)
                    {
                        AddError($"Argument '{argument.Name}' already exists as a type", argument);
                    }
                }
                scopeIdentifiers[argument.Name] = argument;
            }
            var returnType = VerifyType(function.ReturnType);

            // 2. For extern functions, simply verify there is no body and return
            if (function.Extern)
            {
                if (function.Children.Any())
                {
                    AddError("Extern function cannot have a body", function);
                }
                function.Verified = true;
                return;
            }

            // 3. Resolve the compiler directives in the function
            if (function.HasDirectives)
            {
                ResolveCompilerDirectives(function.Children);
            }

            // 4. Loop through function body and verify all ASTs
            var returned = VerifyAsts(function.Children, function, scopeIdentifiers);

            // 5. Verify the main function doesn't call the compiler
            if (function.Name == "main" && function.CallsCompiler)
            {
                AddError("The main function cannot call the compiler", function);
            }

            // 6. Verify the function returns on all paths
            if (!returned && returnType != Type.Void)
            {
                AddError($"Function '{function.Name}' does not return type '{PrintTypeDefinition(function.ReturnType)}' on all paths", function);
            }
            function.Verified = true;
        }

        private void ResolveCompilerDirectives(List<IAst> asts)
        {
            for (int i = 0; i < asts.Count; i++)
            {
                var ast = asts[i];
                switch (ast)
                {
                    case ScopeAst:
                    case WhileAst:
                    case EachAst:
                        ResolveCompilerDirectives(ast.Children);
                        break;
                    case ConditionalAst conditional:
                        ResolveCompilerDirectives(conditional.Children);
                        ResolveCompilerDirectives(conditional.Else);
                        break;
                    case CompilerDirectiveAst directive:
                        asts.RemoveAt(i);
                        switch (directive.Type)
                        {
                            case DirectiveType.If:

                                var conditional = directive.Value as ConditionalAst;
                                if (VerifyCondition(conditional!.Condition, null, _globalIdentifiers))
                                {
                                    _programRunner.Init(_programGraph);
                                    if (_programRunner.ExecuteCondition(conditional!.Condition))
                                    {
                                        asts.InsertRange(i, conditional.Children);
                                    }
                                    else if (conditional.Else.Any())
                                    {
                                        asts.InsertRange(i, conditional.Else);
                                    }
                                }
                                break;
                            case DirectiveType.Assert:
                                if (VerifyCondition(directive.Value, null, _globalIdentifiers))
                                {
                                    _programRunner.Init(_programGraph);
                                    if (!_programRunner.ExecuteCondition(directive.Value))
                                    {
                                        AddError("Assertion failed", directive.Value);
                                    }
                                }
                                break;
                        }
                        i--;
                        break;
                }
            }
        }

        private bool VerifyAsts(List<IAst> asts, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            var returns = false;
            foreach (var ast in asts)
            {
                if (VerifyAst(ast, currentFunction, scopeIdentifiers))
                {
                    returns = true;
                }
            }
            return returns;
        }

        private bool VerifyScope(List<IAst> syntaxTrees, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, IAst>(scopeIdentifiers);

            // 2. Verify function lines
            return VerifyAsts(syntaxTrees, currentFunction, scopeVariables);
        }

        private bool VerifyAst(IAst syntaxTree, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            switch (syntaxTree)
            {
                case ReturnAst returnAst:
                    VerifyReturnStatement(returnAst, currentFunction, scopeIdentifiers);
                    return true;
                case DeclarationAst declaration:
                    VerifyDeclaration(declaration, currentFunction, scopeIdentifiers);
                    break;
                case AssignmentAst assignment:
                    VerifyAssignment(assignment, currentFunction, scopeIdentifiers);
                    break;
                case ScopeAst scope:
                    return VerifyScope(scope.Children, currentFunction, scopeIdentifiers);
                case ConditionalAst conditional:
                    return VerifyConditional(conditional, currentFunction, scopeIdentifiers);
                case WhileAst whileAst:
                    return VerifyWhile(whileAst, currentFunction, scopeIdentifiers);
                case EachAst each:
                    return VerifyEach(each, currentFunction, scopeIdentifiers);
                default:
                    VerifyExpression(syntaxTree, currentFunction, scopeIdentifiers);
                    break;
            }

            return false;
        }

        private void VerifyReturnStatement(ReturnAst returnAst, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Infer the return type of the function
            var returnType = VerifyType(currentFunction.ReturnType);

            // 2. Handle void case since it's the easiest to interpret
            if (returnType == Type.Void)
            {
                if (returnAst.Value != null)
                {
                    AddError("Function return should be void", returnAst);
                }
                return;
            }

            // 3. Determine if the expression returns the correct value
            var returnValueType = VerifyExpression(returnAst.Value, currentFunction, scopeIdentifiers);
            if (returnValueType == null)
            {
                AddError($"Expected to return type '{PrintTypeDefinition(currentFunction.ReturnType)}'", returnAst);
            }
            else
            {
                if (!TypeEquals(currentFunction.ReturnType, returnValueType))
                {
                    AddError($"Expected to return type '{PrintTypeDefinition(currentFunction.ReturnType)}', but returned type '{PrintTypeDefinition(returnValueType)}'", returnAst.Value);
                }
            }
        }

        private void VerifyDeclaration(DeclarationAst declaration, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the variable is already defined
            if (scopeIdentifiers.ContainsKey(declaration.Name))
            {
                AddError($"Identifier '{declaration.Name}' already defined", declaration);
                return;
            }

            // 2. Verify the null values
            if (declaration.Value is NullAst nullAst)
            {
                // 2a. Verify null can be assigned
                if (declaration.Type == null)
                {
                    AddError("Cannot assign null value without declaring a type", declaration.Value);
                }
                else
                {
                    var type = VerifyType(declaration.Type);
                    if (type == Type.Error)
                    {
                        AddError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type);
                    }
                    else if (type != Type.Pointer)
                    {
                        AddError("Cannot assign null to non-pointer type", declaration.Value);
                    }

                    nullAst.TargetType = declaration.Type;
                }
            }
            // 3. Verify object initializers
            else if (declaration.Assignments.Any())
            {
                if (declaration.Type == null)
                {
                    AddError("Struct literals are not yet supported", declaration);
                }
                else
                {
                    var type = VerifyType(declaration.Type);
                    if (type != Type.Struct)
                    {
                        AddError($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.Type)}'", declaration.Type);
                        return;
                    }

                    var structDef = _programGraph.Types[declaration.Type.GenericName] as StructAst;
                    var fields = structDef!.Fields.ToDictionary(_ => _.Name);
                    foreach (var assignment in declaration.Assignments)
                    {
                        StructFieldAst field = null;
                        if (assignment.Variable is not IdentifierAst identifier)
                        {
                            AddError("Expected to get field in object initializer", assignment.Variable);
                        }
                        else if (!fields.TryGetValue(identifier.Name, out field))
                        {
                            AddError($"Field '{identifier.Name}' not present in struct '{PrintTypeDefinition(declaration.Type)}'", assignment.Variable);
                        }

                        if (assignment.Operator != Operator.None)
                        {
                            AddError("Cannot have operator assignments in object initializers", assignment.Variable);
                        }

                        var valueType = VerifyExpression(assignment.Value, currentFunction, scopeIdentifiers);
                        if (valueType != null && field != null)
                        {
                            if (!TypeEquals(field.Type, valueType))
                            {
                                AddError($"Expected field value to be type '{PrintTypeDefinition(field.Type)}', " +
                                    $"but got '{PrintTypeDefinition(valueType)}'", field.Type);
                            }
                            else if (field.Type.PrimitiveType != null && assignment.Value is ConstantAst constant)
                            {
                                VerifyConstant(constant, field.Type);
                            }
                        }
                    }
                }
            }
            // 4. Verify declaration values
            else
            {
                if (declaration.Value == null)
                {
                    switch (declaration.Name)
                    {
                        case "os":
                            // declaration.Value = GetOSVersion();
                            break;
                        case "build_env":
                            // declaration.Value = GetBuildEnv();
                            break;
                    }
                }

                var valueType = VerifyExpression(declaration.Value, currentFunction, scopeIdentifiers);

                // 4a. Verify the assignment value matches the type definition if it has been defined
                if (declaration.Type == null)
                {
                    if (valueType.Name == "void")
                    {
                        AddError($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                        return;
                    }
                    declaration.Type = valueType;
                }
                else
                {
                    var type = VerifyType(declaration.Type);
                    if (type == Type.Error)
                    {
                        AddError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type);
                    }
                    else if (type == Type.Void)
                    {
                        AddError($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Type);
                    }

                    // Verify the type is correct
                    if (valueType != null)
                    {
                        if (!TypeEquals(declaration.Type, valueType))
                        {
                            AddError($"Expected declaration value to be type '{PrintTypeDefinition(declaration.Type)}', " +
                                $"but got '{PrintTypeDefinition(valueType)}'", declaration.Type);
                        }
                        else if (declaration.Type.PrimitiveType != null && declaration.Value is ConstantAst constant)
                        {
                            VerifyConstant(constant, declaration.Type);
                        }
                    }
                }
            }

            // 5. Verify the type definition count if necessary
            if (declaration.Type?.Count != null)
            {
                VerifyExpression(declaration.Type.Count, currentFunction, scopeIdentifiers);
            }

            // 6. Verify constant values
            if (declaration.Constant)
            {
                switch (declaration.Value)
                {
                    case ConstantAst:
                    // case StructFieldRefAst structField when structField.IsEnum:
                        break;
                    default:
                        AddError($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
                        break;
                }
                if (declaration.Type != null)
                {
                    declaration.Type.Constant = true;
                }
            }

            scopeIdentifiers.Add(declaration.Name, declaration);
        }

        // private StructFieldRefAst GetOSVersion()
        // {
        //     return new StructFieldRefAst
        //     {
        //         Value = "OS",
        //         Value = new StructFieldRefAst
        //         {
        //             Value = Environment.OSVersion.Platform switch
        //             {
        //                 PlatformID.Unix => "Linux",
        //                 PlatformID.Win32NT => "Windows",
        //                 PlatformID.MacOSX => "Mac",
        //                 _ => "None"
        //             }
        //         }
        //     };
        // }

        // private StructFieldRefAst GetBuildEnv()
        // {
        //     return new StructFieldRefAst
        //     {
        //         Value = "BuildEnv",
        //         Value = new StructFieldRefAst
        //         {
        //             Value = _buildSettings.Release ? "Release" : "Debug"
        //         }
        //     };
        // }

        private void VerifyAssignment(AssignmentAst assignment, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the variable is already defined and that it is not a constant
            var variableTypeDefinition = GetVariable(assignment.Variable, currentFunction, scopeIdentifiers);
            if (variableTypeDefinition == null) return;

            if (variableTypeDefinition.Constant)
            {
                var variable = assignment.Variable as IdentifierAst;
                AddError($"Cannot reassign value of constant variable '{variable?.Name}'", assignment);
            }

            // 2. Verify the assignment value
            if (assignment.Value is NullAst nullAst)
            {
                if (assignment.Operator != Operator.None)
                {
                    AddError("Cannot assign null value with operator assignment", assignment.Value);
                }
                if (variableTypeDefinition.Name != "*")
                {
                    AddError("Cannot assign null to non-pointer type", assignment.Value);
                }
                nullAst.TargetType = variableTypeDefinition;
                return;
            }
            var valueType = VerifyExpression(assignment.Value, currentFunction, scopeIdentifiers);

            // 3. Verify the assignment value matches the variable type definition
            if (valueType != null)
            {
                // 3a. Verify the operator is valid
                if (assignment.Operator != Operator.None)
                {
                    var type = VerifyType(variableTypeDefinition);
                    var nextType = VerifyType(valueType);
                    switch (assignment.Operator)
                    {
                        // Both need to be bool and returns bool
                        case Operator.And:
                        case Operator.Or:
                            if (type != Type.Boolean || nextType != Type.Boolean)
                            {
                                AddError($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types " +
                                    $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                        // Invalid assignment operators
                        case Operator.Equality:
                        case Operator.GreaterThan:
                        case Operator.LessThan:
                        case Operator.GreaterThanEqual:
                        case Operator.LessThanEqual:
                            AddError($"Invalid operator '{PrintOperator(assignment.Operator)}' in assignment", assignment);
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
                                AddError($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
                                    $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (!(type == Type.Boolean && nextType == Type.Boolean) &&
                                !(type == Type.Int && nextType == Type.Int))
                            {
                                AddError($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
                                    $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                    }
                }
                else if (!TypeEquals(variableTypeDefinition, valueType))
                {
                    AddError($"Expected assignment value to be type '{PrintTypeDefinition(variableTypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", assignment.Value);
                }
                else if (variableTypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                {
                    VerifyConstant(constant, variableTypeDefinition);
                }
            }
        }

        private TypeDefinition GetVariable(IAst ast, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 2. Get the variable name
            var variableName = ast switch
            {
                IdentifierAst identifierAst => identifierAst.Name,
                // StructFieldRefAst fieldRef => fieldRef.Value,
                IndexAst index => index.Variable switch
                {
                    IdentifierAst identifierAst => identifierAst.Name,
                    // StructFieldRefAst fieldRef => fieldRef.Value,
                    _ => string.Empty
                },
                _ => string.Empty
            };
            if (!scopeIdentifiers.TryGetValue(variableName, out var identifier))
            {
                AddError($"Variable '{variableName}' not defined", ast);
                return null;
            }
            if (identifier is not DeclarationAst declaration)
            {
                AddError($"Identifier '{variableName}' is not a variable", ast);
                return null;
            }
            var type = declaration.Type;

            // 2. Get the exact type definition
            if (false)//ast is StructFieldRefAst structField)
            {
                // type = VerifyStructFieldRef(structField, type);
                // if (type == null) return null;
            }
            else if (ast is IndexAst index)
            {
                // if (index.Variable is StructFieldRefAst indexStructField)
                // {
                //     type = VerifyStructFieldRef(indexStructField, type);
                //     if (type == null) return null;
                // }
                type = VerifyIndex(index, type, currentFunction, scopeIdentifiers);
                if (type == null) return null;
            }

            return type;
        }

        private bool VerifyConditional(ConditionalAst conditional, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the condition expression
            VerifyCondition(conditional.Condition, currentFunction, scopeIdentifiers);

            // 2. Verify the conditional scope
            var ifReturned = VerifyScope(conditional.Children, currentFunction, scopeIdentifiers);

            // 3. Verify the else block if necessary
            if (conditional.Else.Any())
            {
                var elseReturned = VerifyScope(conditional.Else, currentFunction, scopeIdentifiers);
                return ifReturned && elseReturned;
            }

            return false;
        }

        private bool VerifyWhile(WhileAst whileAst, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the condition expression
            VerifyCondition(whileAst.Condition, currentFunction, scopeIdentifiers);

            // 2. Verify the scope of the while block
            return VerifyScope(whileAst.Children, currentFunction, scopeIdentifiers);
        }

        private bool VerifyCondition(IAst ast, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            var conditionalType = VerifyExpression(ast, currentFunction, scopeIdentifiers);
            switch (VerifyType(conditionalType))
            {
                case Type.Int:
                case Type.Float:
                case Type.Boolean:
                case Type.Pointer:
                    // Valid types
                    return true;
                default:
                    AddError($"Expected condition to be bool, int, float, or pointer, but got '{PrintTypeDefinition(conditionalType)}'", ast);
                    return false;
            }
        }

        private bool VerifyEach(EachAst each, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            var eachIdentifiers = new Dictionary<string, IAst>(scopeIdentifiers);
            // 1. Verify the iterator or range
            if (eachIdentifiers.ContainsKey(each.IterationVariable))
            {
                AddError($"Iteration variable '{each.IterationVariable}' already exists in scope", each);
            };
            if (each.Iteration != null)
            {
                var variableTypeDefinition = VerifyExpression(each.Iteration, currentFunction, scopeIdentifiers);
                if (variableTypeDefinition == null) return false;
                var iterator = new DeclarationAst {Name = each.IterationVariable, Type = variableTypeDefinition.Generics.FirstOrDefault()};

                switch (variableTypeDefinition.Name)
                {
                    case "List":
                        each.IteratorType = iterator.Type;
                        eachIdentifiers.TryAdd(each.IterationVariable, iterator);
                        break;
                    case "Params":
                        each.IteratorType = iterator.Type;
                        eachIdentifiers.TryAdd(each.IterationVariable, iterator);
                        break;
                    default:
                        AddError($"Type {PrintTypeDefinition(variableTypeDefinition)} cannot be used as an iterator", each.Iteration);
                        break;
                }
            }
            else
            {
                var beginType = VerifyExpression(each.RangeBegin, currentFunction, scopeIdentifiers);
                if (VerifyType(beginType) != Type.Int)
                {
                    AddError($"Expected range to begin with 'int', but got '{PrintTypeDefinition(beginType)}'", each.RangeBegin);
                }
                var endType = VerifyExpression(each.RangeEnd, currentFunction, scopeIdentifiers);
                if (VerifyType(endType) != Type.Int)
                {
                    AddError($"Expected range to end with 'int', but got '{PrintTypeDefinition(endType)}'", each.RangeEnd);
                }
                var iterType = new DeclarationAst
                {
                    Name = each.IterationVariable,
                    Type = new TypeDefinition {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}}
                };
                if (!eachIdentifiers.TryAdd(each.IterationVariable, iterType))
                {
                    AddError($"Iteration variable '{each.IterationVariable}' already exists in scope", each);
                };
            }

            // 2. Verify the scope of the each block
            return VerifyAsts(each.Children, currentFunction, eachIdentifiers);
        }

        private void VerifyTopLevelDirective(CompilerDirectiveAst directive)
        {
            switch (directive.Type)
            {
                case DirectiveType.Run:
                    VerifyAst(directive.Value, null, _globalIdentifiers);
                    if (!_programGraph.Errors.Any())
                    {
                        _programRunner.Init(_programGraph);
                        _programRunner.RunProgram(directive.Value);
                    }
                    break;
                default:
                    AddError($"Compiler directive '{directive.Type}' not supported", directive);
                    break;
            }
        }

        private TypeDefinition VerifyExpression(IAst ast, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the expression value
            switch (ast)
            {
                case ConstantAst constant:
                    return constant.Type;
                case NullAst:
                    return null;
                // case StructFieldRefAst structField:
                // {
                //     if (!scopeIdentifiers.TryGetValue(structField.Value, out var identifier))
                //     {
                //         AddError($"Identifier '{structField.Value}' not defined", ast);
                //         return null;
                //     }
                //     switch (identifier)
                //     {
                //         case EnumAst enumAst:
                //             return VerifyEnumValue(structField, enumAst);
                //         case DeclarationAst declaration:
                //             return VerifyStructFieldRef(structField, declaration.Type);
                //         default:
                //             AddError($"Cannot reference static field of type '{structField.Value}'", ast);
                //             return null;
                //     }
                // }
                case IdentifierAst identifierAst:
                {
                    if (!scopeIdentifiers.TryGetValue(identifierAst.Name, out var identifier))
                    {
                        AddError($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                    }
                    switch (identifier)
                    {
                        case DeclarationAst declaration:
                            return declaration.Type;
                        case PrimitiveAst primitive:
                            return new TypeDefinition {Name = "Type", TypeIndex = primitive.TypeIndex};
                        case EnumAst enumAst:
                            return new TypeDefinition {Name = "Type", TypeIndex = enumAst.TypeIndex};
                        case StructAst structAst:
                            if (structAst.Generics.Any())
                            {
                                AddError($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                            }
                            return new TypeDefinition {Name = "Type", TypeIndex = structAst.TypeIndex};
                        default:
                            return null;
                    }
                }
                case ChangeByOneAst changeByOne:
                    switch (changeByOne.Variable)
                    {
                        case IdentifierAst identifierAst:
                            if (scopeIdentifiers.TryGetValue(identifierAst.Name, out var identifier))
                            {
                                if (identifier is not DeclarationAst declaration)
                                {
                                    AddError($"Identifier '{identifierAst.Name}' is not a variable", identifierAst);
                                    return null;
                                }

                                var type = VerifyType(declaration.Type);
                                if (type == Type.Int || type == Type.Float) return declaration.Type;

                                var op = changeByOne.Positive ? "increment" : "decrement";
                                AddError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(declaration.Type)}'", identifierAst);
                                return null;
                            }
                            else
                            {
                                AddError($"Variable '{identifierAst.Name}' not defined", identifierAst);
                                return null;
                            }
                        // case StructFieldRefAst structField:
                        //     if (scopeIdentifiers.TryGetValue(structField.Value, out var structIdentifier))
                        //     {
                        //         if (structIdentifier is not DeclarationAst declaration)
                        //         {
                        //             AddError($"Identifier '{structField.Value}' is not a variable", structField);
                        //             return null;
                        //         }

                        //         var fieldType = VerifyStructFieldRef(structField, declaration.Type);
                        //         if (fieldType == null) return null;

                        //         var type = VerifyType(fieldType);
                        //         if (type == Type.Int || type == Type.Float) return fieldType;

                        //         var op = changeByOne.Positive ? "increment" : "decrement";
                        //         AddError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(fieldType)}'", structField);
                        //         return null;
                        //     }
                        //     else
                        //     {
                        //         AddError($"Variable '{structField.Value}' not defined", structField);
                        //         return null;
                        //     }
                        case IndexAst index:
                            var indexType = VerifyIndexType(index, currentFunction, scopeIdentifiers, out var variable);
                            if (indexType != null)
                            {
                                var type = VerifyType(indexType);
                                if (type == Type.Int || type == Type.Float) return indexType;

                                var op = changeByOne.Positive ? "increment" : "decrement";
                                AddError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(indexType)}'", variable);
                            }
                            return null;
                        default:
                            var operand = changeByOne.Positive ? "increment" : "decrement";
                            AddError($"Expected to {operand} variable", changeByOne);
                            return null;
                    }
                case UnaryAst unary:
                {
                    var valueType = VerifyExpression(unary.Value, currentFunction, scopeIdentifiers);
                    var type = VerifyType(valueType);
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not:
                            if (type == Type.Boolean)
                            {
                                return valueType;
                            }
                            AddError($"Expected type 'bool', but got type '{PrintTypeDefinition(valueType)}'", unary.Value);
                            return null;
                        case UnaryOperator.Negate:
                            if (type == Type.Int || type == Type.Float)
                            {
                                return valueType;
                            }
                            AddError($"Negation not compatible with type '{PrintTypeDefinition(valueType)}'", unary.Value);
                            return null;
                        case UnaryOperator.Dereference:
                            if (type == Type.Pointer)
                            {
                                return valueType.Generics[0];
                            }
                            AddError($"Cannot dereference type '{PrintTypeDefinition(valueType)}'", unary.Value);
                            return null;
                        case UnaryOperator.Reference:
                            if (unary.Value is IdentifierAst || /*unary.Value is StructFieldRefAst ||*/ unary.Value is IndexAst || type == Type.Pointer)
                            {
                                var pointerType = new TypeDefinition {Name = "*"};
                                pointerType.Generics.Add(valueType);
                                return pointerType;
                            }
                            AddError("Can only reference variables, structs, or struct fields", unary.Value);
                            return null;
                        default:
                            AddError($"Unexpected unary operator '{unary.Operator}'", unary.Value);
                            return null;
                    }
                }
                case CallAst call:
                    if (!_programGraph.Functions.TryGetValue(call.Function, out var function))
                    {
                        AddError($"Call to undefined function '{call.Function}'", call);
                    }

                    var arguments = call.Arguments.Select(arg => VerifyExpression(arg, currentFunction, scopeIdentifiers)).ToList();

                    if (function != null)
                    {
                        if (!function.Verified && function != currentFunction)
                        {
                            VerifyFunction(function);
                        }

                        if (currentFunction != null && !currentFunction.CallsCompiler && (function.Compiler || function.CallsCompiler))
                        {
                            currentFunction.CallsCompiler = true;
                        }

                        call.Params = function.Params;
                        var argumentCount = function.Varargs || function.Params ? function.Arguments.Count - 1 : function.Arguments.Count;
                        var callArgumentCount = arguments.Count;

                        // Verify function argument count
                        if (function.Varargs || function.Params)
                        {
                            if (argumentCount > callArgumentCount)
                            {
                                AddError($"Call to function '{function.Name}' expected arguments (" +
                                    $"{string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", call);
                                return function.ReturnType;
                            }
                        }
                        else if (argumentCount < callArgumentCount)
                        {
                            AddError($"Call to function '{function.Name}' expected arguments (" +
                                $"{string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", call);
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
                                if (functionArg.Value == null)
                                {
                                    callError = true;
                                }
                                else
                                {
                                    call.Arguments.Insert(i, functionArg.Value);
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
                                        VerifyConstant(constant, functionArg.Type);
                                    }
                                    else if (argument.TypeIndex.HasValue)
                                    {
                                        var typeIndex = new ConstantAst
                                        {
                                            Type = new TypeDefinition {PrimitiveType = new IntegerType {Signed = true, Bytes = 4}},
                                            Value = argument.TypeIndex.ToString()
                                        };
                                        call.Arguments[i] = typeIndex;
                                        arguments[i] = typeIndex.Type;
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
                                                VerifyConstant(constant, paramsType);
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
                                        if (argument?.PrimitiveType is FloatType {Bytes: 4})
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
                            AddError($"Call to function '{function.Name}' expected arguments (" +
                                $"{string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", call);
                        }
                    }
                    return function?.ReturnType;
                case ExpressionAst expression:
                    return VerifyExpressionType(expression, currentFunction, scopeIdentifiers);
                case IndexAst index:
                    return VerifyIndexType(index, currentFunction, scopeIdentifiers, out _);
                case TypeDefinition typeDef:
                {
                    if (VerifyType(typeDef) == Type.Error)
                    {
                        return null;
                    }
                    if (!_programGraph.Types.TryGetValue(typeDef.GenericName, out var type))
                    {
                        return null;
                    }
                    return new TypeDefinition {Name = "Type", TypeIndex = type.TypeIndex};
                }
                case null:
                    return null;
                default:
                    AddError($"Unexpected Ast '{ast}'", ast);
                    return null;
            }
        }

        private void VerifyConstant(ConstantAst constant, TypeDefinition typeDef)
        {
            constant.Type.Name = typeDef.Name;
            constant.Type.PrimitiveType = typeDef.PrimitiveType;

            var type = constant.Type;
            switch (type.PrimitiveType)
            {
                case IntegerType integer:
                    if (!integer.Signed && constant.Value[0] == '-')
                    {
                        AddError($"Unsigned type '{PrintTypeDefinition(constant.Type)}' cannot be negative", constant);
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
                        AddError($"Value '{constant.Value}' out of range for type '{PrintTypeDefinition(constant.Type)}'", constant);
                    }
                    break;
            }
        }

        private TypeDefinition VerifyExpressionType(ExpressionAst expression, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Get the type of the initial child
            expression.Type = VerifyExpression(expression.Children[0], currentFunction, scopeIdentifiers);
            for (var i = 1; i < expression.Children.Count; i++)
            {
                // 2. Get the next operator and expression type
                var op = expression.Operators[i - 1];
                var next = expression.Children[i];
                if (next is NullAst nullAst)
                {
                    if (expression.Type.Name != "*" || (op != Operator.Equality && op != Operator.NotEqual))
                    {
                        AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and null", next);
                    }

                    nullAst.TargetType = expression.Type;
                    expression.Type = new TypeDefinition {Name = "bool"};
                    expression.ResultingTypes.Add(expression.Type);
                    continue;
                }

                var nextExpressionType = VerifyExpression(next, currentFunction, scopeIdentifiers);
                if (nextExpressionType == null) return null;

                // 3. Verify the operator and expression types are compatible and convert the expression type if necessary
                var type = VerifyType(expression.Type);
                var nextType = VerifyType(nextExpressionType);
                switch (op)
                {
                    // Both need to be bool and returns bool
                    case Operator.And:
                    case Operator.Or:
                        if (type != Type.Boolean || nextType != Type.Boolean)
                        {
                            AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
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
                                AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
                            }
                        }
                        else if (!(type == Type.Int || type == Type.Float) &&
                            !(nextType == Type.Int || nextType == Type.Float))
                        {
                            AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
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
                            AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
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
                            AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
                            if (nextType == Type.Boolean || nextType == Type.Int)
                            {
                                expression.Type = nextExpressionType;
                            }
                            else if (!(type == Type.Boolean || type == Type.Int))
                            {
                                // If the type can't be determined, default to int
                                expression.Type = new TypeDefinition {Name = "s32"};
                            }
                        }
                        break;
                }
                expression.ResultingTypes.Add(expression.Type);
            }
            return expression.Type;
        }

        private TypeDefinition VerifyEnumValue(ExpressionAst enumRef, EnumAst enumAst)
        {
            // enumRef.IsEnum = true;
            // var value = enumRef.Value;

            // if (value.Value != null)
            // {
            //     AddError("Cannot get a value of an enum value", value.Value);
            //     return null;
            // }

            // EnumValueAst enumValue = null;
            // for (var i = 0; i < enumAst.Values.Count; i++)
            // {
            //     if (enumAst.Values[i].Name == value.Name)
            //     {
            //         enumRef.ValueIndex = i;
            //         enumValue = enumAst.Values[i];
            //         break;
            //     }
            // }

            // if (enumValue == null)
            // {
            //     AddError($"Enum '{enumAst.Name}' does not contain value '{value.Name}'", value);
            //     return null;
            // }

            return new TypeDefinition {Name = enumAst.Name, PrimitiveType = new EnumType()};
        }

        private TypeDefinition VerifyStructFieldRef(ExpressionAst structField, TypeDefinition structType)
        {
            // 1. Load the struct definition in typeDefinition
            // var genericName = structType.GenericName;
            // if (structType.Name == "*")
            // {
            //     genericName = structType.Generics[0].GenericName;
            //     structField.IsPointer = true;
            // }
            // structField.StructName = genericName;
            // if (!_programGraph.Types.TryGetValue(genericName, out var typeDefinition))
            // {
            //     AddError($"Struct '{PrintTypeDefinition(structType)}' not defined", structField);
            //     return null;
            // }
            // if (typeDefinition is not StructAst)
            // {
            //     AddError($"Type '{PrintTypeDefinition(structType)}' is not a struct", structField);
            //     return null;
            // }
            // var structDefinition = (StructAst) typeDefinition;

            // // 2. If the type of the field is other, recurse and return
            // var value = structField.Value;
            // StructFieldAst field = null;
            // for (var i = 0; i < structDefinition.Fields.Count; i++)
            // {
            //     if (structDefinition.Fields[i].Name == value.Name)
            //     {
            //         structField.ValueIndex = i;
            //         field = structDefinition.Fields[i];
            //         break;
            //     }
            // }
            // if (field == null)
            // {
            //     AddError($"Struct '{PrintTypeDefinition(structType)}' does not contain field '{value.Name}'", structField);
            //     return null;
            // }

            // return value.Value == null ? field.Type : VerifyStructFieldRef(value, field.Type);
            return null;
        }

        private TypeDefinition VerifyIndexType(IndexAst index, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers, out IAst variableAst)
        {
            switch (index.Variable)
            {
                case IdentifierAst identifierAst:
                    variableAst = identifierAst;
                    if (scopeIdentifiers.TryGetValue(identifierAst.Name, out var identifier))
                    {
                        if (identifier is not DeclarationAst declaration)
                        {
                            AddError($"Identifier '{identifierAst.Name}' is not a variable", identifierAst);
                            return null;
                        }
                        return VerifyIndex(index, declaration.Type, currentFunction, scopeIdentifiers);
                    }
                    else
                    {
                        AddError($"Variable '{identifierAst.Name}' not defined", identifierAst);
                        return null;
                    }
                // case StructFieldRefAst structField:
                //     variableAst = structField;
                //     if (scopeIdentifiers.TryGetValue(structField.Value, out var structIdentifier))
                //     {
                //         if (structIdentifier is not DeclarationAst declaration)
                //         {
                //             AddError($"Identifier '{structField.Value}' is not a variable", structField);
                //             return null;
                //         }
                //         var fieldType = VerifyStructFieldRef(structField, declaration.Type);
                //         return fieldType == null ? null : VerifyIndex(index, fieldType, currentFunction, scopeIdentifiers);
                //     }
                //     else
                //     {
                //         AddError($"Variable '{structField.Value}' not defined", structField);
                //         return null;
                //     }
                default:
                    variableAst = null;
                    AddError("Expected to index a variable", index);
                    return null;
            }
        }

        private TypeDefinition VerifyIndex(IndexAst index, TypeDefinition typeDef, FunctionAst currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the variable is a list
            var type = VerifyType(typeDef);
            if (type != Type.List && type != Type.Params)
            {
                AddError($"Cannot index type '{PrintTypeDefinition(typeDef)}'", index.Variable);
                return null;
            }

            // 2. Load the list element type definition
            var elementType = typeDef.Generics.FirstOrDefault();
            if (elementType == null)
            {
                AddError("Unable to determine element type of the List", index);
            }

            // 3. Verify the count expression is an integer
            var indexValue = VerifyExpression(index.Index, currentFunction, scopeIdentifiers);
            var indexType = VerifyType(indexValue);
            if (indexType != Type.Int && indexType != Type.Type)
            {
                AddError($"Expected List index to be type 'int', but got '{PrintTypeDefinition(indexValue)}'", index);
            }

            return elementType;
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


        private Type VerifyType(TypeDefinition typeDef)
        {
            if (typeDef == null) return Type.Error;

            if (typeDef.IsGeneric)
            {
                if (typeDef.Generics.Any())
                {
                    AddError("Generic type cannot have additional generic types", typeDef);
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
                        AddError($"Type '{typeDef.Name}' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have count", typeDef);
                        return Type.Error;
                    }
                    return Type.Int;
                case FloatType:
                    if (hasGenerics)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have count", typeDef);
                        return Type.Error;
                    }
                    return Type.Float;
            }

            switch (typeDef.Name)
            {
                case "bool":
                    if (hasGenerics)
                    {
                        AddError("Type 'bool' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have count", typeDef);
                        return Type.Error;
                    }
                    return Type.Boolean;
                case "string":
                    if (hasGenerics)
                    {
                        AddError("Type 'string' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have count", typeDef);
                        return Type.Error;
                    }
                    return Type.String;
                case "List":
                {
                    if (typeDef.Generics.Count != 1)
                    {
                        AddError($"Type 'List' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        return Type.Error;
                    }
                    return VerifyList(typeDef) ? Type.List : Type.Error;
                }
                case "void":
                    if (hasGenerics)
                    {
                        AddError("Type 'void' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    if (hasCount)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have count", typeDef);
                        return Type.Error;
                    }
                    return Type.Void;
                case "*":
                    if (typeDef.Generics.Count != 1)
                    {
                        AddError($"pointer type should have reference to 1 type, but got {typeDef.Generics.Count}", typeDef);
                        return Type.Error;
                    }
                    if (_programGraph.Types.ContainsKey(typeDef.GenericName))
                    {
                        return Type.Pointer;
                    }
                    if (VerifyType(typeDef.Generics[0]) == Type.Error)
                    {
                        return Type.Error;
                    }

                    var pointer = new PrimitiveAst {Name = typeDef.GenericName, TypeIndex = _typeIndex++, TypeKind = TypeKind.Pointer};
                    _programGraph.Types.Add(typeDef.GenericName, pointer);
                    return Type.Pointer;
                case "...":
                    if (hasGenerics)
                    {
                        AddError("Type 'varargs' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    return Type.VarArgs;
                case "Params":
                {
                    if (typeDef.Generics.Count != 1)
                    {
                        AddError($"Type 'Params' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        return Type.Error;
                    }
                    return VerifyList(typeDef) ? Type.Params : Type.Error;
                }
                case "Type":
                    if (hasGenerics)
                    {
                        AddError("Type 'Type' cannot have generics", typeDef);
                        return Type.Error;
                    }
                    return Type.Type;
                default:
                    if (typeDef.Generics.Any())
                    {
                        var genericName = typeDef.GenericName;
                        if (_programGraph.Types.ContainsKey(genericName))
                        {
                            return Type.Struct;
                        }
                        var generics = typeDef.Generics.ToArray();
                        var error = false;
                        foreach (var generic in generics)
                        {
                            if (VerifyType(generic) == Type.Error)
                            {
                                error = true;
                            }
                        }
                        if (!_polymorphicStructs.TryGetValue(typeDef.Name, out var structDef))
                        {
                            AddError($"No polymorphic structs of type '{typeDef.Name}'", typeDef);
                            return Type.Error;
                        }
                        if (structDef.Generics.Count != typeDef.Generics.Count)
                        {
                            AddError($"Expected type '{typeDef.Name}' to have {structDef.Generics.Count} generic(s), but got {typeDef.Generics.Count}", typeDef);
                            return Type.Error;
                        }
                        if (error) return Type.Error;
                        CreatePolymorphedStruct(structDef, PrintTypeDefinition(typeDef), genericName, TypeKind.Struct, generics);
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

        private bool VerifyList(TypeDefinition typeDef)
        {
            var listType = typeDef.Generics[0];
            var genericType = VerifyType(listType);
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
                AddError($"No polymorphic structs with name '{typeDef.Name}'", typeDef);
                return false;
            }

            CreatePolymorphedStruct(structDef, $"List<{PrintTypeDefinition(listType)}>", genericName, TypeKind.List, listType);
            return true;
        }

        private void CreatePolymorphedStruct(StructAst structAst, string name, string genericName, TypeKind typeKind, params TypeDefinition[] genericTypes)
        {
            var polyStruct = new StructAst {Name = name, TypeIndex = _typeIndex++, TypeKind = typeKind};
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

            _programGraph.Types.Add(genericName, polyStruct);
        }

        private TypeDefinition CopyType(TypeDefinition type, TypeDefinition[] genericTypes)
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
            VerifyType(copyType);

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

        private void AddError(string errorMessage, IAst ast = null)
        {
            var error = new TranslationError
            {
                Error = errorMessage,
                FileIndex = ast?.FileIndex ?? 0,
                Line = ast?.Line ?? 0,
                Column = ast?.Column ?? 0
            };
            _programGraph.Errors.Add(error);
        }
    }
}
