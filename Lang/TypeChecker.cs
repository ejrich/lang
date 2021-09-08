using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lang
{
    public interface ITypeChecker
    {
        void CheckTypes(List<IAst> asts);
    }

    public class TypeChecker : ITypeChecker
    {
        private readonly IPolymorpher _polymorpher;
        private readonly IProgramIRBuilder _irBuilder;
        private readonly IProgramRunner _runner;

        private readonly Dictionary<string, Dictionary<Operator, OperatorOverloadAst>> _operatorOverloads = new();
        private readonly Dictionary<string, StructAst> _polymorphicStructs = new();
        private readonly Dictionary<string, List<FunctionAst>> _polymorphicFunctions = new();
        private readonly Dictionary<string, Dictionary<Operator, OperatorOverloadAst>> _polymorphicOperatorOverloads = new();
        private readonly ScopeAst _globalScope = new();

        private IType _boolType;
        private IType _u8Type;
        private PrimitiveAst _s32Type;
        private IType _float64Type;

        public TypeChecker(IPolymorpher polymorpher, IProgramIRBuilder irBuilder, IProgramRunner runner)
        {
            _polymorpher = polymorpher;
            _irBuilder = irBuilder;
            _runner = runner;
        }

        public void CheckTypes(List<IAst> asts)
        {
            var mainDefined = false;
            bool verifyAdditional;

            // Add primitive types to global identifiers
            AddPrimitive("void", TypeKind.Void, 1);
            _boolType = AddPrimitive("bool", TypeKind.Boolean, 1);
            AddPrimitive("s8", TypeKind.Integer, 1, true);
            _u8Type = AddPrimitive("u8", TypeKind.Integer, 1);
            AddPrimitive("s16", TypeKind.Integer, 2, true);
            AddPrimitive("u16", TypeKind.Integer, 2);
            _s32Type = AddPrimitive("s32", TypeKind.Integer, 4, true);
            AddPrimitive("u32", TypeKind.Integer, 4);
            AddPrimitive("s64", TypeKind.Integer, 8, true);
            AddPrimitive("u64", TypeKind.Integer, 8);
            AddPrimitive("float", TypeKind.Float, 4, true);
            _float64Type = AddPrimitive("float64", TypeKind.Float, 8, true);

            _irBuilder.Init();

            var functionNames = new HashSet<string>();
            do
            {
                // 1. Verify enum and struct definitions
                for (var i = 0; i < asts.Count; i++)
                {
                    switch (asts[i])
                    {
                        case EnumAst enumAst:
                            VerifyEnum(enumAst);
                            asts.RemoveAt(i--);
                            break;
                        case StructAst structAst:
                            if (structAst.Generics.Any())
                            {
                                if (_polymorphicStructs.ContainsKey(structAst.Name))
                                {
                                    ErrorReporter.Report($"Multiple definitions of polymorphic struct '{structAst.Name}'", structAst);
                                }
                                _polymorphicStructs[structAst.Name] = structAst;
                                asts.RemoveAt(i--);
                            }
                            else
                            {
                                if (!TypeTable.Add(structAst.Name, structAst))
                                {
                                    ErrorReporter.Report($"Multiple definitions of struct '{structAst.Name}'", structAst);
                                }

                                if (structAst.Name == "string")
                                {
                                    structAst.TypeKind = TypeKind.String;
                                    VerifyStruct(structAst);
                                    asts.RemoveAt(i--);
                                }
                                else
                                {
                                    structAst.TypeKind = TypeKind.Struct;
                                }
                            }

                            _globalScope.Identifiers[structAst.Name] = structAst;
                            break;
                    }
                }

                // 2. Verify global variables and function return types/arguments
                for (var i = 0; i < asts.Count; i++)
                {
                    switch (asts[i])
                    {
                        case DeclarationAst globalVariable:
                            VerifyGlobalVariable(globalVariable);
                            asts.RemoveAt(i--);
                            break;
                    }
                }

                // 3. Verify function return types/arguments
                for (var i = 0; i < asts.Count; i++)
                {
                    switch (asts[i])
                    {
                        case FunctionAst function:
                            var main = function.Name == "main";
                            if (main)
                            {
                                if (mainDefined)
                                {
                                    ErrorReporter.Report("Only one main function can be defined", function);
                                }

                                mainDefined = true;
                            }

                            VerifyFunctionDefinition(function, functionNames, main);
                            asts.RemoveAt(i--);
                            break;
                        case OperatorOverloadAst overload:
                            VerifyOperatorOverloadDefinition(overload);
                            asts.RemoveAt(i--);
                            break;
                    }
                }

                // 4. Verify struct bodies
                for (var i = 0; i < asts.Count; i++)
                {
                    switch (asts[i])
                    {
                        case StructAst structAst:
                            if (!structAst.Verified)
                            {
                                VerifyStruct(structAst);
                            }
                            asts.RemoveAt(i--);
                            break;
                    }
                }

                // 5. Verify and run top-level static ifs
                verifyAdditional = false;
                var additionalAsts = new List<IAst>();
                for (int i = 0; i < asts.Count; i++)
                {
                    switch (asts[i])
                    {
                        case CompilerDirectiveAst directive:
                            switch (directive.Type)
                            {
                                case DirectiveType.If:
                                    var conditional = directive.Value as ConditionalAst;
                                    if (VerifyCondition(conditional.Condition, null, _globalScope))
                                    {
                                        var condition = _irBuilder.CreateRunnableCondition(conditional.Condition, _globalScope);
                                        _runner.Init();

                                        if (_runner.ExecuteCondition(condition, conditional.Condition))
                                        {
                                            additionalAsts.AddRange(conditional.IfBlock.Children);
                                        }
                                        else if (conditional.ElseBlock != null)
                                        {
                                            additionalAsts.AddRange(conditional.ElseBlock.Children);
                                        }
                                    }
                                    asts.RemoveAt(i--);
                                    break;
                                case DirectiveType.Assert:
                                    if (VerifyCondition(directive.Value, null, _globalScope))
                                    {
                                        var condition = _irBuilder.CreateRunnableCondition(directive.Value, _globalScope);
                                        _runner.Init();

                                        if (!_runner.ExecuteCondition(condition, directive.Value))
                                        {
                                            ErrorReporter.Report("Assertion failed", directive.Value);
                                        }
                                    }
                                    asts.RemoveAt(i--);
                                    break;
                            }
                            break;
                    }
                }
                if (additionalAsts.Any())
                {
                    asts.AddRange(additionalAsts);
                    verifyAdditional = true;
                }
            } while (verifyAdditional);

            // 6. Verify operator overload bodies
            foreach (var overloads in _operatorOverloads.Values)
            {
                foreach (var overload in overloads.Values)
                {
                    if (overload.Flags.HasFlag(FunctionFlags.Verified)) continue;
                    VerifyOperatorOverload(overload);
                }
            }

            // 7. Verify function bodies
            foreach (var name in functionNames)
            {
                var functions = TypeTable.Functions[name];
                foreach (var function in functions)
                {
                    if (function.Flags.HasFlag(FunctionFlags.Verified)) continue;
                    VerifyFunction(function);
                }
            }

            // 8. Execute any other compiler directives
            foreach (var ast in asts)
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
                ErrorReporter.Report("'main' function of the program is not defined");
            }
        }

        private PrimitiveAst AddPrimitive(string name, TypeKind typeKind, uint size = 0, bool signed = false)
        {
            var primitiveAst = new PrimitiveAst {Name = name, TypeKind = typeKind, Size = size, Signed = signed};
            _globalScope.Identifiers.Add(name, primitiveAst);
            TypeTable.Add(name, primitiveAst);
            TypeTable.CreateTypeInfo(primitiveAst);
            return primitiveAst;
        }

        private void VerifyEnum(EnumAst enumAst)
        {
            // 1. Verify enum has not already been defined
            if (!TypeTable.Add(enumAst.Name, enumAst))
            {
                ErrorReporter.Report($"Multiple definitions of enum '{enumAst.Name}'", enumAst);
            }
            _globalScope.Identifiers.Add(enumAst.Name, enumAst);

            if (enumAst.BaseTypeDefinition == null)
            {
                // enumAst.BaseTypeDefinition = _s32Type; // TODO Remove
                enumAst.BaseType = _s32Type;
            }
            else
            {
                var baseType = VerifyType(enumAst.BaseTypeDefinition);
                if (baseType != TypeKind.Integer && baseType != TypeKind.Error)
                {
                    ErrorReporter.Report($"Base type of enum must be an integer, but got '{PrintTypeDefinition(enumAst.BaseTypeDefinition)}'", enumAst.BaseTypeDefinition);
                    // enumAst.BaseTypeDefinition.PrimitiveType = new IntegerType {Bytes = 4, Signed = true};
                    enumAst.BaseType = _s32Type;
                }
                else
                {
                    enumAst.BaseType = (PrimitiveAst)TypeTable.GetType(enumAst.BaseTypeDefinition);
                    enumAst.Size = enumAst.BaseType.Size;
                }
            }

            // 2. Verify enums don't have repeated values
            var valueNames = new HashSet<string>();
            var values = new HashSet<int>();

            var baseType = enumAst.BaseType;
            var lowestAllowedValue = baseType.Signed ? -Math.Pow(2, 4 * baseType.Size - 1) : 0;
            var largestAllowedValue = baseType.Signed ? Math.Pow(2, 4 * baseType.Size - 1) - 1 : Math.Pow(2, 4 * baseType.Size) - 1;

            var largestValue = -1;
            foreach (var value in enumAst.Values)
            {
                // 2a. Check if the value has been previously defined
                if (!valueNames.Add(value.Name))
                {
                    ErrorReporter.Report($"Enum '{enumAst.Name}' already contains value '{value.Name}'", value);
                }

                // 2b. Check if the value has been previously used
                if (value.Defined)
                {
                    if (!values.Add(value.Value))
                    {
                        ErrorReporter.Report($"Value '{value.Value}' previously defined in enum '{enumAst.Name}'", value);
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
                    ErrorReporter.Report($"Enum value '{enumAst.Name}.{value.Name}' value '{value.Value}' is out of range", value);
                }
            }

            TypeTable.CreateTypeInfo(enumAst);
        }

        private void VerifyStruct(StructAst structAst)
        {
            // 1. Verify struct fields have valid types
            var fieldNames = new HashSet<string>();
            structAst.Verifying = true;
            foreach (var structField in structAst.Fields)
            {
                // 1a. Check if the field has been previously defined
                if (!fieldNames.Add(structField.Name))
                {
                    ErrorReporter.Report($"Struct '{structAst.Name}' already contains field '{structField.Name}'", structField);
                }

                IType fieldType = null;

                if (structField.TypeDefinition != null)
                {
                    var type = VerifyType(structField.TypeDefinition);

                    if (type == TypeKind.Error)
                    {
                        ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(structField.TypeDefinition)}' in struct field '{structAst.Name}.{structField.Name}'", structField.TypeDefinition);
                    }
                    else if (type == TypeKind.Void)
                    {
                        ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be assigned type 'void'", structField.TypeDefinition);
                    }
                    else
                    {
                        fieldType = TypeTable.GetType(structField.TypeDefinition);
                        if (type == TypeKind.Array || type == TypeKind.CArray)
                        {
                            var elementType = structField.TypeDefinition.Generics[0];
                            structField.ArrayElementType = TypeTable.GetType(elementType);
                        }
                    }

                    if (structField.Value != null)
                    {
                        if (structField.Value is NullAst nullAst)
                        {
                            if (type != TypeKind.Pointer)
                            {
                                ErrorReporter.Report("Cannot assign null to non-pointer type", structField.Value);
                            }

                            nullAst.TargetTypeDefinition = structField.TypeDefinition;
                            nullAst.TargetType = structField.Type;
                        }
                        else
                        {
                            var valueType = VerifyConstantExpression(structField.Value, null, _globalScope, out var isConstant, out _);

                            // Verify the type is correct
                            if (valueType != null)
                            {
                                if (!TypeEquals(structField.TypeDefinition, valueType))
                                {
                                    ErrorReporter.Report($"Expected struct field value to be type '{PrintTypeDefinition(structField.TypeDefinition)}', but got '{valueType.Name}'", structField.Value);
                                }
                                else if (!isConstant)
                                {
                                    ErrorReporter.Report("Default values in structs must be constant", structField.Value);
                                }
                                else if (structField.TypeDefinition.PrimitiveType != null && structField.Value is ConstantAst constant)
                                {
                                    VerifyConstant(constant, structField.TypeDefinition);
                                }
                            }
                        }
                    }
                    else if (structField.Assignments != null)
                    {
                        if (type != TypeKind.Struct && type != TypeKind.String)
                        {
                            ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(structField.TypeDefinition)}'", structField.TypeDefinition);
                        }
                        // Catch circular references
                        else if (fieldType != null && fieldType != structAst)
                        {
                            var structDef = fieldType as StructAst;
                            if (!structDef.Verified)
                            {
                                VerifyStruct(structDef);
                            }
                            var fields = structDef.Fields.ToDictionary(_ => _.Name);
                            foreach (var (name, assignment) in structField.Assignments)
                            {
                                if (!fields.TryGetValue(name, out var field))
                                {
                                    ErrorReporter.Report($"Field '{name}' not in struct '{PrintTypeDefinition(structField.TypeDefinition)}'", assignment.Reference);
                                }

                                if (assignment.Operator != Operator.None)
                                {
                                    ErrorReporter.Report("Cannot have operator assignments in object initializers", assignment.Reference);
                                }

                                var valueType = VerifyConstantExpression(assignment.Value, null, _globalScope, out var isConstant, out _);
                                if (valueType != null && field != null)
                                {
                                    if (!TypeEquals(field.TypeDefinition, valueType))
                                    {
                                        ErrorReporter.Report($"Expected field value to be type '{PrintTypeDefinition(field.TypeDefinition)}', but got '{valueType.Name}'", assignment.Value);
                                    }
                                    else if (!isConstant)
                                    {
                                        ErrorReporter.Report("Default values in structs should be constant", assignment.Value);
                                    }
                                    else if (field.TypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                                    {
                                        VerifyConstant(constant, field.TypeDefinition);
                                    }
                                }
                            }
                        }
                    }
                    else if (structField.ArrayValues != null)
                    {
                        if (type != TypeKind.Error && type != TypeKind.Array && type != TypeKind.CArray)
                        {
                            ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(structField.TypeDefinition)}'", structField.TypeDefinition);
                        }
                        else
                        {
                            structField.TypeDefinition.ConstCount = (uint)structField.ArrayValues.Count;
                            var elementType = structField.TypeDefinition.Generics[0];
                            foreach (var value in structField.ArrayValues)
                            {
                                var valueType = VerifyConstantExpression(value, null, _globalScope, out var isConstant, out _);
                                if (valueType != null)
                                {
                                    if (!TypeEquals(elementType, valueType))
                                    {
                                        ErrorReporter.Report($"Expected array value to be type '{PrintTypeDefinition(elementType)}', but got '{valueType.Name}'", value);
                                    }
                                    else if (!isConstant)
                                    {
                                        ErrorReporter.Report("Default values in structs array initializers should be constant", value);
                                    }
                                    else if (elementType.PrimitiveType != null && value is ConstantAst constant)
                                    {
                                        VerifyConstant(constant, elementType);
                                    }
                                }
                            }
                        }
                    }

                    // Check type count
                    if (type == TypeKind.CArray && structField.TypeDefinition.Count == null && structField.TypeDefinition.ConstCount == null)
                    {
                        ErrorReporter.Report($"C array of field '{structAst.Name}.{structField.Name}' must be initialized with a constant size", structField.TypeDefinition);
                    }
                    else if (structField.TypeDefinition.Count != null)
                    {
                        // Verify the count is a constant
                        var countType = VerifyConstantExpression(structField.TypeDefinition.Count, null, _globalScope, out var isConstant, out var count);

                        if (countType != null)
                        {
                            if (!isConstant || countType.TypeKind != TypeKind.Integer)
                            {
                                ErrorReporter.Report($"Expected size of '{structAst.Name}.{structField.Name}' to be a constant integer", structField.TypeDefinition.Count);
                            }
                            else if (count < 0)
                            {
                                ErrorReporter.Report($"Expected size of '{structAst.Name}.{structField.Name}' to be a positive integer", structField.TypeDefinition.Count);
                            }
                            else
                            {
                                structField.TypeDefinition.ConstCount = (uint)count;
                            }
                        }
                    }
                }
                else
                {
                    if (structField.Value != null)
                    {
                        if (structField.Value is NullAst nullAst)
                        {
                            ErrorReporter.Report("Cannot assign null value without declaring a type", structField.Value);
                        }
                        else
                        {
                            var valueType = VerifyConstantExpression(structField.Value, null, _globalScope, out var isConstant, out _);

                            if (!isConstant)
                            {
                                ErrorReporter.Report("Default values in structs must be constant", structField.Value);
                            }
                            if (valueType.TypeKind == TypeKind.Void)
                            {
                                ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be assigned type 'void'", structField.Value);
                            }
                            structField.Type = fieldType = valueType;
                            // TypeTable.Types.TryGetValue(valueType?.GenericName, out fieldType);
                        }
                    }
                    else if (structField.Assignments != null)
                    {
                        ErrorReporter.Report("Struct literals are not yet supported", structField);
                    }
                    else if (structField.ArrayValues != null)
                    {
                        ErrorReporter.Report($"Declaration for struct field '{structAst.Name}.{structField.Name}' with array initializer must have the type declared", structField);
                    }
                }

                // Check for circular dependencies
                if (structAst.Name == structField.TypeDefinition.Name)
                {
                    ErrorReporter.Report($"Struct '{structAst.Name}' contains circular reference in field '{structField.Name}'", structField);
                }

                // Set the size and offset
                if (fieldType != null)
                {
                    if (fieldType is StructAst fieldStruct)
                    {
                        if (!fieldStruct.Verified && fieldStruct != structAst)
                        {
                            VerifyStruct(fieldStruct);
                        }
                    }
                    structField.Type = fieldType;
                    structField.Offset = structAst.Size;
                    structField.Size = fieldType.TypeKind == TypeKind.CArray ? fieldType.Size * structField.TypeDefinition.ConstCount.Value : fieldType.Size;
                    structAst.Size += fieldType.Size;
                }
            }

            TypeTable.CreateTypeInfo(structAst);
            structAst.Verified = true;
        }

        private void VerifyFunctionDefinition(FunctionAst function, HashSet<string> functionNames, bool main)
        {
            // 1. Verify the return type of the function is valid
            var returnType = VerifyType(function.ReturnTypeDefinition);
            if (returnType == TypeKind.Error)
            {
                ErrorReporter.Report($"Return type '{function.ReturnTypeDefinition.Name}' of function '{function.Name}' is not defined", function.ReturnTypeDefinition);
            }
            else if (returnType == TypeKind.CArray && function.ReturnTypeDefinition.Count == null)
            {
                ErrorReporter.Report($"C array for function '{function.Name}' must have a constant size", function.ReturnTypeDefinition);
            }
            else if (function.ReturnTypeDefinition.Count != null)
            {
                var countType = VerifyConstantExpression(function.ReturnTypeDefinition.Count, null, _globalScope, out var isConstant, out var count);

                if (countType != null)
                {
                    if (isConstant)
                    {
                        if (count < 0)
                        {
                            ErrorReporter.Report($"Expected size of return type of function '{function.Name}' to be a positive integer", function.ReturnTypeDefinition.Count);
                        }
                        else
                        {
                            function.ReturnTypeDefinition.ConstCount = (uint)count;
                        }
                    }
                    else
                    {
                        ErrorReporter.Report($"Return type of function '{function.Name}' should have constant size", function.ReturnTypeDefinition.Count);
                    }
                }
            }
            function.ReturnType = TypeTable.GetType(function.ReturnTypeDefinition);

            // 2. Verify the argument types
            var argumentNames = new HashSet<string>();
            foreach (var argument in function.Arguments)
            {
                // 3a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    ErrorReporter.Report($"Function '{function.Name}' already contains argument '{argument.Name}'", argument);
                }

                // 3b. Check for errored or undefined field types
                var type = VerifyType(argument.TypeDefinition, argument: true);
                // TODO Make this better, roll into VerifyType
                argument.Type = TypeTable.GetType(argument.TypeDefinition);

                switch (type)
                {
                    case TypeKind.VarArgs:
                        if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
                        {
                            ErrorReporter.Report($"Function '{function.Name}' cannot have multiple varargs", argument.TypeDefinition);
                        }
                        function.Flags |= FunctionFlags.Varargs;
                        function.VarargsCallTypes = new List<IType[]>();
                        break;
                    case TypeKind.Params:
                        if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
                        {
                            ErrorReporter.Report($"Function '{function.Name}' cannot have multiple varargs", argument.TypeDefinition);
                        }
                        function.Flags |= FunctionFlags.Params;
                        function.ParamsElementType = TypeTable.GetType(argument.TypeDefinition.Generics[0]);
                        break;
                    case TypeKind.Error:
                        ErrorReporter.Report($"Type '{PrintTypeDefinition(argument.TypeDefinition)}' of argument '{argument.Name}' in function '{function.Name}' is not defined", argument.TypeDefinition);
                        break;
                    default:
                        if (function.Flags.HasFlag(FunctionFlags.Varargs))
                        {
                            ErrorReporter.Report($"Cannot declare argument '{argument.Name}' following varargs", argument);
                        }
                        else if (function.Flags.HasFlag(FunctionFlags.Params))
                        {
                            ErrorReporter.Report($"Cannot declare argument '{argument.Name}' following params", argument);
                        }
                        else if (function.Flags.HasFlag(FunctionFlags.Extern) && type == TypeKind.String)
                        {
                            argument.Type = TypeTable.Types["*.u8"];
                        }
                        break;
                }

                // 3c. Check for default arguments
                if (argument.Value != null)
                {
                    var defaultType = VerifyConstantExpression(argument.Value, null, _globalScope, out var isConstant, out _);

                    if (argument.HasGenerics)
                    {
                        ErrorReporter.Report($"Argument '{argument.Name}' in function '{function.Name}' cannot have default value if the argument has a generic type", argument.Value);
                    }
                    else if (defaultType != null)
                    {
                        if (!isConstant)
                        {
                            ErrorReporter.Report($"Expected default value of argument '{argument.Name}' in function '{function.Name}' to be a constant value", argument.Value);

                        }
                        else if (type != TypeKind.Error && !TypeEquals(argument.TypeDefinition, defaultType))
                        {
                            ErrorReporter.Report($"Type of argument '{argument.Name}' in function '{function.Name}' is '{PrintTypeDefinition(argument.TypeDefinition)}', but default value is type '{PrintTypeDefinition(defaultType)}'", argument.Value);
                        }
                        else if (argument.TypeDefinition.PrimitiveType != null && argument.Value is ConstantAst constant)
                        {
                            VerifyConstant(constant, argument.TypeDefinition);
                        }
                    }
                    else if (isConstant && type != TypeKind.Pointer && type != TypeKind.Error)
                    {
                        ErrorReporter.Report($"Type of argument '{argument.Name}' in function '{function.Name}' is '{PrintTypeDefinition(argument.TypeDefinition)}', but default value is 'null'", argument.Value);
                    }
                }
            }

            // 3. Verify main function return type and arguments
            if (main)
            {
                if (returnType != TypeKind.Void && returnType != TypeKind.Integer)
                {
                    ErrorReporter.Report("The main function should return type 'int' or 'void'", function);
                }

                var argument = function.Arguments.FirstOrDefault();
                if (argument != null && !(function.Arguments.Count == 1 && argument.TypeDefinition.TypeKind == TypeKind.Array && argument.TypeDefinition.Generics.FirstOrDefault()?.TypeKind == TypeKind.String))
                {
                    ErrorReporter.Report("The main function should either have 0 arguments or 'Array<string>' argument", function);
                }

                if (function.Generics.Any())
                {
                    ErrorReporter.Report("The main function cannot have generics", function);
                }
            }

            // 4. Load the function into the dictionary
            if (function.Generics.Any())
            {
                if (!function.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) && function.Arguments.All(arg => !arg.HasGenerics))
                {
                    ErrorReporter.Report($"Function '{function.Name}' has generic(s), but the generic(s) are not used in the argument(s) or the return type", function);
                }

                if (!_polymorphicFunctions.TryGetValue(function.Name, out var functions))
                {
                    _polymorphicFunctions[function.Name] = functions = new List<FunctionAst>();
                }
                if (functions.Any() && OverloadExistsForFunction(function, functions))
                {
                    ErrorReporter.Report($"Function '{function.Name}' has multiple overloads with arguments ({string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.TypeDefinition)))})", function);
                }
                functions.Add(function);
            }
            else
            {
                functionNames.Add(function.Name);
                var _functions = TypeTable.AddFunction(function.Name, function);
                if (_functions.Count > 1)
                {
                    if (function.Flags.HasFlag(FunctionFlags.Extern))
                    {
                        ErrorReporter.Report($"Multiple definitions of extern function '{function.Name}'", function);
                    }
                    else if (OverloadExistsForFunction(function, _functions, false))
                    {
                        ErrorReporter.Report($"Function '{function.Name}' has multiple overloads with arguments ({string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.TypeDefinition)))})", function);
                    }
                }
            }
        }

        private bool OverloadExistsForFunction(IFunction currentFunction, List<FunctionAst> existingFunctions, bool checkAll = true)
        {
            var functionCount = checkAll ? existingFunctions.Count : existingFunctions.Count - 1;
            for (var function = 0; function < functionCount; function++)
            {
                var existingFunction = existingFunctions[function];
                if (currentFunction.Arguments.Count == existingFunction.Arguments.Count)
                {
                    var match = true;
                    for (var i = 0; i < currentFunction.Arguments.Count; i++)
                    {
                        if (!TypeEquals(currentFunction.Arguments[i].TypeDefinition, existingFunction.Arguments[i].TypeDefinition, true))
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
                        ErrorReporter.Report($"Expected type '{overload.Type.Name}' to have {structDef.Generics.Count} generic(s), but got {overload.Generics.Count}", overload.Type);
                    }
                }
                else
                {
                    ErrorReporter.Report($"No polymorphic structs of type '{overload.Type.Name}'", overload.Type);
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
                        ErrorReporter.Report($"Cannot overload operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}'", overload.Type);
                        break;
                }
            }

            // 2. Verify the argument types
            if (overload.Arguments.Count != 2)
            {
                ErrorReporter.Report($"Overload of operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}' should contain exactly 2 arguments to represent the l-value and r-value of the expression", overload);
            }
            var argumentNames = new HashSet<string>();
            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                // 2a. Check if the argument has been previously defined
                if (!argumentNames.Add(argument.Name))
                {
                    ErrorReporter.Report($"Overload of operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}' already contains argument '{argument.Name}'", argument);
                }

                // 2b. Check the argument is the same type as the overload type
                if (overload.Operator == Operator.Subscript && i == 1)
                {
                    if (argument.TypeDefinition.PrimitiveType is not IntegerType)
                    {
                        ErrorReporter.Report($"Expected second argument of ");
                        ErrorReporter.Report($"Expected second argument of overload of operator '{PrintOperator(overload.Operator)}' to be an integer, but got '{PrintTypeDefinition(argument.TypeDefinition)}'", argument.TypeDefinition);
                    }
                }
                else if (!TypeEquals(overload.Type, argument.TypeDefinition, true))
                {
                    ErrorReporter.Report($"Expected overload of operator '{PrintOperator(overload.Operator)}' argument type to be '{PrintTypeDefinition(overload.Type)}', but got '{PrintTypeDefinition(argument.TypeDefinition)}'", argument.TypeDefinition);
                }

                // TODO Make this better, roll into VerifyType
                argument.Type = TypeTable.GetType(argument.TypeDefinition);
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
                    ErrorReporter.Report($"Multiple definitions of overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", overload);
                }
                overloads[overload.Operator] = overload;
            }
            else
            {
                if (!_operatorOverloads.TryGetValue(overload.Type.GenericName, out var overloads))
                {
                    _operatorOverloads[overload.Type.GenericName] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
                }
                if (overloads.ContainsKey(overload.Operator))
                {
                    ErrorReporter.Report($"Multiple definitions of overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", overload);
                }
                overloads[overload.Operator] = overload;
            }
        }

        private bool GetScopeIdentifier(ScopeAst scope, string name, out IAst ast)
        {
            do {
                if (scope.Identifiers.TryGetValue(name, out ast))
                {
                    return true;
                }
                scope = scope.Parent;
            } while (scope != null);
            return false;
        }

        private void VerifyFunction(FunctionAst function)
        {
            // 1. Initialize local variables
            foreach (var argument in function.Arguments)
            {
                // Arguments with the same name as a global variable will be used instead of the global
                if (GetScopeIdentifier(_globalScope, argument.Name, out var identifier))
                {
                    if (identifier is not DeclarationAst)
                    {
                        ErrorReporter.Report($"Argument '{argument.Name}' already exists as a type", argument);
                    }
                }
                if (function.Body != null)
                {
                    function.Body.Identifiers[argument.Name] = argument;
                }
            }
            var returnType = VerifyType(function.ReturnTypeDefinition);

            // 2. For extern functions, simply verify there is no body and return
            if (function.Flags.HasFlag(FunctionFlags.Extern))
            {
                if (function.Body != null)
                {
                    ErrorReporter.Report("Extern function cannot have a body", function);
                }
                else if (!function.Flags.HasFlag(FunctionFlags.Varargs))
                {
                    _runner.InitExternFunction(function);
                }
            }
            else if (!function.Flags.HasFlag(FunctionFlags.Compiler))
            {
                // 3. Resolve the compiler directives in the function
                if (function.Flags.HasFlag(FunctionFlags.HasDirectives))
                {
                    ResolveCompilerDirectives(function.Body.Children, function);
                }

                // 4. Loop through function body and verify all ASTs
                var returned = VerifyScope(function.Body, function, _globalScope, false);

                // 5. Verify the main function doesn't call the compiler
                if (function.Name == "main" && function.Flags.HasFlag(FunctionFlags.CallsCompiler))
                {
                    ErrorReporter.Report("The main function cannot call the compiler", function);
                }

                // 6. Verify the function returns on all paths
                if (!returned)
                {
                    if (returnType != TypeKind.Void)
                    {
                        ErrorReporter.Report($"Function '{function.Name}' does not return type '{PrintTypeDefinition(function.ReturnTypeDefinition)}' on all paths", function);
                    }
                    else
                    {
                        function.Flags |= FunctionFlags.ReturnVoidAtEnd;
                    }
                }
            }
            function.Flags |= FunctionFlags.Verified;

            if (!ErrorReporter.Errors.Any())
            {
                _irBuilder.AddFunction(function);
            }
        }

        private void VerifyOperatorOverload(OperatorOverloadAst overload)
        {
            // 1. Initialize local variables
            foreach (var argument in overload.Arguments)
            {
                // Arguments with the same name as a global variable will be used instead of the global
                if (GetScopeIdentifier(_globalScope, argument.Name, out var identifier))
                {
                    if (identifier is not DeclarationAst)
                    {
                        ErrorReporter.Report($"Argument '{argument.Name}' already exists as a type", argument);
                    }
                }
                if (overload.Body != null)
                {
                    overload.Body.Identifiers[argument.Name] = argument;
                }
            }
            var returnType = VerifyType(overload.ReturnTypeDefinition);
            overload.ReturnType = TypeTable.GetType(overload.ReturnTypeDefinition);

            // 2. Resolve the compiler directives in the body
            if (overload.Flags.HasFlag(FunctionFlags.HasDirectives))
            {
                ResolveCompilerDirectives(overload.Body.Children, overload);
            }

            // 3. Loop through body and verify all ASTs
            var returned = VerifyScope(overload.Body, overload, _globalScope, false);

            // 4. Verify the body returns on all paths
            if (!returned)
            {
                ErrorReporter.Report($"Overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}' does not return type '{PrintTypeDefinition(overload.ReturnTypeDefinition)}' on all paths", overload);
            }
            overload.Flags |= FunctionFlags.Verified;

            if (!ErrorReporter.Errors.Any())
            {
                _irBuilder.AddOperatorOverload(overload);
            }
        }

        private void ResolveCompilerDirectives(List<IAst> asts, IFunction function)
        {
            for (int i = 0; i < asts.Count; i++)
            {
                var ast = asts[i];
                switch (ast)
                {
                    case ScopeAst scope:
                        ResolveCompilerDirectives(scope.Children, function);
                        break;
                    case WhileAst whileAst:
                        ResolveCompilerDirectives(whileAst.Body.Children, function);
                        break;
                    case EachAst each:
                        ResolveCompilerDirectives(each.Body.Children, function);
                        break;
                    case ConditionalAst conditional:
                        if (conditional.IfBlock != null) ResolveCompilerDirectives(conditional.IfBlock.Children, function);
                        if (conditional.ElseBlock != null) ResolveCompilerDirectives(conditional.ElseBlock.Children, function);
                        break;
                    case CompilerDirectiveAst directive:
                        asts.RemoveAt(i);
                        switch (directive.Type)
                        {
                            case DirectiveType.If:

                                var conditional = directive.Value as ConditionalAst;
                                if (VerifyCondition(conditional.Condition, null, _globalScope))
                                {
                                    var condition = _irBuilder.CreateRunnableCondition(conditional.Condition, _globalScope);
                                    _runner.Init();

                                    if (_runner.ExecuteCondition(condition, conditional.Condition))
                                    {
                                        asts.InsertRange(i, conditional.IfBlock.Children);
                                    }
                                    else if (conditional.ElseBlock != null)
                                    {
                                        asts.InsertRange(i, conditional.ElseBlock.Children);
                                    }
                                }
                                break;
                            case DirectiveType.Assert:
                                if (VerifyCondition(directive.Value, null, _globalScope))
                                {
                                    var condition = _irBuilder.CreateRunnableCondition(directive.Value, _globalScope);
                                    _runner.Init();

                                    if (!_runner.ExecuteCondition(condition, directive.Value))
                                    {
                                        if (function is FunctionAst functionAst)
                                        {
                                            ErrorReporter.Report($"Assertion failed in function '{functionAst.Name}'", directive.Value);
                                        }
                                        else if (function is OperatorOverloadAst overload)
                                        {
                                            ErrorReporter.Report($"Assertion failed in overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", directive.Value);
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

        private bool VerifyScope(ScopeAst scope, IFunction currentFunction, ScopeAst parentScope, bool canBreak)
        {
            // 1. Set the parent scope
            scope.Parent = parentScope;

            // 2. Verify function lines
            var returns = false;
            foreach (var ast in scope.Children)
            {
                if (VerifyAst(ast, currentFunction, scope, canBreak))
                {
                    returns = true;
                }
            }
            return returns;
        }

        private bool VerifyAst(IAst syntaxTree, IFunction currentFunction, ScopeAst scope, bool canBreak)
        {
            switch (syntaxTree)
            {
                case ReturnAst returnAst:
                    VerifyReturnStatement(returnAst, currentFunction, scope);
                    return true;
                case DeclarationAst declaration:
                    VerifyDeclaration(declaration, currentFunction, scope);
                    break;
                case AssignmentAst assignment:
                    VerifyAssignment(assignment, currentFunction, scope);
                    break;
                case ScopeAst newScope:
                    return VerifyScope(newScope, currentFunction, scope, canBreak);
                case ConditionalAst conditional:
                    return VerifyConditional(conditional, currentFunction, scope, canBreak);
                case WhileAst whileAst:
                    VerifyWhile(whileAst, currentFunction, scope);
                    break;
                case EachAst each:
                    VerifyEach(each, currentFunction, scope);
                    break;
                case BreakAst:
                    if (!canBreak)
                    {
                        ErrorReporter.Report("No parent loop to break", syntaxTree);
                    }
                    break;
                case ContinueAst:
                    if (!canBreak)
                    {
                        ErrorReporter.Report("No parent loop to continue", syntaxTree);
                    }
                    break;
                default:
                    VerifyExpression(syntaxTree, currentFunction, scope);
                    break;
            }

            return false;
        }

        private void VerifyReturnStatement(ReturnAst returnAst, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Infer the return type of the function
            var returnTypeKind = VerifyType(currentFunction.ReturnTypeDefinition);

            // 2. Handle void case since it's the easiest to interpret
            if (returnTypeKind == TypeKind.Void)
            {
                if (returnAst.Value != null)
                {
                    ErrorReporter.Report("Function return should be void", returnAst);
                }
            }
            else
            {
                // 3. Determine if the expression returns the correct value
                var returnValueType = VerifyExpression(returnAst.Value, currentFunction, scope);
                if (returnValueType == null)
                {
                    ErrorReporter.Report($"Expected to return type '{PrintTypeDefinition(currentFunction.ReturnTypeDefinition)}'", returnAst);
                }
                else
                {
                    if (!TypeEquals(currentFunction.ReturnTypeDefinition, returnValueType))
                    {
                        ErrorReporter.Report($"Expected to return type '{PrintTypeDefinition(currentFunction.ReturnTypeDefinition)}', but returned type '{PrintTypeDefinition(returnValueType)}'", returnAst.Value);
                    }
                }
            }
        }

        private void VerifyGlobalVariable(DeclarationAst declaration)
        {
            // 1. Verify the variable is already defined
            if (GetScopeIdentifier(_globalScope, declaration.Name, out _))
            {
                ErrorReporter.Report($"Identifier '{declaration.Name}' already defined", declaration);
                return;
            }

            // 2. Verify the null values
            if (declaration.Value is NullAst nullAst)
            {
                // 2a. Verify null can be assigned
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report("Cannot assign null value without declaring a type", declaration.Value);
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type == TypeKind.Error)
                    {
                        ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else if (type != TypeKind.Pointer)
                    {
                        ErrorReporter.Report("Cannot assign null to non-pointer type", declaration.Value);
                    }

                    nullAst.TargetTypeDefinition = declaration.TypeDefinition;
                    nullAst.TargetType = declaration.Type;
                }
            }
            // 3. Verify declaration values
            else if (declaration.Value != null)
            {
                VerifyGlobalVariableValue(declaration);
            }
            // 4. Verify object initializers
            else if (declaration.Assignments != null)
            {
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report("Struct literals are not yet supported", declaration);
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type != TypeKind.Struct && type != TypeKind.String)
                    {
                        ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                        return;
                    }

                    var structDef = TypeTable.Types[declaration.TypeDefinition.GenericName] as StructAst;
                    var fields = structDef!.Fields.ToDictionary(_ => _.Name);
                    foreach (var (name, assignment) in declaration.Assignments)
                    {
                        if (!fields.TryGetValue(name, out var field))
                        {
                            ErrorReporter.Report($"Field '{name}' not present in struct '{PrintTypeDefinition(declaration.TypeDefinition)}'", assignment.Reference);
                        }

                        if (assignment.Operator != Operator.None)
                        {
                            ErrorReporter.Report("Cannot have operator assignments in object initializers", assignment.Reference);
                        }

                        var valueType = VerifyConstantExpression(assignment.Value, null, _globalScope, out var isConstant, out _);
                        if (valueType != null && field != null)
                        {
                            if (!TypeEquals(field.TypeDefinition, valueType))
                            {
                                ErrorReporter.Report($"Expected field value to be type '{PrintTypeDefinition(field.TypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            else if (!isConstant)
                            {
                                ErrorReporter.Report($"Global variables can only be initialized with constant values", assignment.Value);
                            }
                            else if (field.TypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                            {
                                VerifyConstant(constant, field.TypeDefinition);
                            }
                        }
                    }
                }
            }
            // 5. Verify array initializer
            else if (declaration.ArrayValues != null)
            {
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report($"Declaration for variable '{declaration.Name}' with array initializer must have the type declared", declaration);
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type != TypeKind.Error && type != TypeKind.Array && type != TypeKind.CArray)
                    {
                        ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else
                    {
                        declaration.TypeDefinition.ConstCount = (uint)declaration.ArrayValues.Count;
                        var elementType = declaration.TypeDefinition.Generics[0];
                        declaration.ArrayElementType = TypeTable.GetType(elementType);
                        foreach (var value in declaration.ArrayValues)
                        {
                            var valueType = VerifyConstantExpression(value, null, _globalScope, out var isConstant, out _);
                            if (valueType != null)
                            {
                                if (!TypeEquals(elementType, valueType))
                                {
                                    ErrorReporter.Report($"Expected array value to be type '{PrintTypeDefinition(elementType)}', but got '{PrintTypeDefinition(valueType)}'", value);
                                }
                                else if (!isConstant)
                                {
                                    ErrorReporter.Report($"Global variables can only be initialized with constant values", value);
                                }
                                else if (elementType.PrimitiveType != null && value is ConstantAst constant)
                                {
                                    VerifyConstant(constant, elementType);
                                }
                            }
                        }
                    }
                }
            }
            // 6. Verify the declaration type
            else
            {
                switch (declaration.Name)
                {
                    case "os":
                        declaration.Value = GetOSVersion();
                        VerifyGlobalVariableValue(declaration);
                        break;
                    case "build_env":
                        declaration.Value = GetBuildEnv();
                        VerifyGlobalVariableValue(declaration);
                        break;
                    default:
                        var type = VerifyType(declaration.TypeDefinition);
                        if (type == TypeKind.Error)
                        {
                            ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                        }
                        else if (type == TypeKind.Void)
                        {
                            ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
                        }
                        break;
                }
            }

            // 7. Verify the type definition count if necessary
            if (declaration.TypeDefinition != null)
            {
                if (declaration.TypeDefinition.TypeKind == TypeKind.CArray && declaration.TypeDefinition.Count == null && declaration.TypeDefinition.ConstCount == null)
                {
                    ErrorReporter.Report($"Length of C array variable '{declaration.Name}' must be initialized to a constant integer", declaration.TypeDefinition);
                }
                else if (declaration.TypeDefinition.Count != null)
                {
                    var countType = VerifyConstantExpression(declaration.TypeDefinition.Count, null, _globalScope, out var isConstant, out var count);

                    if (countType != null)
                    {
                        if (declaration.TypeDefinition.TypeKind == TypeKind.CArray && !isConstant)
                        {
                            ErrorReporter.Report($"Length of C array variable '{declaration.Name}' must be initialized with a constant size", declaration.TypeDefinition.Count);
                        }
                        else if (countType.PrimitiveType is not IntegerType)
                        {
                            ErrorReporter.Report($"Expected count of variable '{declaration.Name}' to be an integer", declaration.TypeDefinition.Count);
                        }
                        if (isConstant)
                        {
                            if (count < 0)
                            {
                                ErrorReporter.Report($"Expected count of variable '{declaration.Name}' to be a positive integer", declaration.TypeDefinition.Count);
                            }
                            else
                            {
                                declaration.TypeDefinition.ConstCount = (uint)count;
                            }
                        }
                    }
                }
            }

            // 8. Verify constant values
            if (declaration.Constant)
            {
                if (declaration.Value == null)
                {
                    ErrorReporter.Report($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
                }
                else if (declaration.TypeDefinition != null)
                {
                    declaration.TypeDefinition.Constant = true;
                }
            }

            if (!ErrorReporter.Errors.Any())
            {
                declaration.Type = TypeTable.GetType(declaration.TypeDefinition);
                _irBuilder.EmitGlobalVariable(declaration, _globalScope);
            }

            _globalScope.Identifiers.TryAdd(declaration.Name, declaration);
        }

        private void VerifyGlobalVariableValue(DeclarationAst declaration)
        {
            var valueType = VerifyConstantExpression(declaration.Value, null, _globalScope, out var isConstant, out _);
            if (!isConstant)
            {
                ErrorReporter.Report($"Global variables can only be initialized with constant values", declaration.Value);
            }

            // Verify the assignment value matches the type definition if it has been defined
            if (declaration.TypeDefinition == null)
            {
                if (VerifyType(valueType) == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                }
                declaration.TypeDefinition = valueType;
            }
            else
            {
                var type = VerifyType(declaration.TypeDefinition);
                if (type == TypeKind.Error)
                {
                    ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else if (type == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
                }

                // Verify the type is correct
                if (valueType != null)
                {
                    if (!TypeEquals(declaration.TypeDefinition, valueType))
                    {
                        ErrorReporter.Report($"Expected declaration value to be type '{PrintTypeDefinition(declaration.TypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", declaration.TypeDefinition);
                    }
                    else if (declaration.TypeDefinition.PrimitiveType != null && declaration.Value is ConstantAst constant)
                    {
                        VerifyConstant(constant, declaration.TypeDefinition);
                    }
                }
            }
        }

        private void VerifyDeclaration(DeclarationAst declaration, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Verify the variable is already defined
            if (GetScopeIdentifier(scope, declaration.Name, out _))
            {
                ErrorReporter.Report($"Identifier '{declaration.Name}' already defined", declaration);
                return;
            }

            // 2. Verify the null values
            if (declaration.Value is NullAst nullAst)
            {
                // 2a. Verify null can be assigned
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report("Cannot assign null value without declaring a type", declaration.Value);
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type == TypeKind.Error)
                    {
                        ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else if (type != TypeKind.Pointer)
                    {
                        ErrorReporter.Report("Cannot assign null to non-pointer type", declaration.Value);
                    }

                    nullAst.TargetTypeDefinition = declaration.TypeDefinition;
                    nullAst.TargetType = declaration.Type;
                }
            }
            // 3. Verify declaration values
            else if (declaration.Value != null)
            {
                var valueType = VerifyExpression(declaration.Value, currentFunction, scope);

                // Verify the assignment value matches the type definition if it has been defined
                if (declaration.TypeDefinition == null)
                {
                    if (VerifyType(valueType) == TypeKind.Void)
                    {
                        ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                    }
                    declaration.TypeDefinition = valueType;
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type == TypeKind.Error)
                    {
                        ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else if (type == TypeKind.Void)
                    {
                        ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
                    }

                    // Verify the type is correct
                    if (valueType != null)
                    {
                        if (!TypeEquals(declaration.TypeDefinition, valueType))
                        {
                            ErrorReporter.Report($"Expected declaration value to be type '{PrintTypeDefinition(declaration.TypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", declaration.TypeDefinition);
                        }
                        else if (declaration.TypeDefinition.PrimitiveType != null && declaration.Value is ConstantAst constant)
                        {
                            VerifyConstant(constant, declaration.TypeDefinition);
                        }
                    }
                }
            }
            // 4. Verify object initializers
            else if (declaration.Assignments != null)
            {
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report("Struct literals are not yet supported", declaration);
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type != TypeKind.Struct && type != TypeKind.String)
                    {
                        ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                        return;
                    }

                    var structDef = TypeTable.Types[declaration.TypeDefinition.GenericName] as StructAst;
                    var fields = structDef!.Fields.ToDictionary(_ => _.Name);
                    foreach (var (name, assignment) in declaration.Assignments)
                    {
                        if (!fields.TryGetValue(name, out var field))
                        {
                            ErrorReporter.Report($"Field '{name}' not present in struct '{PrintTypeDefinition(declaration.TypeDefinition)}'", assignment.Reference);
                        }

                        if (assignment.Operator != Operator.None)
                        {
                            ErrorReporter.Report("Cannot have operator assignments in object initializers", assignment.Reference);
                        }

                        var valueType = VerifyExpression(assignment.Value, currentFunction, scope);
                        if (valueType != null && field != null)
                        {
                            if (!TypeEquals(field.TypeDefinition, valueType))
                            {
                                ErrorReporter.Report($"Expected field value to be type '{PrintTypeDefinition(field.TypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            else if (field.TypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                            {
                                VerifyConstant(constant, field.TypeDefinition);
                            }
                        }
                    }
                }
            }
            // 5. Verify array initializer
            else if (declaration.ArrayValues != null)
            {
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report($"Declaration for variable '{declaration.Name}' with array initializer must have the type declared", declaration);
                }
                else
                {
                    var type = VerifyType(declaration.TypeDefinition);
                    if (type != TypeKind.Error && type != TypeKind.Array && type != TypeKind.CArray)
                    {
                        ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else
                    {
                        declaration.TypeDefinition.ConstCount = (uint)declaration.ArrayValues.Count;
                        var elementType = declaration.TypeDefinition.Generics[0];
                        declaration.ArrayElementType = TypeTable.GetType(elementType);
                        foreach (var value in declaration.ArrayValues)
                        {
                            var valueType = VerifyExpression(value, currentFunction, scope);
                            if (valueType != null)
                            {
                                if (!TypeEquals(elementType, valueType))
                                {
                                    ErrorReporter.Report($"Expected array value to be type '{PrintTypeDefinition(elementType)}', but got '{PrintTypeDefinition(valueType)}'", value);
                                }
                                else if (elementType.PrimitiveType != null && value is ConstantAst constant)
                                {
                                    VerifyConstant(constant, elementType);
                                }
                            }
                        }
                    }
                }
            }
            // 6. Verify the declaration type
            else
            {
                var type = VerifyType(declaration.TypeDefinition);
                if (type == TypeKind.Error)
                {
                    ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else if (type == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
                }
                else if (type == TypeKind.Array || type == TypeKind.CArray)
                {
                    var elementType = declaration.TypeDefinition.Generics[0];
                    declaration.ArrayElementType = TypeTable.GetType(elementType);
                }
            }

            // 7. Verify the type definition count if necessary
            if (declaration.TypeDefinition != null)
            {
                if (declaration.TypeDefinition.TypeKind == TypeKind.CArray && declaration.TypeDefinition.Count == null && declaration.TypeDefinition.ConstCount == null)
                {
                    ErrorReporter.Report($"Length of C array variable '{declaration.Name}' must be initialized to a constant integer", declaration.TypeDefinition);
                }
                else if (declaration.TypeDefinition.Count != null)
                {
                    var countType = VerifyConstantExpression(declaration.TypeDefinition.Count, currentFunction, scope, out var isConstant, out var count);

                    if (countType != null)
                    {
                        if (declaration.TypeDefinition.TypeKind == TypeKind.CArray && !isConstant)
                        {
                            ErrorReporter.Report($"Length of C array variable '{declaration.Name}' must be initialized with a constant size", declaration.TypeDefinition.Count);
                        }
                        else if (countType.PrimitiveType is not IntegerType)
                        {
                            ErrorReporter.Report($"Expected count of variable '{declaration.Name}' to be an integer", declaration.TypeDefinition.Count);
                        }
                        if (isConstant)
                        {
                            if (count < 0)
                            {
                                ErrorReporter.Report($"Expected count of variable '{declaration.Name}' to be a positive integer", declaration.TypeDefinition.Count);
                            }
                            else
                            {
                                declaration.TypeDefinition.ConstCount = (uint)count;
                            }
                        }
                    }
                }
            }

            // 8. Verify constant values
            if (declaration.Constant)
            {
                switch (declaration.Value)
                {
                    case ConstantAst constant:
                        constant.TypeDefinition.Constant = true;
                        break;
                    case StructFieldRefAst structField when structField.IsEnum:
                        break;
                    default:
                        ErrorReporter.Report($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
                        break;
                }
                if (declaration.TypeDefinition != null)
                {
                    declaration.TypeDefinition.Constant = true;
                }
            }

            // TODO Move somewhere
            if (declaration.TypeDefinition != null)
            {
                declaration.Type = TypeTable.GetType(declaration.TypeDefinition);
            }

            scope.Identifiers.TryAdd(declaration.Name, declaration);
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
                    new IdentifierAst {Name = BuildSettings.Release ? "Release" : "Debug"}
                }
            };
        }

        private void VerifyAssignment(AssignmentAst assignment, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Verify the variable is already defined, that it is not a constant, and the r-value
            var variableTypeDefinition = GetReference(assignment.Reference, currentFunction, scope, out _);
            var valueType = VerifyExpression(assignment.Value, currentFunction, scope);

            if (variableTypeDefinition == null) return;

            if (variableTypeDefinition.Constant)
            {
                var variable = assignment.Reference as IdentifierAst;
                ErrorReporter.Report($"Cannot reassign value of constant variable '{variable?.Name}'", assignment);
                return;
            }

            // 2. Verify the assignment value
            if (assignment.Value is NullAst nullAst)
            {
                if (assignment.Operator != Operator.None)
                {
                    ErrorReporter.Report("Cannot assign null value with operator assignment", assignment.Value);
                }
                if (variableTypeDefinition.Name != "*")
                {
                    ErrorReporter.Report("Cannot assign null to non-pointer type", assignment.Value);
                }
                nullAst.TargetTypeDefinition = variableTypeDefinition;
                nullAst.TargetType = TypeTable.GetType(variableTypeDefinition);
                return;
            }

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
                                ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                        // Invalid assignment operators
                        case Operator.Equality:
                        case Operator.GreaterThan:
                        case Operator.LessThan:
                        case Operator.GreaterThanEqual:
                        case Operator.LessThanEqual:
                            ErrorReporter.Report($"Invalid operator '{PrintOperator(assignment.Operator)}' in assignment", assignment);
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
                                ErrorReporter.Report($"Operator {PrintOperator(assignment.Operator)} not applicable to types '{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (!(lhs == TypeKind.Boolean && rhs == TypeKind.Boolean) &&
                                !(lhs == TypeKind.Integer && rhs == TypeKind.Integer))
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(assignment.Operator)} not applicable to types " +
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
                                ErrorReporter.Report($"Operator {PrintOperator(assignment.Operator)} not applicable to types '{PrintTypeDefinition(variableTypeDefinition)}' and '{PrintTypeDefinition(valueType)}'", assignment.Value);
                            }
                            break;
                    }
                }
                else if (!TypeEquals(variableTypeDefinition, valueType))
                {
                    ErrorReporter.Report($"Expected assignment value to be type '{PrintTypeDefinition(variableTypeDefinition)}', but got '{PrintTypeDefinition(valueType)}'", assignment.Value);
                }
                else if (variableTypeDefinition.PrimitiveType != null && assignment.Value is ConstantAst constant)
                {
                    VerifyConstant(constant, variableTypeDefinition);
                }
            }
        }

        // private TypeDefinition GetReference(IAst ast, IFunction currentFunction, ScopeAst scope, out bool hasPointer, bool fromUnaryReference = false)
        private IType GetReference(IAst ast, IFunction currentFunction, ScopeAst scope, out bool hasPointer, bool fromUnaryReference = false)
        {
            hasPointer = true;
            switch (ast)
            {
                case IdentifierAst identifier:
                {
                    var variableType = GetVariable(identifier.Name, identifier, scope, out var constant);
                    if (constant)
                    {
                        ErrorReporter.Report($"Cannot reassign value of constant variable '{identifier.Name}'", identifier);
                    }
                    return variableType;
                }
                case IndexAst index:
                {
                    var variableType = GetVariable(index.Name, index, scope, out var constant);
                    if (variableType == null) return null;
                    if (constant)
                    {
                        ErrorReporter.Report($"Cannot reassign value of constant variable '{index.Name}'", index);
                    }
                    var type = VerifyIndex(index, variableType, currentFunction, scope, out var overloaded);
                    if (type != null && overloaded)
                    {
                        if (type.TypeKind != TypeKind.Pointer)
                        {
                            ErrorReporter.Report($"Overload [] for type '{variableType.Name}' must be a pointer to be able to set the value", index);
                            return null;
                        }
                        hasPointer = false;
                        // return type.Generics[0];
                        var pointerType = (PrimitiveAst)type;
                        return pointerType.PointerType;
                    }
                    return type;
                }
                case StructFieldRefAst structFieldRef:
                {
                    structFieldRef.Pointers = new bool[structFieldRef.Children.Count - 1];
                    structFieldRef.Types = new IType[structFieldRef.Children.Count - 1];
                    structFieldRef.ValueIndices = new int[structFieldRef.Children.Count - 1];

                    // TypeDefinition refType;
                    IType refType;
                    switch (structFieldRef.Children[0])
                    {
                        case IdentifierAst identifier:
                        {
                            refType = GetVariable(identifier.Name, identifier, scope, out var constant, true);
                            if (refType == null) return null;
                            if (constant)
                            {
                                ErrorReporter.Report($"Cannot reassign value of constant variable '{identifier.Name}'", identifier);
                                return null;
                            }
                            break;
                        }
                        case IndexAst index:
                        {
                            var variableType = GetVariable(index.Name, index, scope, out var constant);
                            if (variableType == null) return null;
                            if (constant)
                            {
                                ErrorReporter.Report($"Cannot reassign value of constant variable '{index.Name}'", index);
                                return null;
                            }
                            refType = VerifyIndex(index, variableType, currentFunction, scope, out var overloaded);
                            if (refType != null && overloaded && refType.TypeKind != TypeKind.Pointer)
                            {
                                ErrorReporter.Report($"Overload [] for type '{variableType.Name}' must be a pointer to be able to set the value", index);
                                return null;
                            }
                            break;
                        }
                        default:
                            ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", structFieldRef.Children[0]);
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
                                refType = VerifyIndex(index, fieldType, currentFunction, scope, out var overloaded);
                                if (refType != null && overloaded)
                                {
                                    if (refType.TypeKind == TypeKind.Pointer)
                                    {
                                        if (i == structFieldRef.Children.Count - 1)
                                        {
                                            hasPointer = false;
                                            // refType = refType.Generics[0];
                                            var pointerType = (PrimitiveAst)refType;
                                            refType = pointerType.PointerType;
                                        }
                                    }
                                    else
                                    {
                                        ErrorReporter.Report($"Overload [] for type '{PrintTypeDefinition(fieldType)}' must be a pointer to be able to set the value", index);
                                        return null;
                                    }
                                }
                                break;
                            default:
                                ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", structFieldRef.Children[i]);
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
                {
                    if (fromUnaryReference)
                    {
                        ErrorReporter.Report("Operators '*' and '&' cancel each other out", unary);
                        return null;
                    }
                    var reference = GetReference(unary.Value, currentFunction, scope, out var canDereference);
                    if (reference == null)
                    {
                        return null;
                    }
                    if (!canDereference)
                    {
                        ErrorReporter.Report("Cannot dereference pointer to assign value", unary.Value);
                        return null;
                    }

                    if (reference.TypeKind != TypeKind.Pointer)
                    {
                        ErrorReporter.Report("Expected to get pointer to dereference", unary.Value);
                        return null;
                    }
                    // return reference.Generics[0];
                    var pointerType = (PrimitiveAst)reference;
                    return pointerType.PointerType;
                }
                default:
                    ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", ast);
                    return null;
            }
        }

        // private TypeDefinition GetVariable(string name, IAst ast, ScopeAst scope, bool allowEnums = false)
        private IType GetVariable(string name, IAst ast, ScopeAst scope, out bool constant, bool allowEnums = false)
        {
            constant = false;
            if (!GetScopeIdentifier(scope, name, out var identifier))
            {
                ErrorReporter.Report($"Variable '{name}' not defined", ast);
                return null;
            }
            switch (identifier)
            {
                case EnumAst enumAst when allowEnums:
                    // return new TypeDefinition {Name = enumAst.Name, TypeKind = TypeKind.Enum};
                    constant = true;
                    return enumAst;
                case DeclarationAst declaration:
                    // return declaration.TypeDefinition;
                    constant = declaration.Constant;
                    return declaration.Type;
                case VariableAst variable:
                    // return variable.TypeDefinition;
                    return variable.Type;
                default:
                    ErrorReporter.Report($"Identifier '{name}' is not a variable", ast);
                    return null;
            }
        }

        // private TypeDefinition VerifyStructFieldRef(StructFieldRefAst structField, IFunction currentFunction, ScopeAst scope)
        private IType VerifyStructFieldRef(StructFieldRefAst structField, IFunction currentFunction, ScopeAst scope)
        {
            // TypeDefinition refType;
            IType refType;
            switch (structField.Children[0])
            {
                case IdentifierAst identifier:
                    if (!GetScopeIdentifier(scope, identifier.Name, out var value))
                    {
                        ErrorReporter.Report($"Identifier '{identifier.Name}' not defined", structField);
                        return null;
                    }
                    switch (value)
                    {
                        case EnumAst enumAst:
                            return VerifyEnumValue(enumAst, structField);
                        case DeclarationAst declaration:
                            // refType = declaration.TypeDefinition;
                            refType = declaration.Type;
                            break;
                        case VariableAst variable:
                            // refType = variable.TypeDefinition;
                            refType = variable.Type;
                            break;
                        default:
                            ErrorReporter.Report($"Cannot reference static field of type '{identifier.Name}'", structField);
                            return null;
                    }
                    break;
                default:
                    refType = VerifyExpression(structField.Children[0], currentFunction, scope);
                    break;
            }
            if (refType == null)
            {
                return null;
            }
            structField.Pointers = new bool[structField.Children.Count - 1];
            structField.Types = new IType[structField.Children.Count - 1];
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
                        refType = VerifyIndex(index, fieldType, currentFunction, scope, out _);
                        break;
                    default:
                        ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", structField.Children[i]);
                        return null;
                }
                if (refType == null)
                {
                    return null;
                }
            }
            return refType;
        }

        // private TypeDefinition VerifyStructField(string fieldName, TypeDefinition structType, StructFieldRefAst structField, int fieldIndex, IAst ast)
        private IType VerifyStructField(string fieldName, IType structType, StructFieldRefAst structField, int fieldIndex, IAst ast)
        {
            // 1. Load the struct definition in typeDefinition
            // if (structType.Name == "*")
            // {
            //     structType = structType.Generics[0];
            //     structField.Pointers[fieldIndex] = true;
            // }
            // if (!TypeTable.Types.TryGetValue(structType.GenericName, out var type))
            // {
            //     ErrorReporter.Report($"Type '{PrintTypeDefinition(structType)}' not defined", ast);
            //     return null;
            // }
            if (structType.TypeKind == TypeKind.Pointer)
            {
                var pointerType = (PrimitiveAst)structType;
                structType = pointerType.PointerType;
                structField.Pointers[fieldIndex] = true;
            }

            structField.Types[fieldIndex] = structType;
            if (structType is ArrayType && fieldName == "length")
            {
                structField.IsConstant = true;
                structField.ConstantValue = structType.ConstCount.Value;
                return _s32Type;
            }
            if (structType is not StructAst structDefinition)
            {
                ErrorReporter.Report($"Type '{structType.Name}' does not contain field '{fieldName}'", ast);
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
                ErrorReporter.Report($"Struct '{structType.Name}' does not contain field '{fieldName}'", ast);
                return null;
            }

            // return field.TypeDefinition;
            return field.Type;
        }

        private bool VerifyConditional(ConditionalAst conditional, IFunction currentFunction, ScopeAst scope, bool canBreak)
        {
            // 1. Verify the condition expression
            VerifyCondition(conditional.Condition, currentFunction, scope);

            // 2. Verify the conditional scope
            var ifReturns = VerifyScope(conditional.IfBlock, currentFunction, scope, canBreak);

            // 3. Verify the else block if necessary
            if (conditional.ElseBlock != null)
            {
                var elseReturns = VerifyScope(conditional.ElseBlock, currentFunction, scope, canBreak);
                return ifReturns && elseReturns;
            }

            return false;
        }

        private void VerifyWhile(WhileAst whileAst, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Verify the condition expression
            VerifyCondition(whileAst.Condition, currentFunction, scope);

            // 2. Verify the scope of the while block
            VerifyScope(whileAst.Body, currentFunction, scope, true);
        }

        private bool VerifyCondition(IAst ast, IFunction currentFunction, ScopeAst scope)
        {
            var conditionalType = VerifyExpression(ast, currentFunction, scope);
            switch (VerifyType(conditionalType))
            {
                case TypeKind.Integer:
                case TypeKind.Float:
                case TypeKind.Boolean:
                case TypeKind.Pointer:
                    // Valid types
                    return !ErrorReporter.Errors.Any();
                case TypeKind.Error:
                    return false;
                default:
                    ErrorReporter.Report($"Expected condition to be bool, int, float, or pointer, but got '{PrintTypeDefinition(conditionalType)}'", ast);
                    return false;
            }
        }

        private void VerifyEach(EachAst each, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Verify the iterator or range
            if (GetScopeIdentifier(scope, each.IterationVariable.Name, out _))
            {
                ErrorReporter.Report($"Iteration variable '{each.IterationVariable.Name}' already exists in scope", each);
            };
            if (each.Iteration != null)
            {
                var iterationTypeDefinition = VerifyExpression(each.Iteration, currentFunction, scope);
                if (iterationTypeDefinition == null) return;
                each.IterationVariable.TypeDefinition = iterationTypeDefinition.Generics.FirstOrDefault();

                if (each.IndexVariable != null)
                {
                    each.IndexVariable.TypeDefinition = _s32Type;
                    each.IndexVariable.Type = TypeTable.Types["s32"];
                    each.Body.Identifiers.TryAdd(each.IndexVariable.Name, each.IndexVariable);
                }

                switch (iterationTypeDefinition.TypeKind)
                {
                    case TypeKind.CArray:
                        each.CArrayLength = iterationTypeDefinition.ConstCount.Value;
                        // Same logic as below with special case for constant length
                        each.IteratorType = each.IterationVariable.TypeDefinition;
                        each.IterationVariable.Type = TypeTable.GetType(each.IterationVariable.TypeDefinition);
                        each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
                        break;
                    case TypeKind.Array:
                    case TypeKind.Params:
                        each.IteratorType = each.IterationVariable.TypeDefinition;
                        each.IterationVariable.Type = TypeTable.GetType(each.IterationVariable.TypeDefinition);
                        each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
                        break;
                    default:
                        ErrorReporter.Report($"Type {PrintTypeDefinition(iterationTypeDefinition)} cannot be used as an iterator", each.Iteration);
                        break;
                }
            }
            else
            {
                var begin = VerifyExpression(each.RangeBegin, currentFunction, scope);
                var beginType = VerifyType(begin);
                if (beginType != TypeKind.Integer && beginType != TypeKind.Error)
                {
                    ErrorReporter.Report($"Expected range to begin with 'int', but got '{PrintTypeDefinition(begin)}'", each.RangeBegin);
                }
                var end = VerifyExpression(each.RangeEnd, currentFunction, scope);
                var endType = VerifyType(end);
                if (endType != TypeKind.Integer && endType != TypeKind.Error)
                {
                    ErrorReporter.Report($"Expected range to end with 'int', but got '{PrintTypeDefinition(end)}'", each.RangeEnd);
                }
                each.IterationVariable.TypeDefinition = _s32Type;
                each.IterationVariable.Type = TypeTable.Types["s32"];
                each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
            }

            // 2. Verify the scope of the each block
            VerifyScope(each.Body, currentFunction, scope, true);
        }

        private void VerifyTopLevelDirective(CompilerDirectiveAst directive)
        {
            switch (directive.Type)
            {
                case DirectiveType.Run:
                    VerifyAst(directive.Value, null, _globalScope, false);
                    if (!ErrorReporter.Errors.Any())
                    {
                        var function = _irBuilder.CreateRunnableFunction(directive.Value, _globalScope);

                        _runner.Init();
                        _runner.RunProgram(function, directive.Value);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Compiler directive '{directive.Type}' not supported", directive);
                    break;
            }
        }

        // private TypeDefinition VerifyConstantExpression(IAst ast, IFunction currentFunction, ScopeAst scope, out bool isConstant, out int count)
        private IType VerifyConstantExpression(IAst ast, IFunction currentFunction, ScopeAst scope, out bool isConstant, out int count)
        {
            isConstant = false;
            count = 0;
            switch (ast)
            {
                case ConstantAst constant:
                    isConstant = true;
                    if (constant.TypeDefinition.PrimitiveType is IntegerType)
                    {
                        int.TryParse(constant.Value, out count);
                    }
                    VerifyType(constant.TypeDefinition);
                    constant.Type = TypeTable.GetType(constant.TypeDefinition);
                    // return constant.TypeDefinition;
                    return constant.Type;
                case NullAst:
                    isConstant = true;
                    return null;
                case StructFieldRefAst structField:
                    var structFieldType = VerifyStructFieldRef(structField, currentFunction, scope);
                    // isConstant = structFieldType?.PrimitiveType is EnumType;
                    isConstant = structFieldType is EnumAst;
                    return structFieldType;
                case IdentifierAst identifierAst:
                    if (!GetScopeIdentifier(scope, identifierAst.Name, out var identifier))
                    {
                        if (TypeTable.Functions.TryGetValue(identifierAst.Name, out var functions))
                        {
                            if (functions.Count > 1)
                            {
                                ErrorReporter.Report($"Cannot determine type for function '{identifierAst.Name}' that has multiple overloads", identifierAst);
                                return null;
                            }
                            // return new TypeDefinition {Name = "Type", TypeKind = TypeKind.Type, TypeIndex = functions[0].TypeIndex};
                            return functions[0];
                        }
                        ErrorReporter.Report($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                    }
                    switch (identifier)
                    {
                        case DeclarationAst declaration:
                            isConstant = declaration.Constant;
                            if (isConstant && declaration.TypeDefinition.PrimitiveType is IntegerType)
                            {
                                if (declaration.Value is ConstantAst constValue)
                                {
                                    int.TryParse(constValue.Value, out count);
                                }
                            }
                            // return declaration.TypeDefinition;
                            return declaration.Type;
                        case IType type:
                            if (type is StructAst structAst && structAst.Generics.Any())
                            {
                                ErrorReporter.Report($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                            }
                            // return new TypeDefinition {Name = "Type", TypeKind = TypeKind.Type, TypeIndex = type.TypeIndex};
                            return type;
                        default:
                            return null;
                    }
                default:
                    return VerifyExpression(ast, currentFunction, scope);
            }
        }

        // private TypeDefinition VerifyExpression(IAst ast, IFunction currentFunction, ScopeAst scope)
        private IType VerifyExpression(IAst ast, IFunction currentFunction, ScopeAst scope, out bool isType)
        {
            // 1. Verify the expression value
            isType = false;
            switch (ast)
            {
                case ConstantAst constant:
                    VerifyType(constant.TypeDefinition);
                    constant.Type = TypeTable.GetType(constant.TypeDefinition);
                    // return constant.TypeDefinition;
                    return constant.Type;
                case NullAst:
                    return null;
                case StructFieldRefAst structField:
                    return VerifyStructFieldRef(structField, currentFunction, scope);
                case IdentifierAst identifierAst:
                    if (!GetScopeIdentifier(scope, identifierAst.Name, out var identifier))
                    {
                        if (TypeTable.Functions.TryGetValue(identifierAst.Name, out var functions))
                        {
                            if (functions.Count > 1)
                            {
                                ErrorReporter.Report($"Cannot determine type for function '{identifierAst.Name}' that has multiple overloads", identifierAst);
                                return null;
                            }
                            // return new TypeDefinition {Name = "Type", TypeKind = TypeKind.Type, TypeIndex = functions[0].TypeIndex};
                            isType = true;
                            return functions[0];
                        }
                        ErrorReporter.Report($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                    }
                    switch (identifier)
                    {
                        case DeclarationAst declaration:
                            // return declaration.TypeDefinition;
                            return declaration.Type;
                        case VariableAst variable:
                            // return variable.TypeDefinition;
                            return variable.Type;
                        case IType type:
                            if (type is StructAst structAst && structAst.Generics.Any())
                            {
                                ErrorReporter.Report($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                            }
                            // return new TypeDefinition {Name = "Type", TypeKind = TypeKind.Type, TypeIndex = type.TypeIndex};
                            isType = true;
                            return type;
                        default:
                            return null;
                    }
                case ChangeByOneAst changeByOne:
                    var op = changeByOne.Positive ? "increment" : "decrement";
                    switch (changeByOne.Value)
                    {
                        case IdentifierAst:
                        case StructFieldRefAst:
                        case IndexAst:
                            var expressionType = GetReference(changeByOne.Value, currentFunction, scope, out _);
                            if (expressionType != null)
                            {
                                if (expressionType.TypeKind != TypeKind.Integer && expressionType.TypeKind != TypeKind.Float)
                                {
                                    ErrorReporter.Report($"Expected to {op} int or float, but got type '{expressionType.Name}'", changeByOne.Value);
                                    return null;
                                }
                                // changeByOne.Type = TypeTable.GetType(expressionType);
                                changeByOne.Type = expressionType;
                            }

                            return expressionType;
                        default:
                            ErrorReporter.Report($"Expected to {op} variable", changeByOne);
                            return null;
                    }
                case UnaryAst unary:
                    if (unary.Operator == UnaryOperator.Reference)
                    {
                        var referenceType = GetReference(unary.Value, currentFunction, scope, out var hasPointer, true);
                        if (!hasPointer)
                        {
                            ErrorReporter.Report("Unable to get reference of unary value", unary.Value);
                            return null;
                        }

                        var type = VerifyType(referenceType);
                        if (type == TypeKind.Error)
                        {
                            return null;
                        }

                        // TODO Get this figured out
                        var pointerType = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer};
                        if (type == TypeKind.CArray)
                        {
                            pointerType.Generics.Add(referenceType.Generics[0]);
                        }
                        else
                        {
                            pointerType.Generics.Add(referenceType);
                        }
                        unary.Type = TypeTable.GetType(pointerType);
                        return pointerType;
                        // return unary.Type;
                    }
                    else
                    {
                        var valueType = VerifyExpression(unary.Value, currentFunction, scope, out _);
                        // var type = VerifyType(valueType);
                        switch (unary.Operator)
                        {
                            case UnaryOperator.Not:
                                if (valueType.TypeKind == TypeKind.Boolean)
                                {
                                    return valueType;
                                }
                                else if (valueType != null)
                                {
                                    ErrorReporter.Report($"Expected type 'bool', but got type '{valueType.Name}'", unary.Value);
                                }
                                return null;
                            case UnaryOperator.Negate:
                                if (valueType.TypeKind == TypeKind.Integer || valueType.TypeKind == TypeKind.Float)
                                {
                                    return valueType;
                                }
                                else if (valueType != null)
                                {
                                    ErrorReporter.Report($"Negation not compatible with type '{valueType.Name}'", unary.Value);
                                }
                                return null;
                            case UnaryOperator.Dereference:
                                if (valueType.TypeKind == TypeKind.Pointer)
                                {
                                    // valueType = valueType.Generics[0];
                                    // unary.Type = TypeTable.GetType(valueType);
                                    // return valueType;
                                    var pointerType = (PrimitiveAst)valueType;
                                    unary.Type = pointerType.PointerType;
                                    return unary.Type;
                                }
                                else if (valueType != null)
                                {
                                    ErrorReporter.Report($"Cannot dereference type '{valueType.Name}'", unary.Value);
                                }
                                return null;
                            default:
                                ErrorReporter.Report($"Unexpected unary operator '{unary.Operator}'", unary.Value);
                                return null;
                        }
                    }
                case CallAst call:
                    return VerifyCall(call, currentFunction, scope);
                case ExpressionAst expression:
                    return VerifyExpressionType(expression, currentFunction, scope);
                case IndexAst index:
                    return VerifyIndexType(index, currentFunction, scope);
                case TypeDefinition typeDef:
                {
                    if (VerifyType(typeDef) == TypeKind.Error)
                    {
                        return null;
                    }
                    if (!TypeTable.Types.TryGetValue(typeDef.GenericName, out var type))
                    {
                        return null;
                    }
                    typeDef.TypeIndex = type.TypeIndex;
                    // return new TypeDefinition {Name = "Type", TypeKind = TypeKind.Type, TypeIndex = type.TypeIndex};
                    return type;
                }
                case CastAst cast:
                {
                    var targetType = VerifyType(cast.TargetTypeDefinition);
                    var valueType = VerifyExpression(cast.Value, currentFunction, scope, out _);
                    switch (targetType)
                    {
                        case TypeKind.Integer:
                        case TypeKind.Float:
                            switch (valueType?.TypeKind)
                            {
                                case TypeKind.Integer:
                                case TypeKind.Float:
                                case TypeKind.Enum:
                                case null:
                                    break;
                                default:
                                    ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast.Value);
                                    break;
                            }

                            cast.TargetType = TypeTable.GetType(cast.TargetTypeDefinition);
                            break;
                        case TypeKind.Pointer:
                            if (valueType?.TypeKind != TypeKind.Pointer)
                            {
                                ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast.Value);
                            }
                            cast.TargetType = TypeTable.GetType(cast.TargetTypeDefinition);
                            break;
                        case TypeKind.Error:
                            // Don't need to report additional errors
                            return null;
                        default:
                            if (valueType != null)
                            {
                                ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast);
                            }
                            break;
                    }
                    // return cast.TargetTypeDefinition;
                    return cast.TargetType;
                }
                case null:
                    return null;
                default:
                    ErrorReporter.Report($"Invalid expression", ast);
                    return null;
            }
        }

        private void VerifyConstant(ConstantAst constant, TypeDefinition typeDef)
        {
            constant.TypeDefinition.Name = typeDef.Name;
            constant.TypeDefinition.PrimitiveType = typeDef.PrimitiveType;
            constant.Type = TypeTable.GetType(typeDef);

            var type = constant.TypeDefinition;
            switch (type.PrimitiveType)
            {
                case IntegerType integer when !type.Character:
                    if (!integer.Signed && constant.Value[0] == '-')
                    {
                        ErrorReporter.Report($"Unsigned type '{PrintTypeDefinition(constant.TypeDefinition)}' cannot be negative", constant);
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
                        ErrorReporter.Report($"Value '{constant.Value}' out of range for type '{PrintTypeDefinition(constant.TypeDefinition)}'", constant);
                    }
                    break;
            }
        }

        // private TypeDefinition VerifyEnumValue(EnumAst enumAst, StructFieldRefAst structField)
        private IType VerifyEnumValue(EnumAst enumAst, StructFieldRefAst structField)
        {
            structField.IsEnum = true;
            structField.Types = new [] {enumAst};

            if (structField.Children.Count > 2)
            {
                ErrorReporter.Report("Cannot get a value of an enum value", structField.Children[2]);
                return null;
            }

            if (structField.Children[1] is not IdentifierAst value)
            {
                ErrorReporter.Report($"Value of enum '{enumAst.Name}' should be an identifier", structField.Children[1]);
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
                ErrorReporter.Report($"Enum '{enumAst.Name}' does not contain value '{value.Name}'", value);
                return null;
            }

            var primitive = enumAst.BaseTypeDefinition.PrimitiveType;
            // return new TypeDefinition {Name = enumAst.Name, TypeKind = TypeKind.Enum, PrimitiveType = new EnumType {Bytes = primitive.Bytes, Signed = primitive.Signed}};
            return enumAst;
        }

        // private TypeDefinition VerifyCall(CallAst call, IFunction currentFunction, ScopeAst scope)
        private IType VerifyCall(CallAst call, IFunction currentFunction, ScopeAst scope)
        {
            // var arguments = new TypeDefinition[call.Arguments.Count];
            var argumentTypes = new IType[call.Arguments.Count];
            var argumentsError = false;

            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var argument = call.Arguments[i];
                var argumentType = VerifyExpression(argument, currentFunction, scope);
                if (argumentType == null && argument is not NullAst)
                {
                    argumentsError = true;
                }
                argumentTypes[i] = argumentType;
            }

            // var specifiedArguments = new Dictionary<string, TypeDefinition>();
            var specifiedArguments = new Dictionary<string, IType>();
            if (call.SpecifiedArguments != null)
            {
                foreach (var (name, argument) in call.SpecifiedArguments)
                {
                    var argumentType = VerifyExpression(argument, currentFunction, scope);
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
                        ErrorReporter.Report($"Undefined generic type '{PrintTypeDefinition(generic)}'", generic);
                        argumentsError = true;
                    }
                }
            }

            if (argumentsError)
            {
                TypeTable.Functions.TryGetValue(call.FunctionName, out var functions);
                _polymorphicFunctions.TryGetValue(call.FunctionName, out var polymorphicFunctions);
                if (functions == null)
                {
                    if (polymorphicFunctions == null)
                    {
                        ErrorReporter.Report($"Call to undefined function '{call.FunctionName}'", call);
                        return null;
                    }

                    if (polymorphicFunctions.Count == 1)
                    {
                        var calledFunction = polymorphicFunctions[0];
                        // return calledFunction.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) ? null : calledFunction.ReturnTypeDefinition;
                        return calledFunction.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) ? null : calledFunction.ReturnType;
                    }
                }
                else if (polymorphicFunctions == null && functions.Count == 1)
                {
                    // return functions[0].ReturnTypeDefinition;
                    return functions[0].ReturnType;
                }
                return null;
            }

            var function = DetermineCallingFunction(call, argumentTypes, specifiedArguments);

            if (function == null)
            {
                return null;
            }

            if (!function.Flags.HasFlag(FunctionFlags.Verified) && function != currentFunction)
            {
                VerifyFunction(function);
            }

            if (currentFunction != null && !currentFunction.Flags.HasFlag(FunctionFlags.CallsCompiler) && (function.Flags.HasFlag(FunctionFlags.Compiler) || function.Flags.HasFlag(FunctionFlags.CallsCompiler)))
            {
                currentFunction.Flags |= FunctionFlags.CallsCompiler;
            }

            call.Function = function;
            var argumentCount = function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params) ? function.Arguments.Count - 1 : function.Arguments.Count;

            // Verify call arguments match the types of the function arguments
            for (var i = 0; i < argumentCount; i++)
            {
                var functionArg = function.Arguments[i];
                if (call.SpecifiedArguments != null && call.SpecifiedArguments.TryGetValue(functionArg.Name, out var specifiedArgument))
                {
                    if (functionArg.TypeDefinition.PrimitiveType != null && specifiedArgument is ConstantAst constant)
                    {
                        VerifyConstant(constant, functionArg.TypeDefinition);
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
                    nullAst.TargetTypeDefinition = functionArg.TypeDefinition;
                    nullAst.TargetType = functionArg.Type;
                }
                else
                {
                    var argument = argumentTypes[i];
                    if (argument != null)
                    {
                        // TODO Figure this out, maybe add flag like IsType
                        if (functionArg.TypeDefinition.TypeKind == TypeKind.Type)
                        {
                            var typeIndex = new ConstantAst
                            {
                                TypeDefinition = new TypeDefinition {TypeKind = TypeKind.Integer, PrimitiveType = new IntegerType {Signed = true, Bytes = 4}}, Type = TypeTable.Types["s32"]
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
                                var type = TypeTable.Types[argument.GenericName];
                                typeIndex.Value = type.TypeIndex.ToString();
                            }
                            call.Arguments[i] = typeIndex;
                            argumentTypes[i] = typeIndex.Type;
                        }
                        else if (argument.PrimitiveType != null && call.Arguments[i] is ConstantAst constant)
                        {
                            VerifyConstant(constant, functionArg.TypeDefinition);
                        }
                    }
                }
            }

            // Verify varargs call arguments
            if (function.Flags.HasFlag(FunctionFlags.Params))
            {
                var paramsType = function.Arguments[argumentCount].TypeDefinition.Generics.FirstOrDefault();

                if (paramsType != null)
                {
                    for (var i = argumentCount; i < argumentTypes.Length; i++)
                    {
                        var argumentAst = call.Arguments[i];
                        if (argumentAst is NullAst nullAst)
                        {
                            nullAst.TargetTypeDefinition = paramsType;
                            nullAst.TargetType = function.ParamsElementType;
                        }
                        else
                        {
                            var argument = argumentTypes[i];
                            if (argument != null)
                            {
                                if (paramsType.TypeKind == TypeKind.Type)
                                {
                                    var typeIndex = new ConstantAst
                                    {
                                        TypeDefinition = new TypeDefinition {TypeKind = TypeKind.Integer, PrimitiveType = new IntegerType {Signed = true, Bytes = 4}}, Type = TypeTable.Types["s32"]
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
                                        var type = TypeTable.Types[argument.GenericName];
                                        typeIndex.Value = type.TypeIndex.ToString();
                                    }
                                    call.Arguments[i] = typeIndex;
                                    argumentTypes[i] = typeIndex.TypeDefinition;
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
            else if (function.Flags.HasFlag(FunctionFlags.Varargs))
            {
                for (var i = 0; i < argumentTypes.Length; i++)
                {
                    var argumentType = argumentTypes[i];
                    // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                    // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                    if (argumentType.TypeKind == TypeKind.Float && argumentType.Size == 4)
                    {
                        argumentTypes[i] = _float64Type;
                    }
                }
                var found = false;
                for (var index = 0; index < function.VarargsCallTypes.Count; index++)
                {
                    var callTypes = function.VarargsCallTypes[index];
                    if (callTypes.Length == argumentTypes.Length)
                    {
                        var callMatches = true;
                        for (var i = 0; i < callTypes.Length; i++)
                        {
                            // if (!TypeEquals(callTypes[i], argumentTypes[i], true))
                            if (callTypes[i] != argumentTypes[i])
                            {
                                callMatches = false;
                                break;
                            }
                        }

                        if (callMatches)
                        {
                            found = true;
                            call.ExternIndex = index;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    call.ExternIndex = function.VarargsCallTypes.Count;
                    _runner.InitVarargsFunction(function, argumentTypes);
                    function.VarargsCallTypes.Add(argumentTypes);
                }
            }

            // return function.ReturnTypeDefinition;
            return function.ReturnType;
        }

        private FunctionAst DetermineCallingFunction(CallAst call, TypeDefinition[] arguments, Dictionary<string, TypeDefinition> specifiedArguments)
        {
            if (TypeTable.Functions.TryGetValue(call.FunctionName, out var functions))
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    var match = true;
                    var callArgIndex = 0;
                    var functionArgCount = function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params) ? function.Arguments.Count - 1 : function.Arguments.Count;

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
                                    found = VerifyArgument(argument, specifiedArguments[name], functionArg.TypeDefinition, function.Flags.HasFlag(FunctionFlags.Extern));
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
                            if (!VerifyArgument(argumentAst, arguments[callArgIndex], functionArg.TypeDefinition, function.Flags.HasFlag(FunctionFlags.Extern)))
                            {
                                match = false;
                                break;
                            }
                            callArgIndex++;
                        }
                    }

                    if (match && function.Flags.HasFlag(FunctionFlags.Params))
                    {
                        var paramsType = function.Arguments[^1].TypeDefinition.Generics.FirstOrDefault();

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

                    if (match && (function.Flags.HasFlag(FunctionFlags.Varargs) || callArgIndex == call.Arguments.Count))
                    {
                        return function;
                    }
                }
            }

            if (_polymorphicFunctions.TryGetValue(call.FunctionName, out var polymorphicFunctions))
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
                    var functionArgCount = function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params) ? function.Arguments.Count - 1 : function.Arguments.Count;
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
                                        found = VerifyPolymorphicArgument(argument, specifiedArguments[name], functionArg.TypeDefinition, genericTypes);
                                    }
                                    else
                                    {
                                        found = VerifyArgument(argument, specifiedArguments[name], functionArg.TypeDefinition);
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
                                if (!VerifyPolymorphicArgument(argumentAst, arguments[callArgIndex], functionArg.TypeDefinition, genericTypes))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            else
                            {
                                if (!VerifyArgument(argumentAst, arguments[callArgIndex],functionArg.TypeDefinition))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            callArgIndex++;
                        }
                    }

                    if (match && function.Flags.HasFlag(FunctionFlags.Params))
                    {
                        var paramsArgument = function.Arguments[^1];
                        var paramsType = paramsArgument.TypeDefinition.Generics.FirstOrDefault();

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

                    if (match && genericTypes.All(t => t != null) && (function.Flags.HasFlag(FunctionFlags.Varargs) || callArgIndex == call.Arguments.Count))
                    {
                        var genericName = $"{function.Name}.{i}.{string.Join('.', genericTypes.Select(t => t.GenericName))}";
                        var name = $"{function.Name}<{string.Join(", ", genericTypes.Select(PrintTypeDefinition))}>";
                        call.FunctionName = genericName;

                        if (TypeTable.Functions.TryGetValue(genericName, out var implementations))
                        {
                            if (implementations.Count > 1)
                            {
                                ErrorReporter.Report($"Internal compiler error, multiple implementations of polymorphic function '{name}'", call);
                            }
                            return implementations[0];
                        }

                        var polymorphedFunction = _polymorpher.CreatePolymorphedFunction(function, name, genericTypes);
                        VerifyType(polymorphedFunction.ReturnTypeDefinition);
                        polymorphedFunction.ReturnType = TypeTable.GetType(polymorphedFunction.ReturnTypeDefinition);
                        polymorphedFunction.Arguments.ForEach(arg => {
                            VerifyType(arg.TypeDefinition, argument: true);

                            // TODO Make this better, roll into VerifyType
                            if (arg.Type == null)
                            {
                                arg.Type = TypeTable.GetType(arg.TypeDefinition);
                            }
                        });

                        TypeTable.AddFunction(genericName, polymorphedFunction);
                        VerifyFunction(polymorphedFunction);

                        return polymorphedFunction;
                    }
                }
            }

            if (functions == null && polymorphicFunctions == null)
            {
                ErrorReporter.Report($"Call to undefined function '{call.FunctionName}'", call);
            }
            else
            {
                ErrorReporter.Report($"No overload of function '{call.FunctionName}' found with given arguments", call);
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

        // private TypeDefinition VerifyExpressionType(ExpressionAst expression, IFunction currentFunction, ScopeAst scope)
        private IType VerifyExpressionType(ExpressionAst expression, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Get the type of the initial child
            expression.Type = VerifyExpression(expression.Children[0], currentFunction, scope, out _);
            if (expression.Type == null) return null;

            for (var i = 1; i < expression.Children.Count; i++)
            {
                // 2. Get the next operator and expression type
                var op = expression.Operators[i - 1];
                var next = expression.Children[i];
                if (next is NullAst nullAst)
                {
                    if (expression.Type.TypeKind != TypeKind.Pointer || (op != Operator.Equality && op != Operator.NotEqual))
                    {
                        ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and null", next);
                    }

                    nullAst.TargetType = expression.Type;
                    expression.Type = _boolType;
                    expression.ResultingTypes.Add(expression.Type);
                    continue;
                }

                var nextExpressionType = VerifyExpression(next, currentFunction, scope, out _);
                if (nextExpressionType == null) return null;

                // 3. Verify the operator and expression types are compatible and convert the expression type if necessary
                var type = expression.Type.TypeKind;
                var nextType = nextExpressionType.TypeKind;
                if ((type == TypeKind.Struct && nextType == TypeKind.Struct) ||
                    (type == TypeKind.String && nextType == TypeKind.String))
                {
                    if (TypeEquals(expression.TypeDefinition, nextExpressionType, true))
                    {
                        var overload = VerifyOperatorOverloadType(expression.TypeDefinition, op, currentFunction, expression.Children[i]);
                        if (overload != null)
                        {
                            expression.OperatorOverloads[i] = overload;
                            expression.TypeDefinition = overload.ReturnTypeDefinition;
                        }
                    }
                    else
                    {
                        ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
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
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                expression.TypeDefinition = new TypeDefinition {Name = "bool", TypeKind = TypeKind.Boolean};
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
                                if ((op != Operator.Equality && op != Operator.NotEqual) || !TypeEquals(expression.TypeDefinition, nextExpressionType))
                                {
                                    ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                }
                            }
                            else if (!(type == TypeKind.Integer || type == TypeKind.Float) &&
                                !(nextType == TypeKind.Integer || nextType == TypeKind.Float))
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            }
                            expression.TypeDefinition = new TypeDefinition {Name = "bool", TypeKind = TypeKind.Boolean};
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
                                    expression.TypeDefinition = nextExpressionType;
                                }
                            }
                            else if ((type == TypeKind.Integer || type == TypeKind.Float) &&
                                (nextType == TypeKind.Integer || nextType == TypeKind.Float))
                            {
                                // For integer operations, use the larger size and convert to signed if one type is signed
                                if (type == TypeKind.Integer && nextType == TypeKind.Integer)
                                {
                                    var currentIntegerType = expression.TypeDefinition.PrimitiveType;
                                    var nextIntegerType = nextExpressionType.PrimitiveType;
                                    if (currentIntegerType.Bytes == nextIntegerType.Bytes &&
                                        currentIntegerType.Signed == nextIntegerType.Signed)
                                        break;

                                    var integerType = new IntegerType
                                    {
                                        Bytes = currentIntegerType.Bytes > nextIntegerType.Bytes ? currentIntegerType.Bytes : nextIntegerType.Bytes,
                                        Signed = currentIntegerType.Signed || nextIntegerType.Signed
                                    };
                                    expression.TypeDefinition = new TypeDefinition
                                    {
                                        Name = $"{(integerType.Signed ? "s" : "u")}{integerType.Bytes * 8}",
                                        TypeKind = TypeKind.Integer, PrimitiveType = integerType
                                    };
                                }
                                // For floating point operations, convert to the larger size
                                else if (type == TypeKind.Float && nextType == TypeKind.Float)
                                {
                                    if (expression.TypeDefinition.PrimitiveType.Bytes < nextExpressionType.PrimitiveType.Bytes)
                                    {
                                        expression.TypeDefinition = nextExpressionType;
                                    }
                                }
                                // For an int lhs and float rhs, convert to the floating point type
                                // Note that float lhs and int rhs are covered since the floating point is already selected
                                else if (nextType == TypeKind.Float)
                                {
                                    expression.TypeDefinition = nextExpressionType;
                                }
                            }
                            else
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (type == TypeKind.Integer && nextType == TypeKind.Integer)
                            {
                                var currentIntegerType = expression.TypeDefinition.PrimitiveType;
                                var nextIntegerType = nextExpressionType.PrimitiveType;
                                if (currentIntegerType.Bytes == nextIntegerType.Bytes &&
                                    currentIntegerType.Signed == nextIntegerType.Signed)
                                    break;

                                var integerType = new IntegerType
                                {
                                    Bytes = currentIntegerType.Bytes > nextIntegerType.Bytes ? currentIntegerType.Bytes : nextIntegerType.Bytes,
                                    Signed = currentIntegerType.Signed || nextIntegerType.Signed
                                };
                                expression.TypeDefinition = new TypeDefinition
                                {
                                    Name = $"{(integerType.Signed ? "s" : "u")}{integerType.Bytes * 8}",
                                    TypeKind = TypeKind.Integer, PrimitiveType = integerType
                                };
                            }
                            else if (!(type == TypeKind.Boolean && nextType == TypeKind.Boolean))
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                if (nextType == TypeKind.Boolean || nextType == TypeKind.Integer)
                                {
                                    expression.TypeDefinition = nextExpressionType;
                                }
                                else if (!(type == TypeKind.Boolean || type == TypeKind.Integer))
                                {
                                    // If the type can't be determined, default to int
                                    expression.TypeDefinition = _s32Type;
                                }
                            }
                            break;
                        case Operator.ShiftLeft:
                        case Operator.ShiftRight:
                        case Operator.RotateLeft:
                        case Operator.RotateRight:
                            if (type != TypeKind.Integer || nextType != TypeKind.Integer)
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{PrintTypeDefinition(expression.TypeDefinition)}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                if (type != TypeKind.Integer)
                                {
                                    if (nextType == TypeKind.Integer)
                                    {
                                        expression.TypeDefinition = nextExpressionType;
                                    }
                                    else
                                    {
                                        // If the type can't be determined, default to int
                                        expression.TypeDefinition = _s32Type;
                                    }
                                }
                            }
                            break;
                    }
                }
                expression.Type = TypeTable.GetType(expression.TypeDefinition);
                expression.ResultingTypes.Add(expression.Type);
                expression.ResultingTypeDefinitions.Add(expression.TypeDefinition);
            }
            // return expression.TypeDefinition;
            return expression.Type;
        }

        // private TypeDefinition VerifyIndexType(IndexAst index, IFunction currentFunction, ScopeAst scope)
        private IType VerifyIndexType(IndexAst index, IFunction currentFunction, ScopeAst scope)
        {
            if (!GetScopeIdentifier(scope, index.Name, out var identifier))
            {
                ErrorReporter.Report($"Variable '{index.Name}' not defined", index);
                return null;
            }
            if (identifier is not DeclarationAst declaration)
            {
                ErrorReporter.Report($"Identifier '{index.Name}' is not a variable", index);
                return null;
            }
            return VerifyIndex(index, declaration.Type, currentFunction, scope, out _);
        }

        // private StructAst _stringStruct;

        // private TypeDefinition VerifyIndex(IndexAst index, TypeDefinition typeDef, IFunction currentFunction, ScopeAst scope, out bool overloaded)
        private IType VerifyIndex(IndexAst index, IType type, IFunction currentFunction, ScopeAst scope, out bool overloaded)
        {
            // 1. Verify the variable is an array or the operator overload exists
            overloaded = false;
            // var type = VerifyType(typeDef);
            // TypeDefinition elementType = null;
            IType elementType = null;
            switch (type?.TypeKind)
            {
                case TypeKind.Error:
                    break;
                case TypeKind.Struct:
                    index.CallsOverload = true;
                    overloaded = true;
                    index.Overload = VerifyOperatorOverloadType(typeDef, Operator.Subscript, currentFunction, index);
                    // elementType = index.Overload.ReturnTypeDefinition;
                    elementType = index.Overload.ReturnType;
                    break;
                case TypeKind.Array:
                case TypeKind.CArray:
                case TypeKind.Params:
                case TypeKind.Pointer:
                    elementType = typeDef.Generics.FirstOrDefault();
                    if (elementType == null)
                    {
                        ErrorReporter.Report("Unable to determine element type of the Array", index);
                    }
                    break;
                case TypeKind.String:
                    // _stringStruct ??= (StructAst)TypeTable.Types["string"];
                    // elementType = _stringStruct.Fields[1].TypeDefinition.Generics[0];
                    elementType = _u8Type;
                    break;
                default:
                    ErrorReporter.Report($"Cannot index type '{type.Name}'", index);
                    break;
            }

            // 2. Verify the count expression is an integer
            var indexValue = VerifyExpression(index.Index, currentFunction, scope, out var isType);
            // var indexType = VerifyType(indexValue);
            if (indexValue.TypeKind != TypeKind.Integer && !isType)
            {
                ErrorReporter.Report($"Expected index to be type 'int', but got '{indexValue.Name}'", index);
            }

            return elementType;
        }

        private OperatorOverloadAst VerifyOperatorOverloadType(TypeDefinition type, Operator op, IFunction currentFunction, IAst ast)
        {
            if (_operatorOverloads.TryGetValue(type.GenericName, out var overloads) && overloads.TryGetValue(op, out var overload))
            {
                if (!overload.Flags.HasFlag(FunctionFlags.Verified) && overload != currentFunction)
                {
                    VerifyOperatorOverload(overload);
                }
                return overload;
            }
            else if (_polymorphicOperatorOverloads.TryGetValue(type.Name, out var polymorphicOverloads) && polymorphicOverloads.TryGetValue(op, out var polymorphicOverload))
            {
                var polymorphedOverload = _polymorpher.CreatePolymorphedOperatorOverload(polymorphicOverload, type.Generics.ToArray());
                if (overloads == null)
                {
                    _operatorOverloads[type.GenericName] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
                }
                overloads[op] = polymorphedOverload;
                VerifyType(polymorphedOverload.ReturnTypeDefinition);
                polymorphedOverload.Arguments.ForEach(arg => {
                    VerifyType(arg.TypeDefinition, argument: true);

                    // TODO Make this better, roll into VerifyType
                    if (arg.Type == null)
                    {
                        arg.Type = TypeTable.GetType(arg.TypeDefinition);
                    }
                });

                VerifyOperatorOverload(polymorphedOverload);
                return polymorphedOverload;
            }
            else
            {
                ErrorReporter.Report($"Type '{PrintTypeDefinition(type)}' does not contain an overload for operator '{PrintOperator(op)}'", ast);
                return null;
            }
        }

        private static bool TypeEquals(TypeDefinition a, TypeDefinition b, bool checkPrimitives = false, int depth = 0)
        {
            if (a == null || b == null) return false;

            // Check by primitive type
            switch (a.PrimitiveType)
            {
                case IntegerType aInt:
                    if (b.PrimitiveType is IntegerType bInt)
                    {
                        if (!checkPrimitives) return true;
                        return aInt.Bytes == bInt.Bytes && aInt.Signed == bInt.Signed;
                    }
                    return false;
                case FloatType aFloat:
                    if (b.PrimitiveType is FloatType bFloat)
                    {
                        if (!checkPrimitives) return true;
                        return aFloat.Bytes == bFloat.Bytes;
                    }
                    return false;
                case null:
                    if (b.PrimitiveType != null) return false;
                    break;
            }

            // Check by name
            if (a.Name != b.Name) return false;
            if (a.Generics.Count != b.Generics.Count) return false;

            // Check all generics are equal
            if (a.Name == "*")
            {
                var ai = a.Generics[0];
                var bi = b.Generics[0];
                if (depth == 0 && (ai.Name == "void" || bi.Name == "void")) return true;
                if (!TypeEquals(ai, bi, true, depth + 1)) return false;
            }
            else
            {
                for (var i = 0; i < a.Generics.Count; i++)
                {
                    var ai = a.Generics[i];
                    var bi = b.Generics[i];
                    if (!TypeEquals(ai, bi, checkPrimitives, depth + 1)) return false;
                }
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
                    ErrorReporter.Report("Generic type cannot have additional generic types", typeDef);
                }
                return TypeKind.Generic;
            }

            if (typeDef.Count != null && typeDef.Name != "Array" && typeDef.Name != "CArray")
            {
                ErrorReporter.Report($"Type '{PrintTypeDefinition(typeDef)}' cannot have a count", typeDef);
                return TypeKind.Error;
            }

            var hasGenerics = typeDef.Generics.Any();

            switch (typeDef.PrimitiveType)
            {
                case IntegerType:
                    if (hasGenerics)
                    {
                        ErrorReporter.Report($"Type '{typeDef.Name}' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                        return TypeKind.Error;
                    }
                    typeDef.TypeKind = TypeKind.Integer;
                    return TypeKind.Integer;
                case FloatType:
                    if (hasGenerics)
                    {
                        ErrorReporter.Report($"Type '{typeDef.Name}' cannot have generics", typeDef);
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
                        ErrorReporter.Report("Type 'bool' cannot have generics", typeDef);
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
                        ErrorReporter.Report("Type 'string' cannot have generics", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = TypeKind.String;
                    }
                    break;
                case "Array":
                    if (typeDef.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"Type 'Array' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (!VerifyArray(typeDef, depth, argument, out var hasGenericTypes))
                    {
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = hasGenericTypes ? TypeKind.Generic : TypeKind.Array;
                    }
                    break;
                case "CArray":
                    if (typeDef.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"Type 'CArray' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        var elementType = typeDef.Generics[0];
                        var elementTypeKind = VerifyType(elementType, depth + 1, argument);
                        if (elementTypeKind == TypeKind.Error)
                        {
                            typeDef.TypeKind = TypeKind.Error;
                        }
                        else if (elementTypeKind == TypeKind.Generic)
                        {
                            typeDef.TypeKind = TypeKind.Generic;
                        }
                        else
                        {
                            if (!TypeTable.Types.ContainsKey(typeDef.GenericName))
                            {
                                var type = TypeTable.Types[elementType.GenericName];
                                var arrayType = new ArrayType {Name = PrintTypeDefinition(typeDef), Size = type.Size, ElementTypeDefinition = elementType, ElementType = type};
                                TypeTable.Add(typeDef.GenericName, arrayType);
                                TypeTable.CreateTypeInfo(arrayType);
                            }
                            typeDef.TypeKind = TypeKind.CArray;
                        }
                    }
                    break;
                case "void":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'void' cannot have generics", typeDef);
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
                        ErrorReporter.Report($"pointer type should have reference to 1 type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (TypeTable.Types.ContainsKey(typeDef.GenericName))
                    {
                        typeDef.TypeKind = TypeKind.Pointer;
                        VerifyType(typeDef.Generics[0], depth + 1);
                    }
                    else
                    {
                        var type = typeDef.Generics[0];
                        var pointerType = VerifyType(type, depth + 1);
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
                            // There are some cases where the pointed to type is a struct that contains a field for the pointer type
                            // To account for this, the type table needs to be checked for again for the type
                            if (!TypeTable.Types.ContainsKey(typeDef.GenericName))
                            {
                                var pointer = new PrimitiveAst {Name = PrintTypeDefinition(typeDef), TypeKind = TypeKind.Pointer, Size = 8, PointerTypeDefinition = type, PointerType = TypeTable.GetType(type)};
                                TypeTable.Add(typeDef.GenericName, pointer);
                                TypeTable.CreateTypeInfo(pointer);
                            }
                            typeDef.TypeKind = TypeKind.Pointer;
                        }
                    }
                    break;
                case "...":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'varargs' cannot have generics", typeDef);
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
                        ErrorReporter.Report($"Params can only be used in function arguments", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (depth != 0)
                    {
                        ErrorReporter.Report($"Params can only be declared as a top level type, such as 'Params<int>'", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else if (typeDef.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"Type 'Params' should have 1 generic type, but got {typeDef.Generics.Count}", typeDef);
                        typeDef.TypeKind = TypeKind.Error;
                    }
                    else
                    {
                        typeDef.TypeKind = VerifyArray(typeDef, depth, argument, out _) ? TypeKind.Params : TypeKind.Error;
                    }
                    break;
                case "Type":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'Type' cannot have generics", typeDef);
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
                        if (TypeTable.Types.ContainsKey(genericName))
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
                                ErrorReporter.Report($"No polymorphic structs of type '{typeDef.Name}'", typeDef);
                                typeDef.TypeKind = TypeKind.Error;
                            }
                            else if (structDef.Generics.Count != typeDef.Generics.Count)
                            {
                                ErrorReporter.Report($"Expected type '{typeDef.Name}' to have {structDef.Generics.Count} generic(s), but got {typeDef.Generics.Count}", typeDef);
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
                                var polyStruct = _polymorpher.CreatePolymorphedStruct(structDef, PrintTypeDefinition(typeDef), TypeKind.Struct, generics);
                                TypeTable.Add(genericName, polyStruct);
                                VerifyStruct(polyStruct);
                                typeDef.TypeKind = TypeKind.Struct;
                            }
                        }
                    }
                    else if (TypeTable.Types.TryGetValue(typeDef.Name, out var type))
                    {
                        switch (type)
                        {
                            case StructAst structAst:
                                if (!structAst.Verifying)
                                {
                                    VerifyStruct(structAst);
                                }
                                typeDef.TypeKind = TypeKind.Struct;
                                break;
                            case EnumAst enumAst:
                                var primitive = enumAst.BaseTypeDefinition.PrimitiveType;
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

        private bool VerifyArray(TypeDefinition typeDef, int depth, bool argument, out bool hasGenerics)
        {
            hasGenerics = false;
            var elementType = typeDef.Generics[0];
            var genericType = VerifyType(elementType, depth + 1, argument);
            if (genericType == TypeKind.Error)
            {
                return false;
            }
            else if (genericType == TypeKind.Generic)
            {
                hasGenerics = true;
                return true;
            }

            var genericName = $"Array.{elementType.GenericName}";
            if (TypeTable.Types.ContainsKey(genericName))
            {
                return true;
            }
            if (!_polymorphicStructs.TryGetValue("Array", out var structDef))
            {
                ErrorReporter.Report($"No polymorphic structs with name '{typeDef.Name}'", typeDef);
                return false;
            }

            var arrayStruct = _polymorpher.CreatePolymorphedStruct(structDef, $"Array<{PrintTypeDefinition(elementType)}>", TypeKind.Array, elementType);
            TypeTable.Add(genericName, arrayStruct);
            VerifyStruct(arrayStruct);
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
    }
}
