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

        private StructAst _baseArrayType;
        private IType _rawStringType;

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
            TypeTable.VoidType = AddPrimitive("void", TypeKind.Void, 1);
            TypeTable.BoolType = AddPrimitive("bool", TypeKind.Boolean, 1);
            TypeTable.S8Type = AddPrimitive("s8", TypeKind.Integer, 1, true);
            TypeTable.U8Type = AddPrimitive("u8", TypeKind.Integer, 1);
            TypeTable.S16Type = AddPrimitive("s16", TypeKind.Integer, 2, true);
            TypeTable.U16Type = AddPrimitive("u16", TypeKind.Integer, 2);
            TypeTable.S32Type = AddPrimitive("s32", TypeKind.Integer, 4, true);
            TypeTable.U32Type = AddPrimitive("u32", TypeKind.Integer, 4);
            TypeTable.S64Type = AddPrimitive("s64", TypeKind.Integer, 8, true);
            TypeTable.U64Type = AddPrimitive("u64", TypeKind.Integer, 8);
            AddPrimitive("float", TypeKind.Float, 4, true);
            TypeTable.Float64Type = AddPrimitive("float64", TypeKind.Float, 8, true);
            TypeTable.TypeType = AddPrimitive("Type", TypeKind.Type, 4, true);

            // _irBuilder.Init();

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
                            if (structAst.Generics != null)
                            {
                                if (structAst.Name == "Array")
                                {
                                    _baseArrayType = structAst;
                                }
                                if (_polymorphicStructs.ContainsKey(structAst.Name))
                                {
                                    ErrorReporter.Report($"Multiple definitions of polymorphic struct '{structAst.Name}'", structAst);
                                }
                                _polymorphicStructs[structAst.Name] = structAst;
                                asts.RemoveAt(i--);
                            }
                            else
                            {
                                structAst.BackendName = structAst.Name;
                                if (!TypeTable.Add(structAst.Name, structAst))
                                {
                                    ErrorReporter.Report($"Multiple definitions of struct '{structAst.Name}'", structAst);
                                }

                                if (structAst.Name == "string")
                                {
                                    TypeTable.StringType = structAst;
                                    structAst.TypeKind = TypeKind.String;
                                    VerifyStruct(structAst);
                                    asts.RemoveAt(i--);
                                }
                                else if (structAst.Name == "Any")
                                {
                                    TypeTable.AnyType = structAst;
                                    structAst.TypeKind = TypeKind.Any;
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

            // 6. Execute any other compiler directives
            foreach (var ast in asts)
            {
                switch (ast)
                {
                    case CompilerDirectiveAst directive:
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
                        break;
                }
            }

            if (!mainDefined)
            {
                ErrorReporter.Report("'main' function of the program is not defined");
            }

            // 7. Verify operator overload bodies
            foreach (var overloads in _operatorOverloads.Values)
            {
                foreach (var overload in overloads.Values)
                {
                    if (overload.Flags.HasFlag(FunctionFlags.Verified)) continue;
                    VerifyOperatorOverload(overload);
                }
            }

            // 8. Verify function bodies
            foreach (var name in functionNames)
            {
                var functions = TypeTable.Functions[name];
                foreach (var function in functions)
                {
                    if (function.Flags.HasFlag(FunctionFlags.Verified)) continue;
                    VerifyFunction(function);
                }
            }
        }

        private PrimitiveAst AddPrimitive(string name, TypeKind typeKind, uint size = 0, bool signed = false)
        {
            var primitiveAst = new PrimitiveAst {Name = name, BackendName = name, TypeKind = typeKind, Size = size, Signed = signed};
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
                enumAst.BaseType = TypeTable.S32Type;
            }
            else
            {
                var baseType = VerifyType(enumAst.BaseTypeDefinition, _globalScope);
                if (baseType?.TypeKind != TypeKind.Integer)
                {
                    ErrorReporter.Report($"Base type of enum must be an integer, but got '{PrintTypeDefinition(enumAst.BaseTypeDefinition)}'", enumAst.BaseTypeDefinition);
                    enumAst.BaseType = TypeTable.S32Type;
                    enumAst.Size = 4;
                }
                else
                {
                    enumAst.BaseType = (PrimitiveAst)baseType;
                    enumAst.Size = enumAst.BaseType.Size;
                }
            }

            // 2. Verify enums don't have repeated values
            var valueNames = new HashSet<string>();
            var values = new HashSet<int>();

            var lowestAllowedValue = enumAst.BaseType.Signed ? -Math.Pow(2, 8 * enumAst.Size - 1) : 0;
            var largestAllowedValue = enumAst.BaseType.Signed ? Math.Pow(2, 8 * enumAst.Size - 1) - 1 : Math.Pow(2, 8 * enumAst.Size) - 1;

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
            // Verify struct fields have valid types
            var fieldNames = new HashSet<string>();
            structAst.Verifying = true;
            var i = 0;

            if (structAst.BaseTypeDefinition != null)
            {
                var baseType = VerifyType(structAst.BaseTypeDefinition, _globalScope, out var isGeneric, out var isVarargs, out var isParams);

                if (isVarargs || isParams || isGeneric)
                {
                    ErrorReporter.Report($"Struct base type must be a struct", structAst.BaseTypeDefinition);
                }
                else if (baseType == null)
                {
                    ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(structAst.BaseTypeDefinition)}' as the base type of struct '{structAst.Name}'", structAst.BaseTypeDefinition);
                }
                else if (baseType is not StructAst baseTypeStruct)
                {
                    ErrorReporter.Report($"Base type '{PrintTypeDefinition(structAst.BaseTypeDefinition)}' of struct '{structAst.Name}' is not a struct", structAst.BaseTypeDefinition);
                }
                else
                {
                    // Copy fields from the base struct into the new struct
                    foreach (var field in baseTypeStruct.Fields)
                    {
                        fieldNames.Add(field.Name);
                        structAst.Fields.Insert(i++, field);
                    }
                    structAst.Size = baseTypeStruct.Size;
                    structAst.BaseStruct = baseTypeStruct;
                }
            }

            for (; i < structAst.Fields.Count; i++)
            {
                var structField = structAst.Fields[i];
                // Check if the field has been previously defined
                if (!fieldNames.Add(structField.Name))
                {
                    ErrorReporter.Report($"Struct '{structAst.Name}' already contains field '{structField.Name}'", structField);
                }

                if (structField.TypeDefinition != null)
                {
                    structField.Type = VerifyType(structField.TypeDefinition, _globalScope, out var isGeneric, out var isVarargs, out var isParams);

                    if (isVarargs || isParams)
                    {
                        ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be varargs or Params", structField.TypeDefinition);
                    }
                    else if (structField.Type == null)
                    {
                        ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(structField.TypeDefinition)}' in struct field '{structAst.Name}.{structField.Name}'", structField.TypeDefinition);
                    }
                    else if (structField.Type.TypeKind == TypeKind.Void)
                    {
                        ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be assigned type 'void'", structField.TypeDefinition);
                    }
                    else
                    {
                        if (structField.Type.TypeKind == TypeKind.Array || structField.Type.TypeKind == TypeKind.CArray)
                        {
                            var elementType = structField.TypeDefinition.Generics[0];
                            structField.ArrayElementType = TypeTable.GetType(elementType);
                        }
                    }

                    if (structField.Value != null)
                    {
                        if (structField.Value is NullAst nullAst)
                        {
                            if (structField.Type != null && structField.Type.TypeKind != TypeKind.Pointer)
                            {
                                ErrorReporter.Report("Cannot assign null to non-pointer type", structField.Value);
                            }

                            nullAst.TargetType = structField.Type;
                        }
                        else
                        {
                            var valueType = VerifyConstantExpression(structField.Value, null, _globalScope, out var isConstant, out _);

                            // Verify the type is correct
                            if (valueType != null)
                            {
                                if (!TypeEquals(structField.Type, valueType))
                                {
                                    ErrorReporter.Report($"Expected struct field value to be type '{PrintTypeDefinition(structField.TypeDefinition)}', but got '{valueType.Name}'", structField.Value);
                                }
                                else if (!isConstant)
                                {
                                    ErrorReporter.Report("Default values in structs must be constant", structField.Value);
                                }
                                else
                                {
                                    VerifyConstantIfNecessary(structField.Value, structField.Type);
                                }
                            }
                        }
                    }
                    else if (structField.Assignments != null)
                    {
                        if (structField.Type != null && structField.Type.TypeKind != TypeKind.Struct && structField.Type.TypeKind != TypeKind.String)
                        {
                            ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(structField.TypeDefinition)}'", structField.TypeDefinition);
                        }
                        // Catch circular references
                        else if (structField.Type != null && structField.Type != structAst)
                        {
                            var structDef = structField.Type as StructAst;
                            if (structDef != null && !structDef.Verified)
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
                                    if (!TypeEquals(field.Type, valueType))
                                    {
                                        ErrorReporter.Report($"Expected field value to be type '{PrintTypeDefinition(field.TypeDefinition)}', but got '{valueType.Name}'", assignment.Value);
                                    }
                                    else if (!isConstant)
                                    {
                                        ErrorReporter.Report("Default values in structs should be constant", assignment.Value);
                                    }
                                    else
                                    {
                                        VerifyConstantIfNecessary(assignment.Value, field.Type);
                                    }
                                }
                            }
                        }
                    }
                    else if (structField.ArrayValues != null)
                    {
                        if (structField.Type != null && structField.Type.TypeKind != TypeKind.Array && structField.Type.TypeKind != TypeKind.CArray)
                        {
                            ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(structField.TypeDefinition)}'", structField.TypeDefinition);
                        }
                        else
                        {
                            structField.TypeDefinition.ConstCount = (uint)structField.ArrayValues.Count;
                            var elementType = structField.ArrayElementType;
                            foreach (var value in structField.ArrayValues)
                            {
                                var valueType = VerifyConstantExpression(value, null, _globalScope, out var isConstant, out _);
                                if (valueType != null)
                                {
                                    if (!TypeEquals(elementType, valueType))
                                    {
                                        ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                                    }
                                    else if (!isConstant)
                                    {
                                        ErrorReporter.Report("Default values in structs array initializers should be constant", value);
                                    }
                                    else
                                    {
                                        VerifyConstantIfNecessary(value, elementType);
                                    }
                                }
                            }
                        }
                    }

                    // Check type count
                    if (structField.Type?.TypeKind == TypeKind.Array && structField.TypeDefinition.Count != null)
                    {
                        // Verify the count is a constant
                        var countType = VerifyConstantExpression(structField.TypeDefinition.Count, null, _globalScope, out var isConstant, out var arrayLength);

                        if (countType?.TypeKind != TypeKind.Integer || !isConstant || arrayLength < 0)
                        {
                            ErrorReporter.Report($"Expected size of '{structAst.Name}.{structField.Name}' to be a constant, positive integer", structField.TypeDefinition.Count);
                        }
                        else
                        {
                            structField.TypeDefinition.ConstCount = arrayLength;
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
                            structField.Type = valueType;
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
                if (structAst == structField.Type)
                {
                    ErrorReporter.Report($"Struct '{structAst.Name}' contains circular reference in field '{structField.Name}'", structField);
                }

                // Set the size and offset
                if (structField.Type != null)
                {
                    if (structField.Type is StructAst fieldStruct)
                    {
                        if (!fieldStruct.Verified && fieldStruct != structAst)
                        {
                            VerifyStruct(fieldStruct);
                        }
                    }
                    structField.Type = structField.Type;
                    structField.Offset = structAst.Size;
                    structField.Size = structField.Type.Size;
                    structAst.Size += structField.Type.Size;
                }
            }

            TypeTable.CreateTypeInfo(structAst);
            structAst.Verified = true;
        }

        private void VerifyFunctionDefinition(FunctionAst function, HashSet<string> functionNames, bool main)
        {
            // 1. Verify the return type of the function is valid
            if (function.ReturnTypeDefinition == null)
            {
                function.ReturnType = TypeTable.VoidType;
            }
            else
            {
                function.ReturnType = VerifyType(function.ReturnTypeDefinition, _globalScope, out var isGeneric, out var isVarargs, out var isParams);
                if (isVarargs || isParams)
                {
                    ErrorReporter.Report($"Return type of function '{function.Name}' cannot be varargs or Params", function.ReturnTypeDefinition);
                }
                else if (function.ReturnType == null && !isGeneric)
                {
                    ErrorReporter.Report($"Return type '{PrintTypeDefinition(function.ReturnTypeDefinition)}' of function '{function.Name}' is not defined", function.ReturnTypeDefinition);
                }
                else if (function.ReturnType?.TypeKind == TypeKind.Array && function.ReturnTypeDefinition.Count != null)
                {
                    ErrorReporter.Report($"Size of Array does not need to be specified for return type of function '{function.Name}'", function.ReturnTypeDefinition.Count);
                }
            }

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
                argument.Type = VerifyType(argument.TypeDefinition, _globalScope, out var isGeneric, out var isVarargs, out var isParams, allowParams: true);

                if (isVarargs)
                {
                    if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
                    {
                        ErrorReporter.Report($"Function '{function.Name}' cannot have multiple varargs", argument.TypeDefinition);
                    }
                    function.Flags |= FunctionFlags.Varargs;
                    function.VarargsCallTypes = new List<IType[]>();
                }
                else if (isParams)
                {
                    if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
                    {
                        ErrorReporter.Report($"Function '{function.Name}' cannot have multiple varargs", argument.TypeDefinition);
                    }
                    function.Flags |= FunctionFlags.Params;
                    if (!isGeneric)
                    {
                        var paramsArrayType = (StructAst)argument.Type;
                        function.ParamsElementType = paramsArrayType.GenericTypes[0];
                    }
                }
                else if (argument.Type == null && !isGeneric)
                {
                    ErrorReporter.Report($"Type '{PrintTypeDefinition(argument.TypeDefinition)}' of argument '{argument.Name}' in function '{function.Name}' is not defined", argument.TypeDefinition);
                }
                else
                {
                    if (function.Flags.HasFlag(FunctionFlags.Varargs))
                    {
                        ErrorReporter.Report($"Cannot declare argument '{argument.Name}' following varargs", argument);
                    }
                    else if (function.Flags.HasFlag(FunctionFlags.Params))
                    {
                        ErrorReporter.Report($"Cannot declare argument '{argument.Name}' following params", argument);
                    }
                    else if (function.Flags.HasFlag(FunctionFlags.Extern) && argument.Type?.TypeKind == TypeKind.String)
                    {
                        argument.Type = _rawStringType ??= TypeTable.Types["*.u8"];
                    }
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
                        else if (argument.Type != null && !TypeEquals(argument.Type, defaultType))
                        {
                            ErrorReporter.Report($"Type of argument '{argument.Name}' in function '{function.Name}' is '{argument.Type.Name}', but default value is type '{defaultType.Name}'", argument.Value);
                        }
                        else
                        {
                            VerifyConstantIfNecessary(argument.Value, argument.Type);
                        }
                    }
                    else if (isConstant && argument.Type?.TypeKind != TypeKind.Pointer)
                    {
                        ErrorReporter.Report($"Type of argument '{argument.Name}' in function '{function.Name}' is '{PrintTypeDefinition(argument.TypeDefinition)}', but default value is 'null'", argument.Value);
                    }
                }
            }

            // 3. Verify main function return type and arguments
            if (main)
            {
                if (function.ReturnType?.TypeKind != TypeKind.Void)
                {
                    ErrorReporter.Report("The main function should return type 'void'", function);
                }

                if (function.Arguments.Count != 0)
                {
                    ErrorReporter.Report("The main function should have 0 arguments", function);
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
                var functions = TypeTable.AddFunction(function.Name, function);
                if (functions.Count > 1)
                {
                    if (function.Flags.HasFlag(FunctionFlags.Extern))
                    {
                        ErrorReporter.Report($"Multiple definitions of extern function '{function.Name}'", function);
                    }
                    else if (OverloadExistsForFunction(function, functions, false))
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
                        if (!TypeDefinitionEquals(currentFunction.Arguments[i].TypeDefinition, existingFunction.Arguments[i].TypeDefinition))
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
                var targetType = VerifyType(overload.Type, _globalScope, out var isGeneric, out var isVarargs, out var isParams);
                if (isVarargs || isParams)
                {
                    ErrorReporter.Report($"Cannot overload operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}'", overload.Type);
                }
                else
                {
                    switch (targetType?.TypeKind)
                    {
                        case TypeKind.Struct:
                        case TypeKind.String when overload.Operator != Operator.Subscript:
                        case null:
                            break;
                        default:
                            ErrorReporter.Report($"Cannot overload operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}'", overload.Type);
                            break;
                    }
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
                argument.Type = VerifyType(argument.TypeDefinition, _globalScope);
                if (i > 0)
                {
                    if (overload.Operator == Operator.Subscript)
                    {
                        if (argument.Type?.TypeKind != TypeKind.Integer)
                        {
                            ErrorReporter.Report($"Expected second argument of overload of operator '{PrintOperator(overload.Operator)}' to be an integer, but got '{PrintTypeDefinition(argument.TypeDefinition)}'", argument.TypeDefinition);
                        }
                    }
                    else if (!TypeDefinitionEquals(overload.Type, argument.TypeDefinition))
                    {
                        ErrorReporter.Report($"Expected overload of operator '{PrintOperator(overload.Operator)}' argument type to be '{PrintTypeDefinition(overload.Type)}', but got '{PrintTypeDefinition(argument.TypeDefinition)}'", argument.TypeDefinition);
                    }
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
                    if (function.ReturnType?.TypeKind != TypeKind.Void)
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
            overload.ReturnType = VerifyType(overload.ReturnTypeDefinition, _globalScope);

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
            var returnValueType = VerifyExpression(returnAst.Value, currentFunction, scope);

            // Handle void case since it's the easiest to interpret
            if (currentFunction.ReturnType?.TypeKind == TypeKind.Void)
            {
                if (returnAst.Value != null)
                {
                    ErrorReporter.Report("Function return should be void", returnAst);
                }
            }
            else if (currentFunction.ReturnType != null)
            {
                if (returnValueType == null)
                {
                    if (returnAst.Value is NullAst nullAst && currentFunction.ReturnType.TypeKind == TypeKind.Pointer)
                    {
                        nullAst.TargetType = currentFunction.ReturnType;
                    }
                    else
                    {
                        ErrorReporter.Report($"Expected to return type '{currentFunction.ReturnType.Name}'", returnAst);
                    }
                }
                else
                {
                    if (!TypeEquals(currentFunction.ReturnType, returnValueType))
                    {
                        ErrorReporter.Report($"Expected to return type '{currentFunction.ReturnType.Name}', but returned type '{returnValueType.Name}'", returnAst.Value);
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

            if (declaration.TypeDefinition != null)
            {
                declaration.Type = VerifyType(declaration.TypeDefinition, _globalScope, out _, out var isVarargs, out var isParams);
                if (isVarargs || isParams)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be varargs or Params", declaration.TypeDefinition);
                }
                else if (declaration.Type == null)
                {
                    ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else if (declaration.Type.TypeKind == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
                }
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
                    if (declaration.Type != null && declaration.Type.TypeKind != TypeKind.Pointer)
                    {
                        ErrorReporter.Report("Cannot assign null to non-pointer type", declaration.Value);
                    }

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
                    if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Struct && declaration.Type.TypeKind != TypeKind.String))
                    {
                        ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                        return;
                    }

                    var structDef = declaration.Type as StructAst;
                    var fields = structDef.Fields.ToDictionary(_ => _.Name);
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
                            if (!TypeEquals(field.Type, valueType))
                            {
                                ErrorReporter.Report($"Expected field value to be type '{PrintTypeDefinition(field.TypeDefinition)}', but got '{valueType.Name}'", assignment.Value);
                            }
                            else if (!isConstant)
                            {
                                ErrorReporter.Report($"Global variables can only be initialized with constant values", assignment.Value);
                            }
                            else
                            {
                                VerifyConstantIfNecessary(assignment.Value, field.Type);
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
                    if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Array && declaration.Type.TypeKind != TypeKind.CArray))
                    {
                        ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else
                    {
                        declaration.TypeDefinition.ConstCount = (uint)declaration.ArrayValues.Count;
                        var elementType = declaration.ArrayElementType = TypeTable.GetType(declaration.TypeDefinition.Generics[0]);
                        foreach (var value in declaration.ArrayValues)
                        {
                            var valueType = VerifyConstantExpression(value, null, _globalScope, out var isConstant, out _);
                            if (valueType != null)
                            {
                                if (!TypeEquals(elementType, valueType))
                                {
                                    ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                                }
                                else if (!isConstant)
                                {
                                    ErrorReporter.Report($"Global variables can only be initialized with constant values", value);
                                }
                                else
                                {
                                    VerifyConstantIfNecessary(value, elementType);
                                }
                            }
                        }
                    }
                }
            }
            // 6. Verify compiler constants
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
                }
            }

            // 7. Verify the type definition count if necessary
            if (declaration.Type?.TypeKind == TypeKind.Array && declaration.TypeDefinition.Count != null)
            {
                var countType = VerifyConstantExpression(declaration.TypeDefinition.Count, null, _globalScope, out var isConstant, out var arrayLength);

                if (countType?.TypeKind != TypeKind.Integer || !isConstant || arrayLength < 0)
                {
                    ErrorReporter.Report($"Expected Array length of global variable '{declaration.Name}' to be a constant, positive integer", declaration.TypeDefinition.Count);
                }
                else
                {
                    declaration.TypeDefinition.ConstCount = arrayLength;
                }
            }

            // 8. Verify constant values
            if (declaration.Constant)
            {
                if (declaration.Value == null)
                {
                    ErrorReporter.Report($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
                }
            }

            if (!ErrorReporter.Errors.Any())
            {
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
                if (valueType?.TypeKind == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                }
                declaration.Type = valueType;
            }
            else
            {
                // Verify the type is correct
                if (valueType != null)
                {
                    if (!TypeEquals(declaration.Type, valueType))
                    {
                        ErrorReporter.Report($"Expected declaration value to be type '{declaration.Name}', but got '{valueType.Name}'", declaration.Value);
                    }
                    else
                    {
                        VerifyConstantIfNecessary(declaration.Value, declaration.Type);
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

            if (declaration.TypeDefinition != null)
            {
                declaration.Type = VerifyType(declaration.TypeDefinition, scope, out _, out var isVarargs, out var isParams, initialArrayLength: declaration.ArrayValues?.Count);
                if (isVarargs || isParams)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be varargs or Params", declaration.TypeDefinition);
                }
                else if (declaration.Type == null)
                {
                    ErrorReporter.Report($"Undefined type in declaration '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else if (declaration.Type.TypeKind == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
                }
                else if (declaration.Type.TypeKind == TypeKind.Array)
                {
                    var arrayStruct = (StructAst)declaration.Type;
                    declaration.ArrayElementType = arrayStruct.GenericTypes[0];
                }
                else if (declaration.Type.TypeKind == TypeKind.CArray)
                {
                    var arrayType = (ArrayType)declaration.Type;
                    declaration.ArrayElementType = arrayType.ElementType;
                }
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
                    if (declaration.Type != null && declaration.Type.TypeKind != TypeKind.Pointer)
                    {
                        ErrorReporter.Report("Cannot assign null to non-pointer type", declaration.Value);
                    }

                    nullAst.TargetType = declaration.Type;
                }
            }
            // 3. Verify declaration values
            else if (declaration.Value != null)
            {
                var valueType = VerifyExpression(declaration.Value, currentFunction, scope);

                // Verify the assignment value matches the type definition if it has been defined
                if (declaration.Type == null)
                {
                    if (valueType?.TypeKind == TypeKind.Void)
                    {
                        ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                    }
                    declaration.Type = valueType;
                }
                else
                {
                    // Verify the type is correct
                    if (valueType != null)
                    {
                        if (!TypeEquals(declaration.Type, valueType))
                        {
                            ErrorReporter.Report($"Expected declaration value to be type '{declaration.Type}', but got '{valueType.Name}'", declaration.TypeDefinition);
                        }
                        else
                        {
                            VerifyConstantIfNecessary(declaration.Value, declaration.Type);
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
                    if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Struct && declaration.Type.TypeKind != TypeKind.String))
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
                            if (!TypeEquals(field.Type, valueType))
                            {
                                ErrorReporter.Report($"Expected field value to be type '{PrintTypeDefinition(field.TypeDefinition)}', but got '{valueType.Name}'", assignment.Value);
                            }
                            else
                            {
                                VerifyConstantIfNecessary(assignment.Value, field.Type);
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
                    if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Array && declaration.Type.TypeKind != TypeKind.CArray))
                    {
                        ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                    }
                    else
                    {
                        declaration.TypeDefinition.ConstCount = (uint)declaration.ArrayValues.Count;
                        var elementType = declaration.ArrayElementType;
                        foreach (var value in declaration.ArrayValues)
                        {
                            var valueType = VerifyExpression(value, currentFunction, scope);
                            if (valueType != null)
                            {
                                if (!TypeEquals(elementType, valueType))
                                {
                                    ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                                }
                                else
                                {
                                    VerifyConstantIfNecessary(value, elementType);
                                }
                            }
                        }
                    }
                }
            }

            // 6. Verify the type definition count if necessary
            if (declaration.Type?.TypeKind == TypeKind.Array && declaration.TypeDefinition?.Count != null)
            {
                var countType = VerifyConstantExpression(declaration.TypeDefinition.Count, currentFunction, scope, out var isConstant, out var arrayLength);

                if (countType != null)
                {
                    if (countType.TypeKind != TypeKind.Integer || arrayLength < 0)
                    {
                        ErrorReporter.Report($"Expected count of variable '{declaration.Name}' to be a positive integer", declaration.TypeDefinition.Count);
                    }
                    if (isConstant)
                    {
                        declaration.TypeDefinition.ConstCount = arrayLength;
                    }
                }
            }

            // 7. Verify constant values
            if (declaration.Constant)
            {
                switch (declaration.Value)
                {
                    case ConstantAst constant:
                    case StructFieldRefAst structField when structField.IsEnum:
                        break;
                    default:
                        ErrorReporter.Report($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
                        break;
                }
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
            var variableType = GetReference(assignment.Reference, currentFunction, scope, out _);
            var valueType = VerifyExpression(assignment.Value, currentFunction, scope);

            if (variableType == null) return;

            // 2. Verify the assignment value
            if (assignment.Value is NullAst nullAst)
            {
                if (assignment.Operator != Operator.None)
                {
                    ErrorReporter.Report("Cannot assign null value with operator assignment", assignment.Value);
                }
                if (variableType.TypeKind != TypeKind.Pointer)
                {
                    ErrorReporter.Report("Cannot assign null to non-pointer type", assignment.Value);
                }
                nullAst.TargetType = variableType;
                return;
            }

            // 3. Verify the assignment value matches the variable type definition
            if (valueType != null)
            {
                // 3a. Verify the operator is valid
                if (assignment.Operator != Operator.None)
                {
                    var lhs = variableType.TypeKind;
                    var rhs = valueType.TypeKind;
                    switch (assignment.Operator)
                    {
                        // Both need to be bool and returns bool
                        case Operator.And:
                        case Operator.Or:
                            if (lhs != TypeKind.Boolean || rhs != TypeKind.Boolean)
                            {
                                ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{variableType.Name}' and '{valueType.Name}'", assignment.Value);
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
                                ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{variableType.Name}' and '{valueType.Name}'", assignment.Value);
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (!(lhs == TypeKind.Boolean && rhs == TypeKind.Boolean) &&
                                !(lhs == TypeKind.Integer && rhs == TypeKind.Integer))
                            {
                                ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{variableType.Name}' and '{valueType.Name}'", assignment.Value);
                            }
                            break;
                        // Requires both to be integers
                        case Operator.ShiftLeft:
                        case Operator.ShiftRight:
                        case Operator.RotateLeft:
                        case Operator.RotateRight:
                            if (lhs != TypeKind.Integer || rhs != TypeKind.Integer)
                            {
                                ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{variableType.Name}' and '{valueType.Name}'", assignment.Value);
                            }
                            break;
                    }
                }
                else if (!TypeEquals(variableType, valueType))
                {
                    ErrorReporter.Report($"Expected assignment value to be type '{variableType.Name}', but got '{valueType.Name}'", assignment.Value);
                }
                else
                {
                    VerifyConstantIfNecessary(assignment.Value, variableType);
                }
            }
        }

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
                                if (structFieldRef.IsConstant)
                                {
                                    ErrorReporter.Report($"Cannot reassign value of constant field '{identifier.Name}'", identifier);
                                    return null;
                                }
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
                                            var pointerType = (PrimitiveAst)refType;
                                            refType = pointerType.PointerType;
                                        }
                                    }
                                    else
                                    {
                                        ErrorReporter.Report($"Overload [] for type '{fieldType.Name}' must be a pointer to be able to set the value", index);
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
                    var pointerType = (PrimitiveAst)reference;
                    return pointerType.PointerType;
                }
                default:
                    ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", ast);
                    return null;
            }
        }

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
                    constant = true;
                    return enumAst;
                case DeclarationAst declaration:
                    constant = declaration.Constant;
                    return declaration.Type;
                case VariableAst variable:
                    return variable.Type;
                default:
                    ErrorReporter.Report($"Identifier '{name}' is not a variable", ast);
                    return null;
            }
        }

        private IType VerifyStructFieldRef(StructFieldRefAst structField, IFunction currentFunction, ScopeAst scope)
        {
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
                            refType = declaration.Type;
                            break;
                        case VariableAst variable:
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

        private IType VerifyStructField(string fieldName, IType structType, StructFieldRefAst structField, int fieldIndex, IAst ast)
        {
            // 1. Load the struct definition
            if (structType.TypeKind == TypeKind.Pointer)
            {
                var pointerType = (PrimitiveAst)structType;
                structType = pointerType.PointerType;
                structField.Pointers[fieldIndex] = true;
            }

            structField.Types[fieldIndex] = structType;
            if (structType is ArrayType arrayType && fieldName == "length")
            {
                structField.IsConstant = true;
                structField.ConstantValue = arrayType.Length;
                return TypeTable.S32Type;
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
            switch (conditionalType?.TypeKind)
            {
                case TypeKind.Integer:
                case TypeKind.Float:
                case TypeKind.Boolean:
                case TypeKind.Pointer:
                    // Valid types
                    return !ErrorReporter.Errors.Any();
                case null:
                    return false;
                default:
                    ErrorReporter.Report($"Expected condition to be bool, int, float, or pointer, but got '{conditionalType.TypeKind}'", ast);
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
                var iterationType = VerifyExpression(each.Iteration, currentFunction, scope);
                if (iterationType == null) return;

                if (each.IndexVariable != null)
                {
                    each.IndexVariable.Type = TypeTable.S32Type;
                    each.Body.Identifiers.TryAdd(each.IndexVariable.Name, each.IndexVariable);
                }

                switch (iterationType.TypeKind)
                {
                    case TypeKind.CArray:
                        // Same logic as below with special case for constant length
                        var arrayType = (ArrayType)iterationType;
                        each.IterationVariable.Type = arrayType.ElementType;
                        each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
                        break;
                    case TypeKind.Array:
                        var arrayStruct = (StructAst)iterationType;
                        each.IterationVariable.Type = arrayStruct.GenericTypes[0];
                        each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
                        break;
                    default:
                        ErrorReporter.Report($"Type {iterationType.Name} cannot be used as an iterator", each.Iteration);
                        break;
                }
            }
            else
            {
                var beginType = VerifyExpression(each.RangeBegin, currentFunction, scope);
                if (beginType != null && beginType.TypeKind != TypeKind.Integer)
                {
                    ErrorReporter.Report($"Expected range to begin with 'int', but got '{beginType.Name}'", each.RangeBegin);
                }

                var endType = VerifyExpression(each.RangeEnd, currentFunction, scope);
                if (endType != null && endType.TypeKind != TypeKind.Integer)
                {
                    ErrorReporter.Report($"Expected range to end with 'int', but got '{endType.Name}'", each.RangeEnd);
                }

                each.IterationVariable.Type = TypeTable.S32Type;
                each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
            }

            // 2. Verify the scope of the each block
            VerifyScope(each.Body, currentFunction, scope, true);
        }

        private IType VerifyConstantExpression(IAst ast, IFunction currentFunction, ScopeAst scope, out bool isConstant, out uint arrayLength)
        {
            isConstant = false;
            arrayLength = 0;
            switch (ast)
            {
                case ConstantAst constant:
                    isConstant = true;
                    constant.Type = TypeTable.Types[constant.TypeName];
                    if (constant.Type.TypeKind == TypeKind.Integer)
                    {
                        arrayLength = (uint)constant.Value.UnsignedInteger;
                    }
                    return constant.Type;
                case NullAst:
                    isConstant = true;
                    return null;
                case StructFieldRefAst structField:
                    var structFieldType = VerifyStructFieldRef(structField, currentFunction, scope);
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
                            return functions[0];
                        }
                        ErrorReporter.Report($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                    }
                    switch (identifier)
                    {
                        case DeclarationAst declaration:
                            isConstant = declaration.Constant;
                            if (isConstant && declaration.Type.TypeKind == TypeKind.Integer)
                            {
                                if (declaration.Value is ConstantAst constValue)
                                {
                                    arrayLength = (uint)constValue.Value.UnsignedInteger;
                                }
                            }
                            return declaration.Type;
                        case IType type:
                            if (type is StructAst structAst && structAst.Generics.Any())
                            {
                                ErrorReporter.Report($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                            }
                            return type;
                        default:
                            return null;
                    }
                default:
                    return VerifyExpression(ast, currentFunction, scope);
            }
        }

        private IType VerifyExpression(IAst ast, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Verify the expression value
            switch (ast)
            {
                case ConstantAst constant:
                    return constant.Type = TypeTable.Types[constant.TypeName];
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
                            return functions[0];
                        }
                        ErrorReporter.Report($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                    }
                    switch (identifier)
                    {
                        case DeclarationAst declaration:
                            return declaration.Type;
                        case VariableAst variable:
                            return variable.Type;
                        case IType type:
                            if (type is StructAst structAst && structAst.Generics != null)
                            {
                                ErrorReporter.Report($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                            }
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

                        if (referenceType == null)
                        {
                            return null;
                        }

                        if (referenceType.TypeKind == TypeKind.CArray)
                        {
                            var arrayType = (ArrayType)referenceType;
                            var backendName = $"*.{arrayType.ElementType.BackendName}";
                            if (!TypeTable.Types.TryGetValue(backendName, out var pointerType))
                            {
                                var name = $"{arrayType.ElementType.Name}*";
                                pointerType = new PrimitiveAst {Name = name, BackendName = backendName, TypeKind = TypeKind.Pointer, Size = 8, PointerType = arrayType.ElementType};
                                TypeTable.Add(backendName, pointerType);
                                TypeTable.CreateTypeInfo(pointerType);
                            }
                            unary.Type = pointerType;
                        }
                        else
                        {
                            var backendName = $"*.{referenceType.BackendName}";
                            if (!TypeTable.Types.TryGetValue(backendName, out var pointerType))
                            {
                                var name = $"{referenceType.Name}*";
                                pointerType = new PrimitiveAst {Name = name, BackendName = backendName, TypeKind = TypeKind.Pointer, Size = 8, PointerType = referenceType};
                                TypeTable.Add(backendName, pointerType);
                                TypeTable.CreateTypeInfo(pointerType);
                            }
                            unary.Type = pointerType;
                        }
                        return unary.Type;
                    }
                    else
                    {
                        var valueType = VerifyExpression(unary.Value, currentFunction, scope);
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
                    var type = VerifyType(typeDef, scope);
                    if (type == null)
                    {
                        return null;
                    }
                    typeDef.TypeIndex = type.TypeIndex;
                    return type;
                }
                case CastAst cast:
                {
                    cast.TargetType = VerifyType(cast.TargetTypeDefinition, scope);
                    var valueType = VerifyExpression(cast.Value, currentFunction, scope);
                    switch (cast.TargetType?.TypeKind)
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
                            break;
                        case TypeKind.Pointer:
                            if (valueType?.TypeKind != TypeKind.Pointer)
                            {
                                ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast.Value);
                            }
                            break;
                        case null:
                            ErrorReporter.Report($"Unable to cast to invalid type '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast);
                            break;
                        default:
                            if (valueType != null)
                            {
                                ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{cast.TargetType.Name}'", cast);
                            }
                            break;
                    }
                    return cast.TargetType;
                }
                case null:
                    return null;
                default:
                    ErrorReporter.Report($"Invalid expression", ast);
                    return null;
            }
        }

        private void VerifyConstantIfNecessary(IAst ast, IType targetType)
        {
            if (ast is ConstantAst constant && constant.Type != targetType && (constant.Type.TypeKind == TypeKind.Integer || constant.Type.TypeKind == TypeKind.Float) && targetType.TypeKind != TypeKind.Any)
            {
                switch (targetType.TypeKind)
                {
                    case TypeKind.Integer:
                        if (constant.Type.TypeKind == TypeKind.Integer)
                        {
                            var currentInt = (PrimitiveAst)constant.Type;
                            var newInt = (PrimitiveAst)targetType;

                            if (!newInt.Signed)
                            {
                                if (currentInt.Signed && constant.Value.Integer < 0)
                                {
                                    ErrorReporter.Report($"Unsigned type '{targetType.Name}' cannot be negative", constant);
                                }
                                else
                                {
                                    var largestAllowedValue = Math.Pow(2, 8 * newInt.Size) - 1;
                                    if (constant.Value.UnsignedInteger > largestAllowedValue)
                                    {
                                        ErrorReporter.Report($"Value '{constant.Value.UnsignedInteger}' out of range for type '{targetType.Name}'", constant);
                                    }
                                }
                            }
                            else
                            {
                                var largestAllowedValue = Math.Pow(2, 8 * newInt.Size - 1) - 1;
                                if (currentInt.Signed)
                                {
                                    var lowestAllowedValue = -Math.Pow(2, 8 * newInt.Size - 1);
                                    if (constant.Value.Integer < lowestAllowedValue || constant.Value.Integer > largestAllowedValue)
                                    {
                                        ErrorReporter.Report($"Value '{constant.Value.Integer}' out of range for type '{targetType.Name}'", constant);
                                    }
                                }
                                else
                                {
                                    if (constant.Value.UnsignedInteger > largestAllowedValue)
                                    {
                                        ErrorReporter.Report($"Value '{constant.Value.Integer}' out of range for type '{targetType.Name}'", constant);
                                    }
                                }
                            }
                        }
                        // Convert float to int, shelved for now
                        /*else if (constant.Type.TypeKind == TypeKind.Float)
                        {
                            if (type.Size < constant.Type.Size && (constant.Value.Double > float.MaxValue || constant.Value.Double < float.MinValue))
                            {
                                ErrorReporter.Report($"Value '{constant.Value.Double}' out of range for type '{type.Name}'", constant);
                            }
                        }*/
                        else
                        {
                            ErrorReporter.Report($"Type '{constant.Type.Name}' cannot be converted to '{targetType.Name}'", constant);
                        }
                        break;
                    case TypeKind.Float:
                        // Convert int to float
                        if (constant.Type.TypeKind == TypeKind.Integer)
                        {
                            var currentInt = (PrimitiveAst)constant.Type;
                            if (currentInt.Signed)
                            {
                                constant.Value = new Constant {Double = (double)constant.Value.Integer};
                            }
                            else
                            {
                                constant.Value = new Constant {Double = (double)constant.Value.UnsignedInteger};
                            }
                        }
                        else if (constant.Type.TypeKind == TypeKind.Float)
                        {
                            if (targetType.Size < constant.Type.Size && (constant.Value.Double > float.MaxValue || constant.Value.Double < float.MinValue))
                            {
                                ErrorReporter.Report($"Value '{constant.Value.Double}' out of range for type '{targetType.Name}'", constant);
                            }
                        }
                        else
                        {
                            ErrorReporter.Report($"Type '{constant.Type.Name}' cannot be converted to '{targetType.Name}'", constant);
                        }
                        break;
                }

                constant.Type = targetType;
                constant.TypeName = targetType.Name;
            }
        }

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

            return enumAst;
        }

        private IType VerifyCall(CallAst call, IFunction currentFunction, ScopeAst scope)
        {
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
                    var genericType = VerifyType(generic, scope);
                    if (genericType == null)
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
                        return calledFunction.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) ? null : calledFunction.ReturnType;
                    }
                }
                else if (polymorphicFunctions == null && functions.Count == 1)
                {
                    return functions[0].ReturnType;
                }
                return null;
            }

            var function = DetermineCallingFunction(call, argumentTypes, specifiedArguments, scope);

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
                    VerifyConstantIfNecessary(specifiedArgument, functionArg.Type);

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
                    var argument = argumentTypes[i];
                    if (argument != null)
                    {
                        if (functionArg.Type.TypeKind == TypeKind.Type)
                        {
                            if (argument.TypeKind != TypeKind.Type)
                            {
                                var typeIndex = new ConstantAst {Type = TypeTable.S32Type, Value = new Constant {Integer = argument.TypeIndex}};
                                call.Arguments[i] = typeIndex;
                                argumentTypes[i] = typeIndex.Type;
                            }
                        }
                        else
                        {
                            VerifyConstantIfNecessary(call.Arguments[i], functionArg.Type);
                        }
                    }
                }
            }

            // Verify varargs call arguments
            if (function.Flags.HasFlag(FunctionFlags.Params))
            {
                var paramsType = function.ParamsElementType;

                if (paramsType != null)
                {
                    for (var i = argumentCount; i < argumentTypes.Length; i++)
                    {
                        var argumentAst = call.Arguments[i];
                        if (argumentAst is NullAst nullAst)
                        {
                            nullAst.TargetType = function.ParamsElementType;
                        }
                        else
                        {
                            var argument = argumentTypes[i];
                            if (argument != null)
                            {
                                if (paramsType.TypeKind == TypeKind.Type)
                                {
                                    if (argument.TypeKind != TypeKind.Type)
                                    {
                                        var typeIndex = new ConstantAst {Type = TypeTable.S32Type, Value = new Constant {Integer = argument.TypeIndex}};
                                        call.Arguments[i] = typeIndex;
                                        argumentTypes[i] = typeIndex.Type;
                                    }
                                }
                                else
                                {
                                    VerifyConstantIfNecessary(argumentAst, paramsType);
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
                        argumentTypes[i] = TypeTable.Float64Type;
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

            return function.ReturnType;
        }

        private FunctionAst DetermineCallingFunction(CallAst call, IType[] arguments, Dictionary<string, IType> specifiedArguments, ScopeAst scope)
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
                                    found = VerifyArgument(argument, specifiedArguments[name], functionArg.Type, function.Flags.HasFlag(FunctionFlags.Extern));
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
                            if (!VerifyArgument(argumentAst, arguments[callArgIndex], functionArg.Type, function.Flags.HasFlag(FunctionFlags.Extern)))
                            {
                                match = false;
                                break;
                            }
                            callArgIndex++;
                        }
                    }

                    if (match && function.Flags.HasFlag(FunctionFlags.Params))
                    {
                        var paramsType = function.ParamsElementType;

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
                    var genericTypes = new IType[function.Generics.Count];

                    if (call.Generics != null)
                    {
                        var genericsError = false;
                        for (var index = 0; index < call.Generics.Count; index++)
                        {
                            var generic = call.Generics[i];
                            genericTypes[i] = VerifyType(generic, scope);
                            if (genericTypes[i] == null)
                            {
                                genericsError = true;
                                ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(generic)}' in generic function call", generic);
                            }
                        }
                        if (genericsError)
                        {
                            continue;
                        }
                    }

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
                                if (!VerifyPolymorphicArgument(argumentAst, arguments[callArgIndex], functionArg.TypeDefinition, genericTypes))
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
                                    if (!VerifyArgument(call.Arguments[callArgIndex], arguments[callArgIndex], function.ParamsElementType))
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (match && genericTypes.All(t => t != null) && (function.Flags.HasFlag(FunctionFlags.Varargs) || callArgIndex == call.Arguments.Count))
                    {
                        var genericName = $"{function.Name}.{i}.{string.Join('.', genericTypes.Select(t => t.BackendName))}";
                        var name = $"{function.Name}<{string.Join(", ", genericTypes.Select(t => t.Name))}>";
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
                        if (polymorphedFunction.ReturnType == null)
                        {
                            polymorphedFunction.ReturnType = VerifyType(polymorphedFunction.ReturnTypeDefinition, scope);
                        }
                        foreach (var argument in polymorphedFunction.Arguments)
                        {
                            if (argument.Type == null)
                            {
                                argument.Type = VerifyType(argument.TypeDefinition, scope, out _, out _, out var isParams, allowParams: true);
                                if (isParams)
                                {
                                    var paramsArrayType = (StructAst)argument.Type;
                                    polymorphedFunction.ParamsElementType = paramsArrayType.GenericTypes[0];
                                }
                            }
                        }

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

        private bool VerifyArgument(IAst argumentAst, IType callType, IType argumentType, bool externCall = false)
        {
            if (argumentAst is NullAst)
            {
                if (argumentType?.TypeKind != TypeKind.Pointer)
                {
                    return false;
                }
            }
            else if (argumentType.TypeKind != TypeKind.Type && argumentType.TypeKind != TypeKind.Any)
            {
                if (externCall && callType.TypeKind == TypeKind.String)
                {
                    if (argumentType != _rawStringType)
                    {
                        return false;
                    }
                }
                else if (!TypeEquals(argumentType, callType))
                {
                    return false;
                }
            }
            return true;
        }

        private bool VerifyPolymorphicArgument(IAst ast, IType callType, TypeDefinition argumentType, IType[] genericTypes)
        {
            if (ast is NullAst)
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

        private bool VerifyPolymorphicArgument(IType callType, TypeDefinition argumentType, IType[] genericTypes)
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
                if (argumentType.Generics.Any())
                {
                    switch (callType.TypeKind)
                    {
                        case TypeKind.Pointer:
                            if (argumentType.Name != "*" || argumentType.Generics.Count != 1)
                            {
                                return false;
                            }
                            var pointerType = (PrimitiveAst)callType;
                            return VerifyPolymorphicArgument(pointerType.PointerType, argumentType.Generics[0], genericTypes);
                        case TypeKind.Array:
                        case TypeKind.Struct:
                            var structDef = (StructAst)callType;
                            if (structDef.GenericTypes == null || argumentType.Generics.Count != structDef.GenericTypes.Length || argumentType.Name != structDef.BaseStructName)
                            {
                                return false;
                            }
                            for (var i = 0; i < structDef.GenericTypes.Length; i++)
                            {
                                if (!VerifyPolymorphicArgument(structDef.GenericTypes[i], argumentType.Generics[i], genericTypes))
                                {
                                    return false;
                                }
                            }
                            break;
                        // case TypeKind.CArray:
                        // case TypeKind.Function:
                        //    break;
                        default:
                            return false;
                    }
                }
                else if (callType.Name != argumentType.Name)
                {
                    return false;
                }
            }
            return true;
        }

        private IType VerifyExpressionType(ExpressionAst expression, IFunction currentFunction, ScopeAst scope)
        {
            // 1. Get the type of the initial child
            expression.Type = VerifyExpression(expression.Children[0], currentFunction, scope);
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
                    expression.Type = TypeTable.BoolType;
                    expression.ResultingTypes.Add(expression.Type);
                    continue;
                }

                var nextExpressionType = VerifyExpression(next, currentFunction, scope);
                if (nextExpressionType == null) return null;

                // 3. Verify the operator and expression types are compatible and convert the expression type if necessary
                var type = expression.Type.TypeKind;
                var nextType = nextExpressionType.TypeKind;
                if ((type == TypeKind.Struct && nextType == TypeKind.Struct) ||
                    (type == TypeKind.String && nextType == TypeKind.String))
                {
                    if (TypeEquals(expression.Type, nextExpressionType, true))
                    {
                        var overload = VerifyOperatorOverloadType((StructAst)expression.Type, op, currentFunction, expression.Children[i], scope);
                        if (overload != null)
                        {
                            expression.OperatorOverloads[i] = overload;
                            expression.Type = overload.ReturnType;
                        }
                    }
                    else
                    {
                        ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
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
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                expression.Type = TypeTable.BoolType;
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
                                    ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                }
                            }
                            else if (!(type == TypeKind.Integer || type == TypeKind.Float) &&
                                !(nextType == TypeKind.Integer || nextType == TypeKind.Float))
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            }
                            expression.Type = TypeTable.BoolType;
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
                                if (expression.Type == nextExpressionType)
                                    break;

                                // For integer operations, use the larger size and convert to signed if one type is signed
                                if (type == TypeKind.Integer && nextType == TypeKind.Integer)
                                {
                                    expression.Type = GetNextIntegerType(expression.Type, nextExpressionType);
                                }
                                // For floating point operations, convert to the larger size
                                else if (type == TypeKind.Float && nextType == TypeKind.Float)
                                {
                                    if (expression.Type.Size < nextExpressionType.Size)
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
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            }
                            break;
                        // Requires both integer or bool types and returns more same type
                        case Operator.BitwiseAnd:
                        case Operator.BitwiseOr:
                        case Operator.Xor:
                            if (type == TypeKind.Integer && nextType == TypeKind.Integer)
                            {
                                if (expression.Type != nextExpressionType)
                                {
                                    expression.Type = GetNextIntegerType(expression.Type, nextExpressionType);
                                }
                            }
                            else if (!(type == TypeKind.Boolean && nextType == TypeKind.Boolean))
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                if (nextType == TypeKind.Boolean || nextType == TypeKind.Integer)
                                {
                                    expression.Type = nextExpressionType;
                                }
                                else if (!(type == TypeKind.Boolean || type == TypeKind.Integer))
                                {
                                    // If the type can't be determined, default to int
                                    expression.Type = TypeTable.S32Type;
                                }
                            }
                            break;
                        case Operator.ShiftLeft:
                        case Operator.ShiftRight:
                        case Operator.RotateLeft:
                        case Operator.RotateRight:
                            if (type != TypeKind.Integer || nextType != TypeKind.Integer)
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                                if (type != TypeKind.Integer)
                                {
                                    if (nextType == TypeKind.Integer)
                                    {
                                        expression.Type = nextExpressionType;
                                    }
                                    else
                                    {
                                        // If the type can't be determined, default to int
                                        expression.Type = TypeTable.S32Type;
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

        private IType GetNextIntegerType(IType currentType, IType nextType)
        {
            var currentIntegerType = (PrimitiveAst)currentType;
            var nextIntegerType = (PrimitiveAst)nextType;

            var size = currentIntegerType.Size > nextIntegerType.Size ? currentIntegerType.Size : nextIntegerType.Size;
            if (currentIntegerType.Signed || nextIntegerType.Signed)
            {
                return size switch
                {
                    1 => TypeTable.S8Type,
                    2 => TypeTable.S16Type,
                    8 => TypeTable.S64Type,
                    _ => TypeTable.S32Type
                };
            }
            else
            {
                return size switch
                {
                    1 => TypeTable.U8Type,
                    2 => TypeTable.U16Type,
                    8 => TypeTable.U64Type,
                    _ => TypeTable.U32Type
                };
            }
        }

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

        private IType VerifyIndex(IndexAst index, IType type, IFunction currentFunction, ScopeAst scope, out bool overloaded)
        {
            // 1. Verify the variable is an array or the operator overload exists
            overloaded = false;
            IType elementType = null;
            switch (type?.TypeKind)
            {
                case TypeKind.Struct:
                    index.CallsOverload = true;
                    overloaded = true;
                    index.Overload = VerifyOperatorOverloadType((StructAst)type, Operator.Subscript, currentFunction, index, scope);
                    elementType = index.Overload?.ReturnType;
                    break;
                case TypeKind.Array:
                    var arrayStruct = (StructAst)type;
                    elementType = arrayStruct.GenericTypes[0];
                    break;
                case TypeKind.CArray:
                    var arrayType = (ArrayType)type;
                    elementType = arrayType.ElementType;
                    break;
                case TypeKind.Pointer:
                    var pointerType = (PrimitiveAst)type;
                    elementType = pointerType.PointerType;
                    break;
                case TypeKind.String:
                    elementType = TypeTable.U8Type;
                    break;
                case null:
                    break;
                default:
                    ErrorReporter.Report($"Cannot index type '{type.Name}'", index);
                    break;
            }

            // 2. Verify the count expression is an integer
            var indexValue = VerifyExpression(index.Index, currentFunction, scope);
            if (indexValue != null && indexValue.TypeKind != TypeKind.Integer && indexValue.TypeKind != TypeKind.Type)
            {
                ErrorReporter.Report($"Expected index to be type 'int', but got '{indexValue.Name}'", index);
            }

            return elementType;
        }

        private OperatorOverloadAst VerifyOperatorOverloadType(StructAst type, Operator op, IFunction currentFunction, IAst ast, ScopeAst scope)
        {
            if (_operatorOverloads.TryGetValue(type.BackendName, out var overloads) && overloads.TryGetValue(op, out var overload))
            {
                if (!overload.Flags.HasFlag(FunctionFlags.Verified) && overload != currentFunction)
                {
                    VerifyOperatorOverload(overload);
                }
                return overload;
            }
            else if (_polymorphicOperatorOverloads.TryGetValue(type.BaseStructName, out var polymorphicOverloads) && polymorphicOverloads.TryGetValue(op, out var polymorphicOverload))
            {
                var polymorphedOverload = _polymorpher.CreatePolymorphedOperatorOverload(polymorphicOverload, type.GenericTypes.ToArray());
                if (overloads == null)
                {
                    _operatorOverloads[type.BackendName] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
                }
                overloads[op] = polymorphedOverload;
                foreach (var argument in polymorphedOverload.Arguments)
                {
                    if (argument.Type == null)
                    {
                        argument.Type = VerifyType(argument.TypeDefinition, scope);
                    }
                }

                VerifyOperatorOverload(polymorphedOverload);
                return polymorphedOverload;
            }
            else
            {
                ErrorReporter.Report($"Type '{type.Name}' does not contain an overload for operator '{PrintOperator(op)}'", ast);
                return null;
            }
        }

        private bool TypeEquals(IType target, IType source, bool checkPrimitives = false)
        {
            if (target == null || source == null) return false;
            if (target == source) return true;

            switch (target.TypeKind)
            {
                case TypeKind.Integer:
                    if (source.TypeKind == TypeKind.Integer)
                    {
                        if (!checkPrimitives) return true;
                        var ap = (PrimitiveAst)target;
                        var bp = (PrimitiveAst)source;
                        return target.Size == source.Size && ap.Signed == bp.Signed;
                    }
                    return false;
                case TypeKind.Float:
                    if (source.TypeKind == TypeKind.Float)
                    {
                        if (!checkPrimitives) return true;
                        return target.Size == source.Size;
                    }
                    return false;
                case TypeKind.Pointer:
                    if (source.TypeKind != TypeKind.Pointer)
                    {
                        return false;
                    }
                    var targetPointer = (PrimitiveAst)target;
                    var targetPointerType = targetPointer.PointerType;
                    var sourcePointer = (PrimitiveAst)source;
                    var sourcePointerType = sourcePointer.PointerType;

                    if (targetPointerType == TypeTable.VoidType || sourcePointerType == TypeTable.VoidType)
                    {
                        return true;
                    }

                    if (targetPointerType is StructAst targetStruct && sourcePointerType is StructAst sourceStruct)
                    {
                        var sourceBaseStruct = sourceStruct.BaseStruct;
                        while (sourceBaseStruct != null)
                        {
                            if (targetStruct == sourceBaseStruct)
                            {
                                return true;
                            }
                            sourceBaseStruct = sourceBaseStruct.BaseStruct;
                        }
                    }

                    return false;
            }

            return false;
        }

        private static bool TypeDefinitionEquals(TypeDefinition a, TypeDefinition b)
        {
            if (a?.Name != b?.Name || a.Generics.Count != b.Generics.Count) return false;

            for (var i = 0; i < a.Generics.Count; i++)
            {
                if (!TypeDefinitionEquals(a.Generics[i], b.Generics[i])) return false;
            }

            return true;
        }

        private IType VerifyType(TypeDefinition type, ScopeAst scope, int depth = 0)
        {
            return VerifyType(type, scope, out _, out _, out _, depth);
        }

        private IType VerifyType(TypeDefinition type, ScopeAst scope, out bool isGeneric, out bool isVarargs, out bool isParams, int depth = 0, bool allowParams = false, int? initialArrayLength = null)
        {
            isGeneric = false;
            isVarargs = false;
            isParams = false;
            if (type == null) return null;

            if (type.IsGeneric)
            {
                if (type.Generics.Any())
                {
                    ErrorReporter.Report("Generic type cannot have additional generic types", type);
                }
                isGeneric = true;
                return null;
            }

            if (type.Count != null && type.Name != "Array" && type.Name != "CArray")
            {
                ErrorReporter.Report($"Type '{PrintTypeDefinition(type)}' cannot have a count", type);
                return null;
            }

            var hasGenerics = type.Generics.Any();

            switch (type.Name)
            {
                case "bool":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'bool' cannot have generics", type);
                    }
                    return TypeTable.BoolType;
                case "string":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'string' cannot have generics", type);
                    }
                    return TypeTable.StringType;
                case "Array":
                    if (type.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"Type 'Array' should have 1 generic type, but got {type.Generics.Count}", type);
                    }
                    return VerifyArray(type, scope, depth, out isGeneric);
                case "CArray":
                    if (type.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"Type 'CArray' should have 1 generic type, but got {type.Generics.Count}", type);
                        return null;
                    }
                    else
                    {
                        var elementType = VerifyType(type.Generics[0], scope, out isGeneric, out _, out _, depth + 1, allowParams);
                        if (elementType == null || isGeneric)
                        {
                            return null;
                        }
                        else
                        {
                            uint arrayLength;
                            if (initialArrayLength.HasValue)
                            {
                                arrayLength = (uint)initialArrayLength.Value;
                            }
                            else
                            {
                                var countType = VerifyConstantExpression(type.Count, null, scope, out var isConstant, out arrayLength);
                                if (countType?.TypeKind != TypeKind.Integer || !isConstant || arrayLength < 0)
                                {
                                    ErrorReporter.Report($"Expected size of C array to be a constant, positive integer", type);
                                }
                            }

                            var name = $"{PrintTypeDefinition(type)}[{arrayLength}]";
                            var backendName = $"{type.GenericName}.{arrayLength}";
                            if (!TypeTable.Types.TryGetValue(backendName, out var arrayType))
                            {
                                arrayType = new ArrayType {Name = name, BackendName = backendName, Size = elementType.Size * arrayLength, Length = arrayLength, ElementType = elementType};
                                TypeTable.Add(backendName, arrayType);
                                TypeTable.CreateTypeInfo(arrayType);
                            }
                            return arrayType;
                        }
                    }
                case "void":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'void' cannot have generics", type);
                    }
                    return TypeTable.VoidType;
                case "*":
                    if (type.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"pointer type should have reference to 1 type, but got {type.Generics.Count}", type);
                        return null;
                    }
                    else if (TypeTable.Types.TryGetValue(type.GenericName, out var pointerType))
                    {
                        return pointerType;
                    }
                    else
                    {
                        var typeDef = type.Generics[0];
                        var pointedToType = VerifyType(typeDef, scope, out isGeneric, out _, out _, depth + 1, allowParams);
                        if (pointedToType == null || isGeneric)
                        {
                            return null;
                        }
                        else
                        {
                            // There are some cases where the pointed to type is a struct that contains a field for the pointer type
                            // To account for this, the type table needs to be checked for again for the type
                            if (!TypeTable.Types.TryGetValue(type.GenericName, out pointerType))
                            {
                                pointerType = new PrimitiveAst {Name = PrintTypeDefinition(type), BackendName = type.GenericName, TypeKind = TypeKind.Pointer, Size = 8, PointerType = pointedToType};
                                TypeTable.Add(type.GenericName, pointerType);
                                TypeTable.CreateTypeInfo(pointerType);
                            }
                            return pointerType;
                        }
                    }
                case "...":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'varargs' cannot have generics", type);
                    }
                    isVarargs = true;
                    return null;
                case "Params":
                    if (!allowParams)
                    {
                        return null;
                    }
                    if (depth != 0)
                    {
                        ErrorReporter.Report($"Params can only be declared as a top level type, such as 'Params<int>'", type);
                        return null;
                    }
                    if (type.Generics.Count == 0)
                    {
                        isParams = true;
                        const string backendName = "Array.Any";
                        if (TypeTable.Types.TryGetValue(backendName, out var arrayType))
                        {
                            return arrayType;
                        }

                        return CreateArrayStruct("Array<Any>", backendName, TypeTable.AnyType);
                    }
                    if (type.Generics.Count != 1)
                    {
                        ErrorReporter.Report($"Type 'Params' should have 1 generic type, but got {type.Generics.Count}", type);
                        return null;
                    }
                    isParams = true;
                    return VerifyArray(type, scope, depth, out isGeneric);
                case "Type":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'Type' cannot have generics", type);
                    }
                    return TypeTable.TypeType;
                case "Any":
                    if (hasGenerics)
                    {
                        ErrorReporter.Report("Type 'Any' cannot have generics", type);
                    }
                    return TypeTable.AnyType;
                default:
                    if (hasGenerics)
                    {
                        var genericName = type.GenericName;
                        if (TypeTable.Types.TryGetValue(genericName, out var structType))
                        {
                            return structType;
                        }
                        else
                        {
                            var generics = type.Generics.ToArray();
                            var genericTypes = new IType[generics.Length];
                            var error = false;
                            for (var i = 0; i < generics.Length; i++)
                            {
                                var genericType = genericTypes[i] = VerifyType(generics[i], scope, out var hasGeneric, out _, out _, depth + 1, allowParams);
                                if (genericType == null && !hasGeneric)
                                {
                                    error = true;
                                }
                                else if (hasGeneric)
                                {
                                    isGeneric = true;
                                }
                            }
                            if (!_polymorphicStructs.TryGetValue(type.Name, out var structDef))
                            {
                                ErrorReporter.Report($"No polymorphic structs of type '{type.Name}'", type);
                                return null;
                            }
                            else if (structDef.Generics.Count != type.Generics.Count)
                            {
                                ErrorReporter.Report($"Expected type '{type.Name}' to have {structDef.Generics.Count} generic(s), but got {type.Generics.Count}", type);
                                return null;
                            }
                            else if (error || isGeneric)
                            {
                                return null;
                            }
                            else
                            {
                                var name = PrintTypeDefinition(type);
                                var polyStruct = _polymorpher.CreatePolymorphedStruct(structDef, name, genericName, TypeKind.Struct, genericTypes, generics);
                                TypeTable.Add(genericName, polyStruct);
                                VerifyStruct(polyStruct);
                                return polyStruct;
                            }
                        }
                    }
                    else if (TypeTable.Types.TryGetValue(type.Name, out var typeValue))
                    {
                        if (typeValue is StructAst structAst && !structAst.Verifying)
                        {
                            VerifyStruct(structAst);
                        }
                        return typeValue;
                    }
                    return null;
            }
        }

        private IType VerifyArray(TypeDefinition typeDef, ScopeAst scope, int depth, out bool isGeneric)
        {
            isGeneric = false;
            var elementTypeDef = typeDef.Generics[0];
            var elementType = VerifyType(elementTypeDef, scope, out isGeneric, out _, out _, depth + 1);
            if (elementType == null || isGeneric)
            {
                return null;
            }

            var backendName = $"Array.{elementType.BackendName}";
            if (TypeTable.Types.TryGetValue(backendName, out var arrayType))
            {
                return arrayType;
            }

            return CreateArrayStruct($"Array<{elementType.Name}>", backendName, elementType, elementTypeDef);
        }

        private IType CreateArrayStruct(string name, string backendName, IType elementType, TypeDefinition elementTypeDef = null)
        {
            var arrayStruct = _polymorpher.CreatePolymorphedStruct(_baseArrayType, name, backendName, TypeKind.Array, new []{elementType}, elementTypeDef);
            TypeTable.Add(backendName, arrayStruct);
            VerifyStruct(arrayStruct);
            return arrayStruct;
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
