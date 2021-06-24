using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lang
{
    public class ProgramGraph
    {
        public List<DeclarationAst> Variables { get; } = new();
        public int TypeCount { get; set; }
        public Dictionary<string, IType> Types { get; } = new();
        public Dictionary<string, List<FunctionAst>> Functions { get; } = new();
        public Dictionary<string, Dictionary<Operator, OperatorOverloadAst>> OperatorOverloads { get; } = new();
        public HashSet<string> Dependencies { get; set; }
        public List<TranslationError> Errors { get; } = new();
    }

    public class TranslationError
    {
        public int FileIndex { get; init; }
        public string Error { get; init; }
        public int Line { get; init; }
        public int Column { get; init; }
    }

    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(ParseResult parseResult, ProjectFile project, BuildSettings buildSettings);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        private readonly IPolymorpher _polymorpher;
        private readonly IProgramRunner _programRunner;
        private readonly ProgramGraph _programGraph = new();
        private BuildSettings _buildSettings;
        private readonly Dictionary<string, StructAst> _polymorphicStructs = new();
        private readonly Dictionary<string, List<FunctionAst>> _polymorphicFunctions = new();
        private readonly Dictionary<string, Dictionary<Operator, OperatorOverloadAst>> _polymorphicOperatorOverloads = new();
        private readonly Dictionary<string, IAst> _globalIdentifiers = new();

        public ProgramGraphBuilder(IPolymorpher polymorpher, IProgramRunner programRunner)
        {
            _polymorpher = polymorpher;
            _programRunner = programRunner;
        }

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, ProjectFile project, BuildSettings buildSettings)
        {
            _programGraph.Dependencies = project.Dependencies;

            _buildSettings = buildSettings;
            var mainDefined = false;
            bool verifyAdditional;

            // Add primitive types to global identifiers
            AddPrimitive("void", TypeKind.Void);
            AddPrimitive("bool", TypeKind.Boolean, size: 1);
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

            var functionNames = new HashSet<string>();
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
                                break;
                            }

                            if (structAst.Generics.Any())
                            {
                                if (_polymorphicStructs.ContainsKey(structAst.Name))
                                {
                                    AddError($"Multiple definitions of polymorphic struct '{structAst.Name}'", structAst);
                                }
                                _polymorphicStructs[structAst.Name] = structAst;
                            }
                            else
                            {
                                structAst.TypeIndex = _programGraph.TypeCount++;
                                structAst.TypeKind = structAst.Name == "string" ? TypeKind.String : TypeKind.Struct;
                                _programGraph.Types.Add(structAst.Name, structAst);
                            }

                            _globalIdentifiers[structAst.Name] = structAst;
                            break;
                    }
                }

                // 2. Verify global variables and function return types/arguments
                for (var i = 0; i < parseResult.SyntaxTrees.Count; i++)
                {
                    switch (parseResult.SyntaxTrees[i])
                    {
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

                            VerifyFunctionDefinition(function, functionNames, main);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                        case OperatorOverloadAst overload:
                            VerifyOperatorOverloadDefinition(overload);
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                    }
                }

                // 3. Verify struct bodies
                for (var i = 0; i < parseResult.SyntaxTrees.Count; i++)
                {
                    switch (parseResult.SyntaxTrees[i])
                    {
                        case StructAst structAst:
                            if (!structAst.Verified)
                            {
                                VerifyStruct(structAst);
                            }
                            parseResult.SyntaxTrees.RemoveAt(i--);
                            break;
                    }
                }

                // 4. Verify and run top-level static ifs
                verifyAdditional = false;
                var additionalAsts = new List<IAst>();
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

            // 5. Verify operator overload bodies
            foreach (var overloads in _programGraph.OperatorOverloads.Values)
            {
                foreach (var overload in overloads.Values)
                {
                    if (overload.Verified) continue;
                    VerifyOperatorOverload(overload);
                }
            }

            // 6. Verify function bodies
            foreach (var name in functionNames)
            {
                var functions = _programGraph.Functions[name];
                foreach (var function in functions)
                {
                    if (function.Verified) continue;
                    VerifyFunction(function);
                }
            }

            // 7. Execute any other compiler directives
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

        private void AddPrimitive(string name, TypeKind typeKind, IPrimitive primitive = null, uint size = 0)
        {
            var primitiveAst = new PrimitiveAst
            {
                Name = name, TypeIndex = _programGraph.TypeCount++, TypeKind = typeKind,
                Size = primitive?.Bytes ?? size, Primitive = primitive
            };
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
            enumAst.TypeIndex = _programGraph.TypeCount++;
            _globalIdentifiers.Add(enumAst.Name, enumAst);

            if (enumAst.BaseType == null)
            {
                enumAst.BaseType = new TypeDefinition {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};
            }
            else
            {
                var baseType = VerifyType(enumAst.BaseType);
                if (baseType != TypeKind.Integer && baseType != TypeKind.Error)
                {
                    AddError($"Base type of enum must be an integer, but got '{PrintTypeDefinition(enumAst.BaseType)}'", enumAst.BaseType);
                    enumAst.BaseType.PrimitiveType = new IntegerType {Bytes = 4, Signed = true};
                }
            }

            // 2. Verify enums don't have repeated values
            var valueNames = new HashSet<string>();
            var values = new HashSet<int>();

            var primitive = enumAst.BaseType.PrimitiveType;
            var lowestAllowedValue = primitive.Signed ? -Math.Pow(2, 4 * primitive.Bytes - 1) : 0;
            var largestAllowedValue = primitive.Signed ? Math.Pow(2, 4 * primitive.Bytes - 1) - 1 : Math.Pow(2, 4 * primitive.Bytes) - 1;

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

                // 2d. Verify the value is in the range of the enum
                if (value.Value < lowestAllowedValue || value.Value > largestAllowedValue)
                {
                    AddError($"Enum value '{enumAst.Name}.{value.Name}' value '{value.Value}' is out of range", value);
                }
            }
        }

        private void VerifyStruct(StructAst structAst)
        {
            // 1. Verify struct fields have valid types
            var errorCount = _programGraph.Errors.Count;
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

                if (type == TypeKind.Error)
                {
                    AddError($"Type '{PrintTypeDefinition(structField.Type)}' of field {structAst.Name}.{structField.Name} is not defined", structField);
                }

                // 1c. Check type count
                if (structField.Type.CArray && structField.Type.Count == null)
                {
                    AddError($"C array of field '{structAst.Name}.{structField.Name}' must be initialized with a constant size", structField.Type);
                }
                else if (structField.Type.Count != null)
                {
                    // Verify the count is a constant
                    var countType = VerifyConstantExpression(structField.Type.Count, null, _globalIdentifiers, out var isConstant, out var count);

                    if (countType != null)
                    {
                        if (!isConstant || countType.PrimitiveType is not IntegerType)
                        {
                            AddError($"Expected size of '{structAst.Name}.{structField.Name}' to be a constant integer", structField.Type.Count);
                        }
                        else if (count < 0)
                        {
                            AddError($"Expected size of '{structAst.Name}.{structField.Name}' to be a positive integer", structField.Type.Count);
                        }
                        else
                        {
                            structField.Type.ConstCount = (uint)count;
                        }
                    }
                }

                // 1d. Check if the default value has the correct type
                if (structField.DefaultValue != null)
                {
                    var defaultType = VerifyConstantExpression(structField.DefaultValue, null, _globalIdentifiers, out var isConstant, out _);

                    if (defaultType != null)
                    {
                        if (!isConstant)
                        {
                            AddError($"Expected default value of '{structAst.Name}.{structField.Name}' to be a constant value", structField.DefaultValue);
                        }
                        else if (type != TypeKind.Error && !TypeEquals(structField.Type, defaultType))
                        {
                            AddError($"Type of field '{structAst.Name}.{structField.Name}' is '{PrintTypeDefinition(structField.Type)}', but default value is type '{PrintTypeDefinition(defaultType)}'", structField.DefaultValue);
                        }
                        else if (structField.Type.PrimitiveType != null && structField.DefaultValue is ConstantAst constant)
                        {
                            VerifyConstant(constant, structField.Type);
                        }
                    }
                    else if (isConstant && type != TypeKind.Pointer && type != TypeKind.Error)
                    {
                        AddError($"Type of field {structAst.Name}.{structField.Name} is '{PrintTypeDefinition(structField.Type)}', but default value is 'null'", structField.DefaultValue);
                    }
                }

                // 1e. Check for circular dependencies
                if (structAst.Name == structField.Type.Name)
                {
                    AddError($"Struct '{structAst.Name}' contains circular reference in field '{structField.Name}'", structField);
                }
            }

            // 2. Calculate the size of the struct and set field offsets
            if (!structAst.Generics.Any() && errorCount == _programGraph.Errors.Count)
            {
                foreach (var field in structAst.Fields)
                {
                    // 2a. Get the type from type dictionary
                    var fieldTypeName = field.Type.CArray ? field.Type.Generics[0].GenericName :field.Type.GenericName;
                    if (_programGraph.Types.TryGetValue(fieldTypeName, out var type))
                    {
                        // 2b. If the type is a struct and the size hasn't been calculated, verify the struct and calculate the size
                        if (type is StructAst fieldStruct)
                        {
                            if (!fieldStruct.Verified)
                            {
                                VerifyStruct(fieldStruct);
                            }
                        }
                        field.Offset = structAst.Size;
                        structAst.Size += field.Type.CArray ? type.Size * field.Type.ConstCount.Value : type.Size;
                    }
                }
            }
            structAst.Verified = true;
        }

        private void VerifyFunctionDefinition(FunctionAst function, HashSet<string> functionNames, bool main)
        {
            // 1. Verify the return type of the function is valid
            var returnType = VerifyType(function.ReturnType);
            if (returnType == TypeKind.Error)
            {
                AddError($"Return type '{function.ReturnType.Name}' of function '{function.Name}' is not defined", function.ReturnType);
            }
            else if (function.ReturnType.CArray && function.ReturnType.Count == null)
            {
                AddError($"C array for function '{function.Name}' must have a constant size", function.ReturnType);
            }
            else if (function.ReturnType.Count != null)
            {
                var countType = VerifyConstantExpression(function.ReturnType.Count, null, _globalIdentifiers, out var isConstant, out var count);

                if (countType != null)
                {
                    if (isConstant)
                    {
                        if (count < 0)
                        {
                            AddError($"Expected size of return type of function '{function.Name}' to be a positive integer", function.ReturnType.Count);
                        }
                        else
                        {
                            function.ReturnType.ConstCount = (uint)count;
                        }
                    }
                    else
                    {
                        AddError($"Return type of function '{function.Name}' should have constant size", function.ReturnType.Count);
                    }
                }
            }

            // 2. Verify main function return type and arguments
            if (main)
            {
                if (returnType != TypeKind.Void && returnType != TypeKind.Integer)
                {
                    AddError("The main function should return type 'int' or 'void'", function);
                }

                var argument = function.Arguments.FirstOrDefault();
                if (argument != null && !(function.Arguments.Count == 1 && argument.Type.Name == "List" && argument.Type.Generics.FirstOrDefault()?.Name == "string"))
                {
                    AddError("The main function should either have 0 arguments or 'List<string>' argument", function);
                }

                if (function.Generics.Any())
                {
                    AddError("The main function cannot have generics", function);
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
                var type = VerifyType(argument.Type, argument: true);

                switch (type)
                {
                    case TypeKind.VarArgs:
                        if (function.Varargs || function.Params)
                        {
                            AddError($"Function '{function.Name}' cannot have multiple varargs", argument.Type);
                        }
                        function.Varargs = true;
                        function.VarargsCalls = new List<List<TypeDefinition>>();
                        break;
                    case TypeKind.Params:
                        if (function.Varargs || function.Params)
                        {
                            AddError($"Function '{function.Name}' cannot have multiple varargs", argument.Type);
                        }
                        function.Params = true;
                        break;
                    case TypeKind.Error:
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
                    var defaultType = VerifyConstantExpression(argument.Value, null, _globalIdentifiers, out var isConstant, out _);

                    if (argument.HasGenerics)
                    {
                        AddError($"Argument '{argument.Name}' in function '{function.Name}' cannot have default value if the argument has a generic type", argument.Value);
                    }
                    else if (defaultType != null)
                    {
                        if (!isConstant)
                        {
                            AddError($"Expected default value of argument '{argument.Name}' in function '{function.Name}' to be a constant value", argument.Value);

                        }
                        else if (type != TypeKind.Error && !TypeEquals(argument.Type, defaultType))
                        {
                            AddError($"Type of argument '{argument.Name}' in function '{function.Name}' is '{PrintTypeDefinition(argument.Type)}', but default value is type '{PrintTypeDefinition(defaultType)}'", argument.Value);
                        }
                        else if (argument.Type.PrimitiveType != null && argument.Value is ConstantAst constant)
                        {
                            VerifyConstant(constant, argument.Type);
                        }
                    }
                    else if (isConstant && type != TypeKind.Pointer && type != TypeKind.Error)
                    {
                        AddError($"Type of argument '{argument.Name}' in function '{function.Name}' is '{PrintTypeDefinition(argument.Type)}', but default value is 'null'", argument.Value);
                    }
                }
            }

            // 4. Load the function into the dictionary
            if (function.Generics.Any())
            {
                if (!function.ReturnTypeHasGenerics && function.Arguments.All(arg => !arg.HasGenerics))
                {
                    AddError($"Function '{function.Name}' has generic(s), but the generic(s) are not used in the argument(s) or the return type", function);
                }

                if (!_polymorphicFunctions.TryGetValue(function.Name, out var functions))
                {
                    _polymorphicFunctions[function.Name] = functions = new List<FunctionAst>();
                }
                if (functions.Any() && OverloadExistsForFunction(function, functions))
                {
                    AddError($"Function '{function.Name}' has multiple overloads with arguments ({string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", function);
                }
                functions.Add(function);
            }
            else
            {
                functionNames.Add(function.Name);
                function.TypeIndex = _programGraph.TypeCount++;
                if (!_programGraph.Functions.TryGetValue(function.Name, out var functions))
                {
                    _programGraph.Functions[function.Name] = functions = new List<FunctionAst>();
                }
                if (functions.Any())
                {
                    if (function.Extern)
                    {
                        AddError($"Multiple definitions of external function '{function.Name}'", function);
                    }
                    else if (OverloadExistsForFunction(function, functions))
                    {
                        AddError($"Function '{function.Name}' has multiple overloads with arguments ({string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.Type)))})", function);
                    }
                }
                functions.Add(function);
            }
        }

        private bool OverloadExistsForFunction(IFunction currentFunction, List<FunctionAst> existingFunctions)
        {
            foreach (var existingFunction in existingFunctions)
            {
                if (currentFunction.Arguments.Count == existingFunction.Arguments.Count)
                {
                    var match = true;
                    for (var i = 0; i < currentFunction.Arguments.Count; i++)
                    {
                        if (!TypeEquals(currentFunction.Arguments[i].Type, existingFunction.Arguments[i].Type, true))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void VerifyOperatorOverloadDefinition(OperatorOverloadAst overload)
        {
            // 1. Verify the operator type exists and is a struct
            if (overload.Generics.Any())
            {
                if (_polymorphicStructs.TryGetValue(overload.Type.Name, out var structDef))
                {
                    if (structDef.Generics.Count != overload.Generics.Count)
                    {
                        AddError($"Expected type '{overload.Type.Name}' to have {structDef.Generics.Count} generic(s), but got {overload.Generics.Count}", overload.Type);
                    }
                }
                else
                {
                    AddError($"No polymorphic structs of type '{overload.Type.Name}'", overload.Type);
                }
            }
            else
            {
                var targetType = VerifyType(overload.Type);
                switch (targetType)
                {
                    case TypeKind.Error:
                    case TypeKind.Struct:
                    case TypeKind.String when overload.Operator != Operator.Subscript:
                        break;
                    default:
                        AddError($"Cannot overload operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}'", overload.Type);
                        break;
                }
            }

            // 2. Verify the argument types
            if (overload.Arguments.Count != 2)
            {
                AddError($"Overload of operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}' should contain exactly 2 arguments to represent the l-value and r-value of the expression", overload);
            }
            var argumentNames = new HashSet<string>();
            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                // 2a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    AddError($"Overload of operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}' already contains argument '{argument.Name}'", argument);
                }

                // 2b. Check the argument is the same type as the overload type
                if (overload.Operator == Operator.Subscript && i == 1)
                {
                    if (argument.Type.PrimitiveType is not IntegerType)
                    {
                        AddError($"Expected second argument of ");
                        AddError($"Expected second argument of overload of operator '{PrintOperator(overload.Operator)}' to be an integer, but got '{PrintTypeDefinition(argument.Type)}'", argument.Type);
                    }
                }
                else if (!TypeEquals(overload.Type, argument.Type, true))
                {
                    AddError($"Expected overload of operator '{PrintOperator(overload.Operator)}' argument type to be '{PrintTypeDefinition(overload.Type)}', but got '{PrintTypeDefinition(argument.Type)}'", argument.Type);
                }
            }

            // 3. Load the overload into the dictionary
            if (overload.Generics.Any())
            {
                if (!_polymorphicOperatorOverloads.TryGetValue(overload.Type.Name, out var overloads))
                {
                    _polymorphicOperatorOverloads[overload.Type.Name] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
                }
                if (overloads.ContainsKey(overload.Operator))
                {
                    AddError($"Multiple definitions of overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", overload);
                }
                overloads[overload.Operator] = overload;
            }
            else
            {
                if (!_programGraph.OperatorOverloads.TryGetValue(overload.Type.GenericName, out var overloads))
                {
                    _programGraph.OperatorOverloads[overload.Type.GenericName] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
                }
                if (overloads.ContainsKey(overload.Operator))
                {
                    AddError($"Multiple definitions of overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", overload);
                }
                overloads[overload.Operator] = overload;
            }
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
                ResolveCompilerDirectives(function.Children, function);
            }

            // 4. Loop through function body and verify all ASTs
            var returned = VerifyAsts(function.Children, function, scopeIdentifiers);

            // 5. Verify the main function doesn't call the compiler
            if (function.Name == "main" && function.CallsCompiler)
            {
                AddError("The main function cannot call the compiler", function);
            }

            // 6. Verify the function returns on all paths
            if (!returned && returnType != TypeKind.Void)
            {
                AddError($"Function '{function.Name}' does not return type '{PrintTypeDefinition(function.ReturnType)}' on all paths", function);
            }
            function.Verified = true;
        }

        private void VerifyOperatorOverload(OperatorOverloadAst overload)
        {
            // 1. Initialize local variables
            var scopeIdentifiers = new Dictionary<string, IAst>(_globalIdentifiers);
            foreach (var argument in overload.Arguments)
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
            var returnType = VerifyType(overload.ReturnType);

            // 2. Resolve the compiler directives in the body
            if (overload.HasDirectives)
            {
                ResolveCompilerDirectives(overload.Children, overload);
            }

            // 3. Loop through body and verify all ASTs
            var returned = VerifyAsts(overload.Children, overload, scopeIdentifiers);

            // 4. Verify the body returns on all paths
            if (!returned)
            {
                AddError($"Overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}' does not return type '{PrintTypeDefinition(overload.ReturnType)}' on all paths", overload);
            }
            overload.Verified = true;
        }

        private void ResolveCompilerDirectives(List<IAst> asts, IFunction function)
        {
            for (int i = 0; i < asts.Count; i++)
            {
                var ast = asts[i];
                switch (ast)
                {
                    case ScopeAst:
                    case WhileAst:
                    case EachAst:
                        ResolveCompilerDirectives(ast.Children, function);
                        break;
                    case ConditionalAst conditional:
                        ResolveCompilerDirectives(conditional.Children, function);
                        ResolveCompilerDirectives(conditional.Else, function);
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
                                        if (function is FunctionAst functionAst)
                                        {
                                            AddError($"Assertion failed in function '{functionAst.Name}'", directive.Value);
                                        }
                                        else if (function is OperatorOverloadAst overload)
                                        {
                                            AddError($"Assertion failed in overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", directive.Value);
                                        }
                                    }
                                }
                                break;
                        }
                        i--;
                        break;
                }
            }
        }

        private bool VerifyAsts(List<IAst> asts, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
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

        private bool VerifyScope(List<IAst> syntaxTrees, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, IAst>(scopeIdentifiers);

            // 2. Verify function lines
            return VerifyAsts(syntaxTrees, currentFunction, scopeVariables);
        }

        private bool VerifyAst(IAst syntaxTree, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
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

        private void VerifyReturnStatement(ReturnAst returnAst, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Infer the return type of the function
            var returnType = VerifyType(currentFunction.ReturnType);

            // 2. Handle void case since it's the easiest to interpret
            if (returnType == TypeKind.Void)
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

        private void VerifyDeclaration(DeclarationAst declaration, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
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
                    if (type == TypeKind.Error)
                    {
                        AddError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type);
                    }
                    else if (type != TypeKind.Pointer)
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
                    if (type != TypeKind.Struct && type != TypeKind.String)
                    {
                        AddError($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.Type)}'", declaration.Type);
                        return;
                    }

                    var structDef = _programGraph.Types[declaration.Type.GenericName] as StructAst;
                    var fields = structDef!.Fields.ToDictionary(_ => _.Name);
                    foreach (var assignment in declaration.Assignments)
                    {
                        StructFieldAst field = null;
                        if (assignment.Reference is not IdentifierAst identifier)
                        {
                            AddError("Expected to get field in object initializer", assignment.Reference);
                        }
                        else if (!fields.TryGetValue(identifier.Name, out field))
                        {
                            AddError($"Field '{identifier.Name}' not present in struct '{PrintTypeDefinition(declaration.Type)}'", assignment.Reference);
                        }

                        if (assignment.Operator != Operator.None)
                        {
                            AddError("Cannot have operator assignments in object initializers", assignment.Reference);
                        }

                        var valueType = VerifyExpression(assignment.Value, currentFunction, scopeIdentifiers);
                        if (valueType != null && field != null)
                        {
                            if (!TypeEquals(field.Type, valueType))
                            {
                                AddError($"Expected field value to be type '{PrintTypeDefinition(field.Type)}', but got '{PrintTypeDefinition(valueType)}'", field.Type);
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
                            declaration.Value = GetOSVersion();
                            break;
                        case "build_env":
                            declaration.Value = GetBuildEnv();
                            break;
                    }
                }

                var valueType = VerifyExpression(declaration.Value, currentFunction, scopeIdentifiers);

                // 4a. Verify the assignment value matches the type definition if it has been defined
                if (declaration.Type == null)
                {
                    if (VerifyType(valueType) == TypeKind.Void)
                    {
                        AddError($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                        return;
                    }
                    declaration.Type = valueType;
                }
                else
                {
                    var type = VerifyType(declaration.Type);
                    if (type == TypeKind.Error)
                    {
                        AddError($"Undefined type in declaration '{PrintTypeDefinition(declaration.Type)}'", declaration.Type);
                    }
                    else if (type == TypeKind.Void)
                    {
                        AddError($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Type);
                    }

                    // Verify the type is correct
                    if (valueType != null)
                    {
                        if (!TypeEquals(declaration.Type, valueType))
                        {
                            AddError($"Expected declaration value to be type '{PrintTypeDefinition(declaration.Type)}', but got '{PrintTypeDefinition(valueType)}'", declaration.Type);
                        }
                        else if (declaration.Type.PrimitiveType != null && declaration.Value is ConstantAst constant)
                        {
                            VerifyConstant(constant, declaration.Type);
                        }
                    }
                }
            }

            // 5. Verify the type definition count if necessary
            if (declaration.Type != null)
            {
                if (declaration.Type.CArray && declaration.Type.Count == null)
                {
                    AddError($"C array of variable '{declaration.Name}' must be initialized to a constant integer", declaration.Type);
                }
                else if (declaration.Type.Count != null)
                {
                    var countType = VerifyConstantExpression(declaration.Type.Count, currentFunction, scopeIdentifiers, out var isConstant, out var count);

                    if (countType != null)
                    {
                        if (declaration.Type.CArray && !isConstant)
                        {
                            AddError($"C array of variable '{declaration.Name}' must be initialized with a constant size", declaration.Type.Count);
                        }
                        else if (countType.PrimitiveType is not IntegerType)
                        {
                            AddError($"Expected count of variable '{declaration.Name}' to be an integer", declaration.Type.Count);
                        }
                        if (isConstant)
                        {
                            if (count < 0)
                            {
                                AddError($"Expected size of variable '{declaration.Name}' to be a positive integer", declaration.Type.Count);
                            }
                            else
                            {
                                declaration.Type.ConstCount = (uint)count;
                            }
                        }
                    }
                }
            }

            // 6. Verify constant values
            if (declaration.Constant)
            {
                switch (declaration.Value)
                {
                    case ConstantAst:
                    case StructFieldRefAst structField when structField.IsEnum:
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

        private StructFieldRefAst GetOSVersion()
        {
            return new StructFieldRefAst
            {
                Children = {
                    new IdentifierAst {Name = "OS"},
                    new IdentifierAst
                    {
                        Name = Environment.OSVersion.Platform switch
                        {
                            PlatformID.Unix => "Linux",
                            PlatformID.Win32NT => "Windows",
                            PlatformID.MacOSX => "Mac",
                            _ => "None"
                        }
                    }
                }
            };
        }

        private StructFieldRefAst GetBuildEnv()
        {
            return new StructFieldRefAst
            {
                Children = {
                    new IdentifierAst {Name = "BuildEnv"},
                    new IdentifierAst {Name = _buildSettings.Release ? "Release" : "Debug"}
                }
            };
        }

        private void VerifyAssignment(AssignmentAst assignment, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the variable is already defined and that it is not a constant
            var variableTypeDefinition = GetReference(assignment.Reference, currentFunction, scopeIdentifiers, out _);
            if (variableTypeDefinition == null) return;

            if (variableTypeDefinition.Constant)
            {
                var variable = assignment.Reference as IdentifierAst;
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
                    var lhs = VerifyType(variableTypeDefinition);
                    var rhs = VerifyType(valueType);
                    switch (assignment.Operator)
                    {
                        // Both need to be bool and returns bool
                        case Operator.And:
                        case Operator.Or:
                            if (lhs != TypeKind.Boolean || rhs != TypeKind.Boolean)
                            {
                                AddError($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
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
                            if (!(lhs == TypeKind.Integer && rhs == TypeKind.Integer) &&
                                !(lhs == TypeKind.Float && (rhs == TypeKind.Float || rhs == TypeKind.Integer)))
                            {
                                AddError($"Operator {PrintOperator(assignment.Operator)} not applicable to types '{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (!(lhs == TypeKind.Boolean && rhs == TypeKind.Boolean) &&
                                    !(lhs == TypeKind.Integer && rhs == TypeKind.Integer))
                            {
                                AddError($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
                                        $"'{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                        // Requires both to be integers
                        case Operator.ShiftLeft:
                        case Operator.ShiftRight:
                        case Operator.RotateLeft:
                        case Operator.RotateRight:
                            if (lhs != TypeKind.Integer || rhs != TypeKind.Integer)
                            {
                                AddError($"Operator {PrintOperator(assignment.Operator)} not applicable to types '{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
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

        private TypeDefinition GetReference(IAst ast, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers, out bool hasPointer)
        {
            hasPointer = true;
            switch (ast)
            {
                case IdentifierAst identifier:
                    return GetVariable(identifier.Name, identifier, scopeIdentifiers);
                case IndexAst index:
                {
                    var variableType = GetVariable(index.Name, index, scopeIdentifiers);
                    if (variableType == null) return null;
                    var type = VerifyIndex(index, variableType, currentFunction, scopeIdentifiers, out var overloaded);
                    if (type != null && overloaded)
                    {
                        if (type.TypeKind != TypeKind.Pointer)
                        {
                            AddError($"Overload [] for type '{PrintTypeDefinition(variableType)}' must be a pointer to be able to set the value", index);
                            return null;
                        }
                        hasPointer = false;
                        return type.Generics[0];
                    }
                    return type;
                }
                case StructFieldRefAst structFieldRef:
                {
                    structFieldRef.Pointers = new bool[structFieldRef.Children.Count - 1];
                    structFieldRef.TypeNames = new string[structFieldRef.Children.Count - 1];
                    structFieldRef.ValueIndices = new int[structFieldRef.Children.Count - 1];

                    TypeDefinition refType;
                    switch (structFieldRef.Children[0])
                    {
                        case IdentifierAst identifier:
                            refType = GetVariable(identifier.Name, identifier, scopeIdentifiers, true);
                            break;
                        case IndexAst index:
                            var variableType = GetVariable(index.Name, index, scopeIdentifiers);
                            if (variableType == null) return null;
                            refType = VerifyIndex(index, variableType, currentFunction, scopeIdentifiers, out var overloaded);
                            if (refType != null && overloaded && refType.TypeKind != TypeKind.Pointer)
                            {
                                AddError($"Overload [] for type '{PrintTypeDefinition(variableType)}' must be a pointer to be able to set the value", index);
                                return null;
                            }
                            break;
                        default:
                            AddError("Expected to have a reference to a variable, field, or pointer", structFieldRef.Children[0]);
                            return null;
                    }
                    if (refType == null)
                    {
                        return null;
                    }

                    for (var i = 1; i < structFieldRef.Children.Count; i++)
                    {
                        switch (structFieldRef.Children[i])
                        {
                            case IdentifierAst identifier:
                                refType = VerifyStructField(identifier.Name, refType, structFieldRef, i-1, identifier);
                                break;
                            case IndexAst index:
                                var fieldType = VerifyStructField(index.Name, refType, structFieldRef, i-1, index);
                                if (fieldType == null) return null;
                                refType = VerifyIndex(index, fieldType, currentFunction, scopeIdentifiers, out var overloaded);
                                if (refType != null && overloaded)
                                {
                                    if (refType.TypeKind == TypeKind.Pointer)
                                    {
                                        if (i == structFieldRef.Children.Count - 1)
                                        {
                                            hasPointer = false;
                                            refType = refType.Generics[0];
                                        }
                                    }
                                    else
                                    {
                                        AddError($"Overload [] for type '{PrintTypeDefinition(fieldType)}' must be a pointer to be able to set the value", index);
                                        return null;
                                    }
                                }
                                break;
                            default:
                                AddError("Expected to have a reference to a variable, field, or pointer", structFieldRef.Children[i]);
                                return null;
                        }
                        if (refType == null)
                        {
                            return null;
                        }
                    }

                    return refType;
                }
                case UnaryAst unary when unary.Operator == UnaryOperator.Dereference:
                    var reference = GetReference(unary.Value, currentFunction, scopeIdentifiers, out var canDereference);
                    if (reference == null)
                    {
                        return null;
                    }
                    if (!canDereference)
                    {
                        AddError("Cannot dereference pointer to assign value", unary.Value);
                        return null;
                    }

                    if (reference.TypeKind != TypeKind.Pointer)
                    {
                        AddError("Expected to get pointer to dereference", unary.Value);
                        return null;
                    }
                    return reference.Generics[0];
                default:
                    AddError("Expected to have a reference to a variable, field, or pointer", ast);
                    return null;
            }
        }

        private TypeDefinition GetVariable(string name, IAst ast, IDictionary<string, IAst> scopeIdentifiers, bool allowEnums = false)
        {
            if (!scopeIdentifiers.TryGetValue(name, out var identifier))
            {
                AddError($"Variable '{name}' not defined", ast);
                return null;
            }
            if (allowEnums && identifier is EnumAst enumAst)
            {
                return new TypeDefinition {Name = enumAst.Name};
            }
            if (identifier is not DeclarationAst declaration)
            {
                AddError($"Identifier '{name}' is not a variable", ast);
                return null;
            }
            return declaration.Type;
        }

        private TypeDefinition VerifyStructFieldRef(StructFieldRefAst structField, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            TypeDefinition refType;
            switch (structField.Children[0])
            {
                case IdentifierAst identifier:
                    if (!scopeIdentifiers.TryGetValue(identifier.Name, out var value))
                    {
                        AddError($"Identifier '{identifier.Name}' not defined", structField);
                        return null;
                    }
                    switch (value)
                    {
                        case EnumAst enumAst:
                            return VerifyEnumValue(enumAst, structField);
                        case DeclarationAst declaration:
                            refType = declaration.Type;
                            break;
                        default:
                            AddError($"Cannot reference static field of type '{identifier.Name}'", structField);
                            return null;
                    }
                    break;
                default:
                    refType = VerifyExpression(structField.Children[0], currentFunction, scopeIdentifiers);
                    break;
            }
            if (refType == null)
            {
                return null;
            }
            structField.Pointers = new bool[structField.Children.Count - 1];
            structField.TypeNames = new string[structField.Children.Count - 1];
            structField.ValueIndices = new int[structField.Children.Count - 1];

            for (var i = 1; i < structField.Children.Count; i++)
            {
                switch (structField.Children[i])
                {
                    case IdentifierAst identifier:
                        refType = VerifyStructField(identifier.Name, refType, structField, i-1, identifier);
                        break;
                    case IndexAst index:
                        var fieldType = VerifyStructField(index.Name, refType, structField, i-1, index);
                        if (fieldType == null) return null;
                        refType = VerifyIndex(index, fieldType, currentFunction, scopeIdentifiers, out _);
                        break;
                    default:
                        AddError("Expected to have a reference to a variable, field, or pointer", structField.Children[i]);
                        return null;
                }
                if (refType == null)
                {
                    return null;
                }
            }
            return refType;
        }

        private TypeDefinition VerifyStructField(string fieldName, TypeDefinition structType, StructFieldRefAst structField, int fieldIndex, IAst ast)
        {
            // 1. Load the struct definition in typeDefinition
            if (structType.Name == "*")
            {
                structType = structType.Generics[0];
                structField.Pointers[fieldIndex] = true;
            }
            var genericName = structType.GenericName;
            structField.TypeNames[fieldIndex] = genericName;
            if (!_programGraph.Types.TryGetValue(genericName, out var typeDefinition))
            {
                AddError($"Struct '{PrintTypeDefinition(structType)}' not defined", ast);
                return null;
            }
            if (typeDefinition is not StructAst structDefinition)
            {
                AddError($"Type '{PrintTypeDefinition(structType)}' does not contain field '{fieldName}'", ast);
                return null;
            }

            // 2. Get the field definition and set the field index
            StructFieldAst field = null;
            for (var i = 0; i < structDefinition.Fields.Count; i++)
            {
                if (structDefinition.Fields[i].Name == fieldName)
                {
                    structField.ValueIndices[fieldIndex] = i;
                    field = structDefinition.Fields[i];
                    break;
                }
            }
            if (field == null)
            {
                AddError($"Struct '{PrintTypeDefinition(structType)}' does not contain field '{fieldName}'", ast);
                return null;
            }

            return field.Type;
        }

        private bool VerifyConditional(ConditionalAst conditional, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
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

        private bool VerifyWhile(WhileAst whileAst, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the condition expression
            VerifyCondition(whileAst.Condition, currentFunction, scopeIdentifiers);

            // 2. Verify the scope of the while block
            return VerifyScope(whileAst.Children, currentFunction, scopeIdentifiers);
        }

        private bool VerifyCondition(IAst ast, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            var conditionalType = VerifyExpression(ast, currentFunction, scopeIdentifiers);
            switch (VerifyType(conditionalType))
            {
                case TypeKind.Integer:
                case TypeKind.Float:
                case TypeKind.Boolean:
                case TypeKind.Pointer:
                    // Valid types
                    return !_programGraph.Errors.Any();
                case TypeKind.Error:
                    return false;
                default:
                    AddError($"Expected condition to be bool, int, float, or pointer, but got '{PrintTypeDefinition(conditionalType)}'", ast);
                    return false;
            }
        }

        private bool VerifyEach(EachAst each, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
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
                var begin = VerifyExpression(each.RangeBegin, currentFunction, scopeIdentifiers);
                var beginType = VerifyType(begin);
                if (beginType != TypeKind.Integer && beginType != TypeKind.Error)
                {
                    AddError($"Expected range to begin with 'int', but got '{PrintTypeDefinition(begin)}'", each.RangeBegin);
                }
                var end = VerifyExpression(each.RangeEnd, currentFunction, scopeIdentifiers);
                var endType = VerifyType(end);
                if (endType != TypeKind.Integer && endType != TypeKind.Error)
                {
                    AddError($"Expected range to end with 'int', but got '{PrintTypeDefinition(end)}'", each.RangeEnd);
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

        private TypeDefinition VerifyConstantExpression(IAst ast, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers, out bool isConstant, out int count)
        {
            isConstant = false;
            count = 0;
            switch (ast)
            {
                case ConstantAst constant:
                    isConstant = true;
                    if (constant.Type.PrimitiveType is IntegerType)
                    {
                        int.TryParse(constant.Value, out count);
                    }
                    return constant.Type;
                case NullAst:
                    isConstant = true;
                    return null;
                case StructFieldRefAst structField:
                    var structFieldType = VerifyStructFieldRef(structField, currentFunction, scopeIdentifiers);
                    isConstant = structFieldType?.PrimitiveType is EnumType;
                    return structFieldType;
                case IdentifierAst identifierAst:
                    if (!scopeIdentifiers.TryGetValue(identifierAst.Name, out var identifier))
                    {
                        if (_programGraph.Functions.TryGetValue(identifierAst.Name, out var functions))
                        {
                            if (functions.Count > 1)
                            {
                                AddError($"Cannot determine type for function '{identifierAst.Name}' that has multiple overloads", identifierAst);
                                return null;
                            }
                            return new TypeDefinition {Name = "Type", TypeIndex = functions[0].TypeIndex};
                        }
                        AddError($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                    }
                    switch (identifier)
                    {
                        case DeclarationAst declaration:
                            isConstant = declaration.Constant;
                            if (isConstant && declaration.Type.PrimitiveType is IntegerType)
                            {
                                if (declaration.Value is ConstantAst constValue)
                                {
                                    int.TryParse(constValue.Value, out count);
                                }
                            }
                            return declaration.Type;
                        case IType type:
                            if (type is StructAst structAst && structAst.Generics.Any())
                            {
                                AddError($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                            }
                            return new TypeDefinition {Name = "Type", TypeIndex = type.TypeIndex};
                        default:
                            return null;
                    }
                default:
                    return VerifyExpression(ast, currentFunction, scopeIdentifiers);
            }
        }

        private TypeDefinition VerifyExpression(IAst ast, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Verify the expression value
            switch (ast)
            {
                case ConstantAst constant:
                    return constant.Type;
                case NullAst:
                    return null;
                case StructFieldRefAst structField:
                    return VerifyStructFieldRef(structField, currentFunction, scopeIdentifiers);
                case IdentifierAst identifierAst:
                    {
                        if (!scopeIdentifiers.TryGetValue(identifierAst.Name, out var identifier))
                        {
                            if (_programGraph.Functions.TryGetValue(identifierAst.Name, out var functions))
                            {
                                if (functions.Count > 1)
                                {
                                    AddError($"Cannot determine type for function '{identifierAst.Name}' that has multiple overloads", identifierAst);
                                    return null;
                                }
                                return new TypeDefinition {Name = "Type", TypeIndex = functions[0].TypeIndex};
                            }
                            AddError($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                        }
                        switch (identifier)
                        {
                            case DeclarationAst declaration:
                                return declaration.Type;
                            case IType type:
                                if (type is StructAst structAst && structAst.Generics.Any())
                                {
                                    AddError($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                                }
                                return new TypeDefinition {Name = "Type", TypeIndex = type.TypeIndex};
                            default:
                                return null;
                        }
                    }
                case ChangeByOneAst changeByOne:
                    var op = changeByOne.Positive ? "increment" : "decrement";
                    switch (changeByOne.Value)
                    {
                        case IdentifierAst:
                        case StructFieldRefAst:
                        case IndexAst:
                            var expressionType = GetReference(changeByOne.Value, currentFunction, scopeIdentifiers, out _);
                            if (expressionType != null)
                            {
                                var type = VerifyType(expressionType);
                                if (type != TypeKind.Integer && type != TypeKind.Float)
                                {
                                    AddError($"Expected to {op} int or float, but got type '{PrintTypeDefinition(expressionType)}'", changeByOne.Value);
                                    return null;
                                }
                            }

                            return expressionType;
                        default:
                            AddError($"Expected to {op} variable", changeByOne);
                            return null;
                    }
                case UnaryAst unary:
                {
                    if (unary.Operator == UnaryOperator.Reference)
                    {
                        var referenceType = GetReference(unary.Value, currentFunction, scopeIdentifiers, out var hasPointer);
                        if (!hasPointer)
                        {
                            AddError("Unable to get reference of unary value", unary.Value);
                            return null;
                        }

                        var type = VerifyType(referenceType);
                        if (type == TypeKind.Error)
                        {
                            return null;
                        }

                        var pointerType = new TypeDefinition {Name = "*"};
                        if (referenceType.CArray)
                        {
                            pointerType.Generics.Add(referenceType.Generics[0]);
                        }
                        else
                        {
                            pointerType.Generics.Add(referenceType);
                        }
                        return pointerType;
                    }
                    else
                    {
                        var valueType = VerifyExpression(unary.Value, currentFunction, scopeIdentifiers);
                        var type = VerifyType(valueType);
                        switch (unary.Operator)
                        {
                            case UnaryOperator.Not:
                                if (type == TypeKind.Boolean)
                                {
                                    return valueType;
                                }
                                else if (type != TypeKind.Error)
                                {
                                    AddError($"Expected type 'bool', but got type '{PrintTypeDefinition(valueType)}'", unary.Value);
                                }
                                return null;
                            case UnaryOperator.Negate:
                                if (type == TypeKind.Integer || type == TypeKind.Float)
                                {
                                    return valueType;
                                }
                                else if (type != TypeKind.Error)
                                {
                                    AddError($"Negation not compatible with type '{PrintTypeDefinition(valueType)}'", unary.Value);
                                }
                                return null;
                            case UnaryOperator.Dereference:
                                if (type == TypeKind.Pointer)
                                {
                                    return valueType.Generics[0];
                                }
                                else if (type != TypeKind.Error)
                                {
                                    AddError($"Cannot dereference type '{PrintTypeDefinition(valueType)}'", unary.Value);
                                }
                                return null;
                            default:
                                AddError($"Unexpected unary operator '{unary.Operator}'", unary.Value);
                                return null;
                        }
                    }
                }
                case CallAst call:
                    return VerifyCall(call, currentFunction, scopeIdentifiers);
                case ExpressionAst expression:
                    return VerifyExpressionType(expression, currentFunction, scopeIdentifiers);
                case IndexAst index:
                    return VerifyIndexType(index, currentFunction, scopeIdentifiers);
                case TypeDefinition typeDef:
                {
                    if (VerifyType(typeDef) == TypeKind.Error)
                    {
                        return null;
                    }
                    if (!_programGraph.Types.TryGetValue(typeDef.GenericName, out var type))
                    {
                        return null;
                    }
                    return new TypeDefinition {Name = "Type", TypeIndex = type.TypeIndex};
                }
                case CastAst cast:
                {
                    var targetType = VerifyType(cast.TargetType);
                    var valueType = VerifyExpression(cast.Value, currentFunction, scopeIdentifiers);
                    switch (targetType)
                    {
                        case TypeKind.Integer:
                        case TypeKind.Float:
                            if (valueType != null && valueType.PrimitiveType == null)
                            {
                                AddError($"Unable to cast type '{PrintTypeDefinition(valueType)}' to '{PrintTypeDefinition(cast.TargetType)}'", cast.Value);
                            }
                            break;
                        case TypeKind.Error:
                            // Don't need to report additional errors
                            return null;
                        default:
                            if (valueType != null)
                            {
                                AddError($"Unable to cast type '{PrintTypeDefinition(valueType)}' to '{PrintTypeDefinition(cast.TargetType)}'", cast);
                            }
                            break;
                    }
                    return cast.TargetType;
                }
                case null:
                    return null;
                default:
                    AddError($"Invalid expression", ast);
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

        private TypeDefinition VerifyEnumValue(EnumAst enumAst, StructFieldRefAst structField)
        {
            structField.IsEnum = true;
            structField.TypeNames = new [] {enumAst.Name};

            if (structField.Children.Count > 2)
            {
                AddError("Cannot get a value of an enum value", structField.Children[2]);
                return null;
            }

            if (structField.Children[1] is not IdentifierAst value)
            {
                AddError($"Value of enum '{enumAst.Name}' should be an identifier", structField.Children[1]);
                return null;
            }

            EnumValueAst enumValue = null;
            for (var i = 0; i < enumAst.Values.Count; i++)
            {
                if (enumAst.Values[i].Name == value.Name)
                {
                    structField.ValueIndices = new [] {i};
                    enumValue = enumAst.Values[i];
                    break;
                }
            }

            if (enumValue == null)
            {
                AddError($"Enum '{enumAst.Name}' does not contain value '{value.Name}'", value);
                return null;
            }

            var primitive = enumAst.BaseType.PrimitiveType;
            return new TypeDefinition {Name = enumAst.Name, PrimitiveType = new EnumType {Bytes = primitive.Bytes, Signed = primitive.Signed}};
        }

        private TypeDefinition VerifyCall(CallAst call, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            var arguments = new TypeDefinition[call.Arguments.Count];
            var argumentsError = false;

            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var argument = call.Arguments[i];
                var argumentType = VerifyExpression(argument, currentFunction, scopeIdentifiers);
                if (argumentType == null && argument is not NullAst)
                {
                    argumentsError = true;
                }
                arguments[i] = argumentType;
            }

            var specifiedArguments = new Dictionary<string, TypeDefinition>();
            if (call.SpecifiedArguments != null)
            {
                foreach (var (name, argument) in call.SpecifiedArguments)
                {
                    var argumentType = VerifyExpression(argument, currentFunction, scopeIdentifiers);
                    if (argumentType == null && argument is not NullAst)
                    {
                        argumentsError = true;
                    }
                    specifiedArguments[name] = argumentType;
                }
            }

            if (call.Generics != null)
            {
                foreach (var generic in call.Generics)
                {
                    if (VerifyType(generic) == TypeKind.Error)
                    {
                        AddError($"Undefined generic type '{PrintTypeDefinition(generic)}'", generic);
                        argumentsError = true;
                    }
                }
            }

            if (argumentsError)
            {
                _programGraph.Functions.TryGetValue(call.Function, out var functions);
                _polymorphicFunctions.TryGetValue(call.Function, out var polymorphicFunctions);
                if (functions == null)
                {
                    if (polymorphicFunctions == null)
                    {
                        AddError($"Call to undefined function '{call.Function}'", call);
                        return null;
                    }

                    if (polymorphicFunctions.Count == 1)
                    {
                        var calledFunction = polymorphicFunctions[0];
                        return calledFunction.ReturnTypeHasGenerics ? null : calledFunction.ReturnType;
                    }
                }
                else if (polymorphicFunctions == null && functions.Count == 1)
                {
                    return functions[0].ReturnType;
                }
                return null;
            }

            var function = DetermineCallingFunction(call, arguments, specifiedArguments);

            if (function == null)
            {
                AddError($"No overload of function '{call.Function}' found with given arguments", call);
                return null;
            }

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

            // Verify call arguments match the types of the function arguments
            for (var i = 0; i < argumentCount; i++)
            {
                var functionArg = function.Arguments[i];
                if (call.SpecifiedArguments != null && call.SpecifiedArguments.TryGetValue(functionArg.Name, out var specifiedArgument))
                {
                    if (functionArg.Type.PrimitiveType != null && specifiedArgument is ConstantAst constant)
                    {
                        VerifyConstant(constant, functionArg.Type);
                    }

                    call.Arguments.Insert(i, specifiedArgument);
                    continue;
                }

                var argumentAst = call.Arguments.ElementAtOrDefault(i);
                if (argumentAst == null)
                {
                    call.Arguments.Insert(i, functionArg.Value);
                }
                else if (argumentAst is NullAst nullAst)
                {
                    nullAst.TargetType = functionArg.Type;
                }
                else
                {
                    var argument = arguments[i];
                    if (argument != null)
                    {
                        if (functionArg.Type.Name == "Type")
                        {
                            var typeIndex = new ConstantAst
                            {
                                Type = new TypeDefinition {PrimitiveType = new IntegerType {Signed = true, Bytes = 4}}
                            };
                            if (argument.TypeIndex.HasValue)
                            {
                                typeIndex.Value = argument.TypeIndex.ToString();
                            }
                            else if (argument.Name == "Type")
                            {
                                continue;
                            }
                            else
                            {
                                var type = _programGraph.Types[argument.GenericName];
                                typeIndex.Value = type.TypeIndex.ToString();
                            }
                            call.Arguments[i] = typeIndex;
                            arguments[i] = typeIndex.Type;
                        }
                        else if (argument.PrimitiveType != null && call.Arguments[i] is ConstantAst constant)
                        {
                            VerifyConstant(constant, functionArg.Type);
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
                    for (var i = argumentCount; i < arguments.Length; i++)
                    {
                        var argumentAst = call.Arguments[i];
                        if (argumentAst is NullAst nullAst)
                        {
                            nullAst.TargetType = paramsType;
                        }
                        else
                        {
                            var argument = arguments[i];
                            if (argument != null)
                            {
                                if (paramsType.Name == "Type")
                                {
                                    var typeIndex = new ConstantAst
                                    {
                                        Type = new TypeDefinition {PrimitiveType = new IntegerType {Signed = true, Bytes = 4}}
                                    };
                                    if (argument.TypeIndex.HasValue)
                                    {
                                        typeIndex.Value = argument.TypeIndex.ToString();
                                    }
                                    else if (argument.Name == "Type")
                                    {
                                        continue;
                                    }

                                    else
                                    {
                                        var type = _programGraph.Types[argument.GenericName];
                                        typeIndex.Value = type.TypeIndex.ToString();
                                    }
                                    call.Arguments[i] = typeIndex;
                                    arguments[i] = typeIndex.Type;
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
                for (var i = 0; i < arguments.Length; i++)
                {
                    var argument = arguments[i];
                    switch (argument?.PrimitiveType)
                    {
                        // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                        // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                        case FloatType {Bytes: 4}:
                            arguments[i] = argument = new TypeDefinition
                            {
                                Name = "float64", PrimitiveType = new FloatType {Bytes = 8}
                            };
                            break;
                        // Enums should be called as integers
                        case EnumType enumType:
                            arguments[i] = argument = new TypeDefinition
                            {
                                Name = argument.Name,
                                PrimitiveType = new IntegerType {Bytes = enumType.Bytes, Signed = enumType.Signed}
                            };
                            break;
                    }

                }
                var found = false;
                for (var index = 0; index < function.VarargsCalls.Count; index++)
                {
                    var callTypes = function.VarargsCalls[index];
                    if (callTypes.Count == arguments.Length)
                    {
                        var callMatches = true;
                        for (var i = 0; i < callTypes.Count; i++)
                        {
                            if (!TypeEquals(callTypes[i], arguments[i], true))
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
                    function.VarargsCalls.Add(arguments.ToList());
                }
            }

            return function.ReturnType;
        }

        private FunctionAst DetermineCallingFunction(CallAst call, TypeDefinition[] arguments, Dictionary<string, TypeDefinition> specifiedArguments)
        {
            if (_programGraph.Functions.TryGetValue(call.Function, out var functions))
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    var match = true;
                    var callArgIndex = 0;
                    var functionArgCount = function.Varargs || function.Params ? function.Arguments.Count - 1 : function.Arguments.Count;

                    if (call.SpecifiedArguments != null)
                    {
                        var specifiedArgsMatch = true;
                        foreach (var (name, argument) in call.SpecifiedArguments)
                        {
                            var found = false;
                            for (var argIndex = 0; argIndex < function.Arguments.Count; argIndex++)
                            {
                                var functionArg = function.Arguments[argIndex];
                                if (functionArg.Name == name)
                                {
                                    found = VerifyArgument(argument, specifiedArguments[name], functionArg.Type, function.Extern);
                                    break;
                                }
                            }
                            if (!found)
                            {
                                specifiedArgsMatch = false;
                                break;
                            }
                        }
                        if (!specifiedArgsMatch)
                        {
                            continue;
                        }
                    }

                    for (var arg = 0; arg < functionArgCount; arg++)
                    {
                        var functionArg = function.Arguments[arg];
                        if (specifiedArguments.ContainsKey(functionArg.Name))
                        {
                            continue;
                        }

                        var argumentAst = call.Arguments.ElementAtOrDefault(callArgIndex);
                        if (argumentAst == null)
                        {
                            if (functionArg.Value == null)
                            {
                                match = false;
                                break;
                            }
                        }
                        else
                        {
                            if (!VerifyArgument(argumentAst, arguments[callArgIndex], functionArg.Type, function.Extern))
                            {
                                match = false;
                                break;
                            }
                            callArgIndex++;
                        }
                    }

                    if (match && function.Params)
                    {
                        var paramsType = function.Arguments[^1].Type.Generics.FirstOrDefault();

                        if (paramsType != null)
                        {
                            for (; callArgIndex < arguments.Length; callArgIndex++)
                            {
                                if (!VerifyArgument(call.Arguments[callArgIndex], arguments[callArgIndex], paramsType))
                                {
                                    match = false;
                                    break;
                                }
                            }
                        }
                    }

                    if (match && (function.Varargs || callArgIndex == call.Arguments.Count))
                    {
                        call.FunctionIndex = i;
                        return function;
                    }
                }
            }

            if (_polymorphicFunctions.TryGetValue(call.Function, out var polymorphicFunctions))
            {
                for (var i = 0; i < polymorphicFunctions.Count; i++)
                {
                    var function = polymorphicFunctions[i];

                    if (call.Generics != null && call.Generics.Count != function.Generics.Count)
                    {
                        continue;
                    }

                    var match = true;
                    var callArgIndex = 0;
                    var functionArgCount = function.Varargs || function.Params ? function.Arguments.Count - 1 : function.Arguments.Count;
                    var genericTypes = new TypeDefinition[function.Generics.Count];

                    if (call.SpecifiedArguments != null)
                    {
                        var specifiedArgsMatch = true;
                        foreach (var (name, argument) in call.SpecifiedArguments)
                        {
                            var found = false;
                            for (var argIndex = 0; argIndex < function.Arguments.Count; argIndex++)
                            {
                                var functionArg = function.Arguments[argIndex];
                                if (functionArg.Name == name)
                                {
                                    if (functionArg.HasGenerics)
                                    {
                                        found = VerifyPolymorphicArgument(argument, specifiedArguments[name], functionArg.Type, genericTypes);
                                    }
                                    else
                                    {
                                        found = VerifyArgument(argument, specifiedArguments[name], functionArg.Type);
                                    }
                                    break;
                                }
                            }
                            if (!found)
                            {
                                specifiedArgsMatch = false;
                                break;
                            }
                        }
                        if (!specifiedArgsMatch)
                        {
                            continue;
                        }
                    }

                    for (var arg = 0; arg < functionArgCount; arg++)
                    {
                        var functionArg = function.Arguments[arg];
                        if (specifiedArguments.ContainsKey(functionArg.Name))
                        {
                            continue;
                        }

                        var argumentAst = call.Arguments.ElementAtOrDefault(callArgIndex);
                        if (argumentAst == null)
                        {
                            if (functionArg.Value == null)
                            {
                                match = false;
                                break;
                            }
                        }
                        else
                        {
                            if (functionArg.HasGenerics)
                            {
                                if (!VerifyPolymorphicArgument(argumentAst, arguments[callArgIndex], functionArg.Type, genericTypes))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            else
                            {
                                if (!VerifyArgument(argumentAst, arguments[callArgIndex],functionArg.Type))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            callArgIndex++;
                        }
                    }

                    if (match && function.Params)
                    {
                        var paramsArgument = function.Arguments[^1];
                        var paramsType = paramsArgument.Type.Generics.FirstOrDefault();

                        if (paramsType != null)
                        {
                            if (paramsArgument.HasGenerics)
                            {
                                for (; callArgIndex < arguments.Length; callArgIndex++)
                                {
                                    if (!VerifyPolymorphicArgument(call.Arguments[callArgIndex], arguments[callArgIndex], paramsType, genericTypes))
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                for (; callArgIndex < arguments.Length; callArgIndex++)
                                {
                                    if (!VerifyArgument(call.Arguments[callArgIndex], arguments[callArgIndex], paramsType))
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (call.Generics != null)
                    {
                        if (genericTypes.Any(t => t != null))
                        {
                            var genericTypesMatch = true;
                            for (var t = 0; t < genericTypes.Length; t++)
                            {
                                if (!TypeEquals(call.Generics[i], genericTypes[i], true))
                                {
                                    genericTypesMatch = false;
                                    break;
                                }
                            }
                            if (!genericTypesMatch)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            for (var t = 0; t < genericTypes.Length; t++)
                            {
                                genericTypes[i] = call.Generics[i];
                            }
                        }
                    }

                    if (match && genericTypes.All(t => t != null) && (function.Varargs || callArgIndex == call.Arguments.Count))
                    {
                        var genericName = $"{function.Name}.{i}.{string.Join('.', genericTypes.Select(t => t.GenericName))}";
                        var name = $"{function.Name}<{string.Join(", ", genericTypes.Select(PrintTypeDefinition))}>";
                        call.Function = genericName;

                        if (_programGraph.Functions.TryGetValue(genericName, out var implementations))
                        {
                            if (implementations.Count > 1)
                            {
                                AddError($"Internal compiler error, multiple implementations of polymorphic function '{name}'", call);
                            }
                            return implementations[0];
                        }

                        var polymorphedFunction = _polymorpher.CreatePolymorphedFunction(function, name, _programGraph.TypeCount++, genericTypes);
                        _programGraph.Functions[genericName] = new List<FunctionAst>{polymorphedFunction};
                        VerifyFunction(polymorphedFunction);

                        return polymorphedFunction;
                    }
                }
            }

            if (functions == null && polymorphicFunctions == null)
            {
                AddError($"Call to undefined function '{call.Function}'", call);
            }
            return null;
        }

        private bool VerifyArgument(IAst argumentAst, TypeDefinition callType, TypeDefinition argumentType, bool externCall = false)
        {
            if (argumentAst is NullAst)
            {
                if (argumentType.Name != "*")
                {
                    return false;
                }
            }
            else if (argumentType.Name != "Type")
            {
                if (externCall && argumentType.Name == "string")
                {
                    if (callType.Name != "string" && callType.Name != "*")
                    {
                        return false;
                    }
                    else if (callType.Name == "*")
                    {
                        var pointerType = callType.Generics.FirstOrDefault();
                        if (pointerType?.Name != "u8" || pointerType.Generics.Any())
                        {
                            return false;
                        }
                    }
                }
                else if (!TypeEquals(argumentType, callType))
                {
                    return false;
                }
            }
            return true;
        }

        private bool VerifyPolymorphicArgument(IAst argumentAst, TypeDefinition callType, TypeDefinition argumentType, TypeDefinition[] genericTypes)
        {
            if (argumentAst is NullAst)
            {
                // Return false if the generic types have been determined,
                // the type cannot be inferred from a null argument if the generics haven't been determined yet
                if (argumentType.Name != "*" || genericTypes.Any(generic => generic == null))
                {
                    return false;
                }
            }
            else if (!VerifyPolymorphicArgument(callType, argumentType, genericTypes))
            {
                return false;
            }
            return true;
        }

        private bool VerifyPolymorphicArgument(TypeDefinition callType, TypeDefinition argumentType, TypeDefinition[] genericTypes)
        {
            if (argumentType.IsGeneric)
            {
                var genericType = genericTypes[argumentType.GenericIndex];
                if (genericType == null)
                {
                    genericTypes[argumentType.GenericIndex] = callType;
                }
                else if (!TypeEquals(genericType, callType))
                {
                    return false;
                }
            }
            else
            {
                if (callType.Name != argumentType.Name || callType.Generics.Count != argumentType.Generics.Count)
                {
                    return false;
                }
                for (var i = 0; i < callType.Generics.Count; i++)
                {
                    if (!VerifyPolymorphicArgument(callType.Generics[i], argumentType.Generics[i], genericTypes))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private TypeDefinition VerifyExpressionType(ExpressionAst expression, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            // 1. Get the type of the initial child
            expression.Type = VerifyExpression(expression.Children[0], currentFunction, scopeIdentifiers);
            if (expression.Type == null) return null;
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
                if ((type == TypeKind.Struct && nextType == TypeKind.Struct) ||
                    (type == TypeKind.String && nextType == TypeKind.String))
                {
                    if (TypeEquals(expression.Type, nextExpressionType, true))
                    {
                        var resultType = VerifyOperatorOverloadType(expression.Type, op, currentFunction, expression.Children[i]);
                        if (resultType != null)
                        {
                            expression.Type = resultType;
                        }
                    }
                    else
                    {
                        AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
                    }
                }
                else
                {
                    switch (op)
                    {
                        // Both need to be bool and returns bool
                        case Operator.And:
                        case Operator.Or:
                            if (type != TypeKind.Boolean || nextType != TypeKind.Boolean)
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
                            if ((type == TypeKind.Enum && nextType == TypeKind.Enum)
                                || (type == TypeKind.Type && nextType == TypeKind.Type))
                            {
                                if ((op != Operator.Equality && op != Operator.NotEqual) || !TypeEquals(expression.Type, nextExpressionType))
                                {
                                    AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
                                }
                            }
                            else if (!(type == TypeKind.Integer || type == TypeKind.Float) &&
                                !(nextType == TypeKind.Integer || nextType == TypeKind.Float))
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
                            if (((type == TypeKind.Pointer && nextType == TypeKind.Integer) ||
                                (type == TypeKind.Integer && nextType == TypeKind.Pointer)) &&
                                (op == Operator.Add || op == Operator.Subtract))
                            {
                                if (nextType == TypeKind.Pointer)
                                {
                                    expression.Type = nextExpressionType;
                                }
                            }
                            else if ((type == TypeKind.Integer || type == TypeKind.Float) &&
                                (nextType == TypeKind.Integer || nextType == TypeKind.Float))
                            {
                                // For integer operations, use the larger size and convert to signed if one type is signed
                                if (type == TypeKind.Integer && nextType == TypeKind.Integer)
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
                                else if (type == TypeKind.Float && nextType == TypeKind.Float)
                                {
                                    if (expression.Type.PrimitiveType.Bytes < nextExpressionType.PrimitiveType.Bytes)
                                    {
                                        expression.Type = nextExpressionType;
                                    }
                                }
                                // For an int lhs and float rhs, convert to the floating point type
                                // Note that float lhs and int rhs are covered since the floating point is already selected
                                else if (nextType == TypeKind.Float)
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
                            if (type == TypeKind.Integer && nextType == TypeKind.Integer)
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
                            else if (!(type == TypeKind.Boolean && nextType == TypeKind.Boolean))
                            {
                                AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
                                if (nextType == TypeKind.Boolean || nextType == TypeKind.Integer)
                                {
                                    expression.Type = nextExpressionType;
                                }
                                else if (!(type == TypeKind.Boolean || type == TypeKind.Integer))
                                {
                                    // If the type can't be determined, default to int
                                    expression.Type = new TypeDefinition {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};
                                }
                            }
                            break;
                        case Operator.ShiftLeft:
                        case Operator.ShiftRight:
                        case Operator.RotateLeft:
                        case Operator.RotateRight:
                            if (type != TypeKind.Integer || nextType != TypeKind.Integer)
                            {
                                AddError($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.Type)}' and '{PrintTypeDefinition(nextExpressionType)}'", expression.Children[i]);
                                if (type != TypeKind.Integer)
                                {
                                    if (nextType == TypeKind.Integer)
                                    {
                                        expression.Type = nextExpressionType;
                                    }
                                    else
                                    {
                                        // If the type can't be determined, default to int
                                        expression.Type = new TypeDefinition {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};
                                    }
                                }
                            }
                            break;
                    }
                }
                expression.ResultingTypes.Add(expression.Type);
            }
            return expression.Type;
        }

        private TypeDefinition VerifyIndexType(IndexAst index, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers)
        {
            if (!scopeIdentifiers.TryGetValue(index.Name, out var identifier))
            {
                AddError($"Variable '{index.Name}' not defined", index);
                return null;
            }
            if (identifier is not DeclarationAst declaration)
            {
                AddError($"Identifier '{index.Name}' is not a variable", index);
                return null;
            }
            return VerifyIndex(index, declaration.Type, currentFunction, scopeIdentifiers, out _);
        }

        private StructAst _stringStruct;

        private TypeDefinition VerifyIndex(IndexAst index, TypeDefinition typeDef, IFunction currentFunction, IDictionary<string, IAst> scopeIdentifiers, out bool overloaded)
        {
            // 1. Verify the variable is a list or the operator overload exists
            overloaded = false;
            var type = VerifyType(typeDef);
            TypeDefinition elementType = null;
            switch (type)
            {
                case TypeKind.Error:
                    break;
                case TypeKind.Struct:
                    index.CallsOverload = true;
                    index.OverloadType = typeDef;
                    overloaded = true;
                    elementType = VerifyOperatorOverloadType(typeDef, Operator.Subscript, currentFunction, index);
                    break;
                case TypeKind.List:
                case TypeKind.Params:
                case TypeKind.Pointer:
                    elementType = typeDef.Generics.FirstOrDefault();
                    if (elementType == null)
                    {
                        AddError("Unable to determine element type of the List", index);
                    }
                    break;
                case TypeKind.String:
                    _stringStruct ??= (StructAst)_programGraph.Types["string"];
                    elementType = _stringStruct.Fields[1].Type.Generics[0];
                    break;
                default:
                    AddError($"Cannot index type '{PrintTypeDefinition(typeDef)}'", index);
                    break;
            }

            // 2. Verify the count expression is an integer
            var indexValue = VerifyExpression(index.Index, currentFunction, scopeIdentifiers);
            var indexType = VerifyType(indexValue);
            if (indexType != TypeKind.Integer && indexType != TypeKind.Type)
            {
                AddError($"Expected index to be type 'int', but got '{PrintTypeDefinition(indexValue)}'", index);
            }

            return elementType;
        }

        private TypeDefinition VerifyOperatorOverloadType(TypeDefinition type, Operator op, IFunction currentFunction, IAst ast)
        {
            if (_programGraph.OperatorOverloads.TryGetValue(type.GenericName, out var overloads) && overloads.TryGetValue(op, out var overload))
            {
                if (!overload.Verified && overload != currentFunction)
                {
                    VerifyOperatorOverload(overload);
                }
                return overload.ReturnType;
            }
            else if (_polymorphicOperatorOverloads.TryGetValue(type.Name, out var polymorphicOverloads) && polymorphicOverloads.TryGetValue(op, out var polymorphicOverload))
            {
                var polymorphedOverload = _polymorpher.CreatePolymorphedOperatorOverload(polymorphicOverload, type.Generics.ToArray());
                if (overloads == null)
                {
                    _programGraph.OperatorOverloads[type.GenericName] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
                }
                overloads[op] = polymorphedOverload;
                VerifyOperatorOverload(polymorphedOverload);
                VerifyType(polymorphicOverload.ReturnType);
                return polymorphedOverload.ReturnType;
            }
            else
            {
                AddError($"Type '{PrintTypeDefinition(type)}' does not contain an overload for operator '{PrintOperator(op)}'", ast);
                return null;
            }
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

        private TypeKind VerifyType(TypeDefinition typeDef, int depth = 0, bool argument = false)
        {
            if (typeDef == null) return TypeKind.Error;
            if (typeDef.TypeKind != null) return typeDef.TypeKind.Value;

            if (typeDef.IsGeneric)
            {
                if (typeDef.Generics.Any())
                {
                    AddError("Generic type cannot have additional generic types", typeDef);
                }
                return TypeKind.Generic;
            }

            if (typeDef.CArray && typeDef.Name != "List")
            {
                AddError("Directive #c_array can only be applied to List", typeDef);
            }

            if (typeDef.Count != null && typeDef.Name != "List")
            {
                AddError($"Type '{PrintTypeDefinition(typeDef)}' cannot have a count", typeDef);
                return TypeKind.Error;
            }

            var hasGenerics = typeDef.Generics.Any();

            switch (typeDef.PrimitiveType)
            {
                case IntegerType:
                    if (hasGenerics)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                        return TypeKind.Error;
                    }
                    typeDef.TypeKind = TypeKind.Integer;
                    return TypeKind.Integer;
                case FloatType:
                    if (hasGenerics)
                    {
                        AddError($"Type '{typeDef.Name}' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                        return TypeKind.Error;
                    }
                    typeDef.TypeKind = TypeKind.Float;
                    return TypeKind.Float;
            }

            switch (typeDef.Name)
            {
                case "bool":
                    if (hasGenerics)
                    {
                        AddError("Type 'bool' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.Boolean;
                    }
                    break;
                case "string":
                    if (hasGenerics)
                    {
                        AddError("Type 'string' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.String;
                    }
                    break;
                case "List":
                    if (typeDef.Generics.Count != 1)
                    {
                        AddError($"Type 'List' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (!VerifyList(typeDef, depth, argument, out var hasGenericTypes))
                    {
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = hasGenericTypes ? TypeKind.Generic : TypeKind.List;
                    }
                    break;
                case "void":
                    if (hasGenerics)
                    {
                        AddError("Type 'void' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.Void;
                    }
                    break;
                case "*":
                    if (typeDef.Generics.Count != 1)
                    {
                        AddError($"pointer type should have reference to 1 type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (_programGraph.Types.ContainsKey(typeDef.GenericName))
                    {
                        typeDef.TypeKind = TypeKind.Pointer;
                    }
                    else
                    {
                        var pointerType = VerifyType(typeDef.Generics[0], depth + 1);
                        if (pointerType == TypeKind.Error)
                        {
                            typeDef.TypeKind = TypeKind.Error;
                        }
                        else if (pointerType == TypeKind.Generic)
                        {
                            typeDef.TypeKind = TypeKind.Generic;
                        }
                        else
                        {
                            var pointer = new PrimitiveAst {Name = PrintTypeDefinition(typeDef), TypeIndex = _programGraph.TypeCount++, TypeKind = TypeKind.Pointer, Size = 8};
                            _programGraph.Types.Add(typeDef.GenericName, pointer);
                            typeDef.TypeKind = TypeKind.Pointer;
                        }
                    }
                    break;
                case "...":
                    if (hasGenerics)
                    {
                        AddError("Type 'varargs' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                        return TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.VarArgs;
                    }
                    break;
                case "Params":
                    if (!argument)
                    {
                        AddError($"Params can only be used in function arguments", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (depth != 0)
                    {
                        AddError($"Params can only be declared as a top level type, such as 'Params<int>'", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (typeDef.Generics.Count != 1)
                    {
                        AddError($"Type 'Params' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = VerifyList(typeDef, depth, argument, out _) ? TypeKind.Params : TypeKind.Error;
                    }
                    break;
                case "Type":
                    if (hasGenerics)
                    {
                        AddError("Type 'Type' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.Type;
                    }
                    break;
                default:
                    if (hasGenerics)
                    {
                        var genericName = typeDef.GenericName;
                        if (_programGraph.Types.ContainsKey(genericName))
                        {
                            typeDef.TypeKind = TypeKind.Struct;
                        }
                        else
                        {
                            var generics = typeDef.Generics.ToArray();
                            var error = false;
                            var hasGenericTypes = false;
                            foreach (var generic in generics)
                            {
                                var genericType = VerifyType(generic, depth + 1);
                                if (genericType == TypeKind.Error)
                                {
                                    error = true;
                                }
                                else if (genericType == TypeKind.Generic)
                                {
                                    hasGenericTypes = true;
                                }
                            }
                            if (!_polymorphicStructs.TryGetValue(typeDef.Name, out var structDef))
                            {
                                AddError($"No polymorphic structs of type '{typeDef.Name}'", typeDef);
                                typeDef.TypeKind = TypeKind.Error;
                            }
                            else if (structDef.Generics.Count != typeDef.Generics.Count)
                            {
                                AddError($"Expected type '{typeDef.Name}' to have {structDef.Generics.Count} generic(s), but got {typeDef.Generics.Count}", typeDef);
                                typeDef.TypeKind = TypeKind.Error;
                            }
                            else if (error)
                            {
                                typeDef.TypeKind = TypeKind.Error;
                            }
                            else if (hasGenericTypes)
                            {
                                typeDef.TypeKind = TypeKind.Generic;
                            }
                            else
                            {
                                var polyStruct = _polymorpher.CreatePolymorphedStruct(structDef, PrintTypeDefinition(typeDef), TypeKind.Struct, _programGraph.TypeCount++, generics);
                                _programGraph.Types.Add(genericName, polyStruct);
                                VerifyStruct(polyStruct);
                                typeDef.TypeKind = TypeKind.Struct;
                            }
                        }
                    }
                    else if (_programGraph.Types.TryGetValue(typeDef.Name, out var type))
                    {
                        switch (type)
                        {
                            case StructAst:
                                typeDef.TypeKind = TypeKind.Struct;
                                break;
                            case EnumAst enumAst:
                                var primitive = enumAst.BaseType.PrimitiveType;
                                typeDef.PrimitiveType ??= new EnumType {Bytes = primitive.Bytes, Signed = primitive.Signed};
                                typeDef.TypeKind = TypeKind.Enum;
                                break;
                            default:
                                typeDef.TypeKind = TypeKind.Error;
                                break;
                        }
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    break;
            }
            return typeDef.TypeKind.Value;
        }

        private bool VerifyList(TypeDefinition typeDef, int depth, bool argument, out bool hasGenerics)
        {
            hasGenerics = false;
            var listType = typeDef.Generics[0];
            var genericType = VerifyType(listType, depth + 1, argument);
            if (genericType == TypeKind.Error)
            {
                return false;
            }
            else if (genericType == TypeKind.Generic)
            {
                hasGenerics = true;
                return true;
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

            var listStruct = _polymorpher.CreatePolymorphedStruct(structDef, $"List<{PrintTypeDefinition(listType)}>", TypeKind.List, _programGraph.TypeCount++, listType);
            _programGraph.Types.Add(genericName, listStruct);
            VerifyStruct(listStruct);
            return true;
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
                Operator.ShiftLeft => "<<",
                Operator.ShiftRight => ">>",
                Operator.RotateLeft => "<<<",
                Operator.RotateRight => ">>>",
                Operator.Subscript => "[]",
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
