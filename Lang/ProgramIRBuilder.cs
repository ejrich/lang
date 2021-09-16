using System;
using System.Collections.Generic;
using System.Linq;

namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }
        void Init();
        FunctionIR AddFunction(FunctionAst function);
        FunctionIR AddOperatorOverload(OperatorOverloadAst overload);
        void EmitGlobalVariable(DeclarationAst declaration, ScopeAst scope);
        void EmitDeclaration(FunctionIR function, DeclarationAst declaration, ScopeAst scope);
        void EmitAssignment(FunctionIR function, AssignmentAst assignment, ScopeAst scope);
        void EmitReturn(FunctionIR function, ReturnAst returnAst, IType returnType, ScopeAst scope, BasicBlock block = null);
        InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block = null, bool useRawString = false);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        private IType _u8Type;
        private IType _s32Type;
        private IType _float64Type;
        private StructAst _stringStruct;

        public ProgramIR Program { get; } = new();

        public void Init()
        {
            _u8Type = TypeTable.Types["u8"];
            _s32Type = TypeTable.Types["s32"];
            _float64Type = TypeTable.Types["float64"];
        }

        public FunctionIR AddFunction(FunctionAst function)
        {
            var functionName = GetFunctionName(function);

            var functionIR = new FunctionIR();
            if (!function.Extern && !function.Compiler)
            {
                functionIR.Allocations = new();
                functionIR.BasicBlocks = new();

                var entryBlock = new BasicBlock();
                functionIR.BasicBlocks.Add(entryBlock);

                for (var i = 0; i < function.Arguments.Count; i++)
                {
                    var argument = function.Arguments[i];
                    var allocationIndex = AddAllocation(functionIR, argument);

                    var storeInstruction = new Instruction
                    {
                        Type = InstructionType.Store, Index = allocationIndex,
                        Value1 = new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i}
                    };
                    entryBlock.Instructions.Add(storeInstruction);
                }
            }

            if (functionName == "main")
            {
                Program.EntryPoint = functionIR;
            }
            else
            {
                Program.Functions[functionName] = functionIR;
            }

            return functionIR;
        }

        public FunctionIR AddOperatorOverload(OperatorOverloadAst overload)
        {
            var functionName = GetOperatorOverloadName(overload.Type, overload.Operator);

            var entryBlock = new BasicBlock();
            var functionIR = new FunctionIR {Allocations = new(), BasicBlocks = new List<BasicBlock>{entryBlock}};

            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                var allocationIndex = AddAllocation(functionIR, argument);

                EmitStore(entryBlock, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i});
            }

            Program.Functions[functionName] = functionIR;

            return functionIR;
        }

        public void EmitGlobalVariable(DeclarationAst declaration, ScopeAst scope)
        {
            if (declaration.Constant)
            {
                Program.Constants[declaration.Name] = EmitConstantIR(declaration.Value, scope);
            }
            else
            {
                var globalIndex = Program.GlobalVariables.Count;
                declaration.AllocationIndex = globalIndex;
                var globalVariable = new GlobalVariable {Name = declaration.Name, Index = globalIndex, Size = declaration.Type.Size};

                if (declaration.Type is ArrayType arrayType)
                {
                    globalVariable.Array = true;
                    globalVariable.ArrayLength = declaration.TypeDefinition.ConstCount.Value;
                    globalVariable.Type = arrayType.ElementType;
                }
                else
                {
                    globalVariable.Type = declaration.Type;
                }

                Program.GlobalVariables.Add(globalVariable);

                if (declaration.Value != null)
                {
                    globalVariable.InitialValue = EmitConstantIR(declaration.Value, scope);
                    return;
                }

                switch (declaration.TypeDefinition.TypeKind)
                {
                    // Initialize arrays
                    case TypeKind.Array:
                        var arrayStruct = (StructAst)declaration.Type;
                        if (declaration.TypeDefinition.ConstCount != null)
                        {
                            globalVariable.InitialValue = InitializeGlobalArray(arrayStruct, declaration, scope);
                        }
                        else
                        {
                            var constArray = new InstructionValue
                            {
                                ValueType = InstructionValueType.ConstantStruct, Type = arrayStruct,
                                Values = new [] {GetDefaultConstant(_s32Type), new InstructionValue {ValueType = InstructionValueType.Null}}
                            };
                            globalVariable.InitialValue = constArray;
                        }
                        break;
                    case TypeKind.CArray:
                        if (declaration.ArrayValues != null)
                        {
                            globalVariable.InitialArrayValues = declaration.ArrayValues.Select(val => EmitConstantIR(val, scope)).ToArray();
                        }
                        break;
                    // Initialize struct field default values
                    case TypeKind.Struct:
                    case TypeKind.String:
                        globalVariable.InitialValue = GetConstantStruct((StructAst)declaration.Type, scope, declaration.Assignments);
                        break;
                    // Initialize pointers to null
                    case TypeKind.Pointer:
                        globalVariable.InitialValue = new InstructionValue {ValueType = InstructionValueType.Null};
                        break;
                    // Or initialize to default
                    default:
                        globalVariable.InitialValue = GetDefaultConstant(declaration.Type);
                        break;
                }
            }
        }

        private InstructionValue GetConstantStruct(StructAst structDef, ScopeAst scope, Dictionary<string, AssignmentAst> assignments)
        {
            var constantStruct = new InstructionValue {Type = structDef, Values = new InstructionValue[structDef.Fields.Count]};

            if (assignments == null)
            {
                for (var i = 0; i < structDef.Fields.Count; i++)
                {
                    var field = structDef.Fields[i];
                    constantStruct.Values[i] = GetFieldConstant(field, scope);
                }
            }
            else
            {
                for (var i = 0; i < structDef.Fields.Count; i++)
                {
                    var field = structDef.Fields[i];

                    if (assignments.TryGetValue(field.Name, out var assignment))
                    {
                        constantStruct.Values[i] = EmitConstantIR(assignment.Value, scope);
                    }
                    else
                    {
                        constantStruct.Values[i] = GetFieldConstant(field, scope);
                    }
                }
            }

            return constantStruct;
        }

        private InstructionValue GetFieldConstant(StructFieldAst field, ScopeAst scope)
        {
            switch (field.Type.TypeKind)
            {
                // Initialize arrays
                case TypeKind.Array:
                    var arrayStruct = (StructAst)field.Type;
                    if (field.TypeDefinition.ConstCount != null)
                    {
                        return InitializeGlobalArray(arrayStruct, field, scope);
                    }
                    else
                    {
                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.ConstantStruct, Type = arrayStruct,
                            Values = new [] {GetDefaultConstant(_s32Type), new InstructionValue {ValueType = InstructionValueType.Null}}
                        };
                    }
                case TypeKind.CArray:
                    var length = field.TypeDefinition.ConstCount.Value;
                    var constArray = new InstructionValue {ValueType = InstructionValueType.ConstantArray, Type = field.ArrayElementType, ArrayLength = length};
                    if (field.ArrayValues != null)
                    {
                        constArray.Values = field.ArrayValues.Select(val => EmitConstantIR(val, scope)).ToArray();
                    }
                    return constArray;
                // Initialize struct field default values
                case TypeKind.Struct:
                case TypeKind.String: // TODO String default values
                    return GetConstantStruct((StructAst)field.Type, scope, field.Assignments);
                // Initialize pointers to null
                case TypeKind.Pointer:
                    return new InstructionValue {ValueType = InstructionValueType.Null};
                // Or initialize to default
                default:
                    return field.Value == null ? GetDefaultConstant(field.Type) : EmitConstantIR(field.Value, scope);
            }
        }

        private InstructionValue InitializeGlobalArray(StructAst arrayStruct, IDeclaration declaration, ScopeAst scope)
        {
            var length = declaration.TypeDefinition.ConstCount.Value;
            var elementType = declaration.ArrayElementType;

            var arrayIndex = Program.GlobalVariables.Count;
            var arrayVariable = new GlobalVariable
            {
                Name = "____array", Index = arrayIndex, Size = elementType.Size,
                Array = true, ArrayLength = length, Type = elementType
            };
            Program.GlobalVariables.Add(arrayVariable);

            if (declaration.ArrayValues != null)
            {
                arrayVariable.InitialArrayValues = declaration.ArrayValues.Select(val => EmitConstantIR(val, scope)).ToArray();
            }

            return new InstructionValue
            {
                ValueType = InstructionValueType.ConstantStruct, Type = arrayStruct,
                Values = new [] {GetConstantInteger(length), new InstructionValue {ValueIndex = arrayIndex}}
            };
        }

        public void EmitDeclaration(FunctionIR function, DeclarationAst declaration, ScopeAst scope)
        {
            if (declaration.Constant)
            {
                function.Constants ??= new();

                function.Constants[declaration.Name] = EmitIR(function, declaration.Value, scope);
            }
            else
            {
                var allocationIndex = AddAllocation(function, declaration);

                var block = function.BasicBlocks[^1];
                if (declaration.Value != null)
                {
                    var value = EmitIR(function, declaration.Value, scope, block);
                    EmitStore(block, allocationIndex, EmitCastValue(block, value, declaration.Type));
                    return;
                }

                switch (declaration.TypeDefinition.TypeKind)
                {
                    // Initialize arrays
                    case TypeKind.Array:
                        var pointer = EmitGetPointer(block, allocationIndex, declaration.Type);
                        var arrayStruct = (StructAst)declaration.Type;
                        if (declaration.TypeDefinition.ConstCount != null)
                        {
                            var arrayPointer = InitializeConstArray(function, block, pointer, arrayStruct, declaration.TypeDefinition.ConstCount.Value, declaration.ArrayElementType);

                            if (declaration.ArrayValues != null)
                            {
                                InitializeArrayValues(function, block, arrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                            }
                        }
                        else if (declaration.TypeDefinition.Count != null)
                        {
                            function.SaveStack = true;
                            var count = EmitIR(function, declaration.TypeDefinition.Count, scope, block);
                            var countPointer = EmitGetStructPointer(block, pointer, arrayStruct, 0);
                            EmitStore(block, countPointer, count);

                            var elementType = new InstructionValue {ValueType = InstructionValueType.Type, Type = declaration.ArrayElementType};
                            var arrayData = EmitInstruction(InstructionType.AllocateArray, block, null, count, elementType);
                            var dataPointer = EmitGetStructPointer(block, pointer, arrayStruct, 0);
                            EmitStore(block, dataPointer, arrayData);
                        }
                        else
                        {
                            var lengthPointer = EmitGetStructPointer(block, pointer, arrayStruct, 0);
                            var lengthValue = GetDefaultConstant(_s32Type);
                            EmitStore(block, lengthPointer, lengthValue);
                        }
                        break;
                    case TypeKind.CArray:
                        var cArrayPointer = EmitGetPointer(block, allocationIndex, declaration.Type);
                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(function, block, cArrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                        }
                        break;
                    // Initialize struct field default values
                    case TypeKind.Struct:
                    case TypeKind.String:
                        var structPointer = EmitGetPointer(block, allocationIndex, declaration.Type);
                        InitializeStruct(function, block, (StructAst)declaration.Type, structPointer, scope, declaration.Assignments);
                        break;
                    // Initialize pointers to null
                    case TypeKind.Pointer:
                        EmitStore(block, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Null});
                        break;
                    // Or initialize to default
                    default:
                        var zero = GetDefaultConstant(declaration.Type);
                        EmitStore(block, allocationIndex, zero);
                        break;
                }
            }
        }

        private int AddAllocation(FunctionIR function, DeclarationAst declaration)
        {
            int index;
            if (declaration.Type is ArrayType arrayType)
            {
                index = AddAllocation(function, arrayType.ElementType, true, declaration.TypeDefinition.ConstCount.Value);
            }
            else
            {
                index = AddAllocation(function, declaration.Type);
            }
            declaration.AllocationIndex = index;
            return index;
        }

        private int AddAllocation(FunctionIR function, IType type, bool array = false, uint arrayLength = 0)
        {
            var index = function.Allocations.Count;
            var allocation = new Allocation
            {
                Index = index, Offset = function.StackSize, Size = type.Size,
                Array = array, ArrayLength = arrayLength, Type = type
            };
            function.StackSize += array ? arrayLength * type.Size : type.Size;
            function.Allocations.Add(allocation);
            return index;
        }

        private InstructionValue InitializeConstArray(FunctionIR function, BasicBlock block, InstructionValue arrayPointer, StructAst arrayStruct, uint length, IType elementType)
        {
            var lengthPointer = EmitGetStructPointer(block, arrayPointer, arrayStruct, 0);
            var lengthValue = GetConstantInteger(length);
            EmitStore(block, lengthPointer, lengthValue);

            var dataIndex = AddAllocation(function, elementType, true, length);
            var arrayDataPointer = EmitGetPointer(block, dataIndex, elementType);
            var dataPointer = EmitGetStructPointer(block, arrayPointer, arrayStruct, 1);
            EmitStore(block, dataPointer, arrayDataPointer);

            return arrayDataPointer;
        }

        private void InitializeArrayValues(FunctionIR function, BasicBlock block, InstructionValue arrayPointer, IType elementType, List<IAst> arrayValues, ScopeAst scope)
        {
            for (var i = 0; i < arrayValues.Count; i++)
            {
                var index = GetConstantInteger(i);
                var pointer = EmitGetPointer(block, arrayPointer, index, elementType, getFirstPointer: true);

                var value = EmitIR(function, arrayValues[i], scope, block);
                EmitStore(block, pointer, EmitCastValue(block, value, elementType));
            }
        }

        private void InitializeStruct(FunctionIR function, BasicBlock block, StructAst structDef, InstructionValue pointer, ScopeAst scope, Dictionary<string, AssignmentAst> assignments)
        {
            if (assignments == null)
            {
                for (var i = 0; i < structDef.Fields.Count; i++)
                {
                    var field = structDef.Fields[i];

                    var fieldPointer = EmitGetStructPointer(block, pointer, structDef, i, field);

                    InitializeField(function, block, field, fieldPointer, scope);
                }
            }
            else
            {
                for (var i = 0; i < structDef.Fields.Count; i++)
                {
                    var field = structDef.Fields[i];

                    var fieldPointer = EmitGetStructPointer(block, pointer, structDef, i, field);

                    if (assignments.TryGetValue(field.Name, out var assignment))
                    {
                        var value = EmitIR(function, assignment.Value, scope, block);

                        EmitStore(block, fieldPointer, EmitCastValue(block, value, field.Type));
                    }
                    else
                    {
                        InitializeField(function, block, field, fieldPointer, scope);
                    }
                }
            }
        }

        private void InitializeField(FunctionIR function, BasicBlock block, StructFieldAst field, InstructionValue pointer, ScopeAst scope)
        {
            switch (field.Type.TypeKind)
            {
                // Initialize arrays
                case TypeKind.Array:
                    var arrayStruct = (StructAst)field.Type;
                    if (field.TypeDefinition.ConstCount != null)
                    {
                        var arrayPointer = InitializeConstArray(function, block, pointer, arrayStruct, field.TypeDefinition.ConstCount.Value, field.ArrayElementType);

                        if (field.ArrayValues != null)
                        {
                            InitializeArrayValues(function, block, arrayPointer, field.ArrayElementType, field.ArrayValues, scope);
                        }
                    }
                    else
                    {
                        var lengthPointer = EmitGetStructPointer(block, pointer, arrayStruct, 0);
                        var lengthValue = GetDefaultConstant(_s32Type);
                        EmitStore(block, lengthPointer, lengthValue);
                    }
                    break;
                case TypeKind.CArray:
                    if (field.ArrayValues != null)
                    {
                        InitializeArrayValues(function, block, pointer, field.ArrayElementType, field.ArrayValues, scope);
                    }
                    break;
                // Initialize struct field default values
                case TypeKind.Struct:
                case TypeKind.String: // TODO String default values
                    InitializeStruct(function, block, (StructAst)field.Type, pointer, scope, field.Assignments);
                    break;
                // Initialize pointers to null
                case TypeKind.Pointer:
                    EmitStore(block, pointer, new InstructionValue {ValueType = InstructionValueType.Null});
                    break;
                // Or initialize to default
                default:
                    var defaultValue = field.Value == null ? GetDefaultConstant(field.Type) : EmitIR(function, field.Value, scope, block);
                    EmitStore(block, pointer, defaultValue);
                    break;
            }
        }

        private InstructionValue GetDefaultConstant(IType type)
        {
            var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = type};
            switch (type.TypeKind)
            {
                case TypeKind.Boolean:
                    value.ConstantValue = new Constant {Boolean = false};
                    break;
                case TypeKind.Integer:
                    value.ConstantValue = new Constant {Integer = 0};
                    break;
                case TypeKind.Float:
                    if (type.Size == 4)
                    {
                        value.ConstantValue = new Constant {Float = 0};
                    }
                    else
                    {
                        value.ConstantValue = new Constant {Double = 0};
                    }
                    break;
            }
            return value;
        }

        public void EmitAssignment(FunctionIR function, AssignmentAst assignment, ScopeAst scope)
        {
            var block = function.BasicBlocks[^1];
            var pointer = EmitGetReference(function, assignment.Reference, scope, block);

            var value = EmitIR(function, assignment.Value, scope, block);
            if (assignment.Operator != Operator.None)
            {
                // TODO Translate BuildExpression from LLVMBackend
                var previousValue = EmitLoad(block, pointer.Type, pointer);
                // value = EmitExpression(block, previousValue, value);
            }

            EmitStore(block, pointer, value);
        }

        public void EmitReturn(FunctionIR function, ReturnAst returnAst, IType returnType, ScopeAst scope, BasicBlock block = null)
        {
            if (block == null)
            {
                block = function.BasicBlocks[^1];
            }

            var instruction = new Instruction {Type = InstructionType.Return};
            if (returnAst.Value != null)
            {
                instruction.Value1 = EmitIR(function, returnAst.Value, scope, block);
            }

            block.Instructions.Add(instruction);
        }

        public InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block = null, bool useRawString = false)
        {
            if (block == null)
            {
                block = function.BasicBlocks[^1];
            }
            switch (ast)
            {
                case ConstantAst constant:
                    return GetConstant(constant, useRawString);
                case NullAst nullAst:
                    return new InstructionValue {ValueType = InstructionValueType.Null};
                case IdentifierAst identifierAst:
                    var identifier = GetScopeIdentifier(scope, identifierAst.Name, out var global);
                    if (identifier is DeclarationAst declaration)
                    {
                        if (declaration.Constant)
                        {
                            var constantValue = global ? Program.Constants[declaration.Name] : function.Constants[declaration.Name];
                            if (useRawString && constantValue.Type?.TypeKind == TypeKind.String)
                            {
                                return new InstructionValue
                                {
                                    ValueType = InstructionValueType.Constant, Type = constantValue.Type,
                                    ConstantString = constantValue.ConstantString, UseRawString = true
                                };
                            }
                            return constantValue;
                        }

                        if (useRawString && declaration.Type.TypeKind == TypeKind.String)
                        {
                            _stringStruct ??= (StructAst) declaration.Type;
                            var dataField = _stringStruct.Fields[1];

                            var stringPointer = EmitGetPointer(block, declaration.AllocationIndex, _stringStruct, global);
                            var dataPointer = EmitGetStructPointer(block, stringPointer, _stringStruct, 1, dataField);
                            return EmitLoad(block, dataField.Type, dataPointer);
                        }
                        return EmitLoad(block, declaration.Type, allocationIndex: declaration.AllocationIndex, global: global);
                    }
                    else if (identifierAst is IType type)
                    {
                        return GetConstantInteger(type.TypeIndex);
                    }
                    else
                    {
                        return GetConstantInteger(TypeTable.Functions[identifierAst.Name][0].TypeIndex);
                    }
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)structField.Types[0];
                        var enumValue = enumDef.Values[structField.ValueIndices[0]].Value;

                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = enumDef.BaseType,
                            ConstantValue = new Constant {Integer = enumValue}
                        };
                    }
                    else if (structField.IsConstant)
                    {
                        return EmitIR(function, structField.ConstantValue, scope, block);
                    }
                    var structFieldPointer = EmitGetStructPointer(function, structField, scope, block, out var loaded);
                    if (!loaded)
                    {
                        if (useRawString && structFieldPointer.Type.TypeKind == TypeKind.String)
                        {
                            _stringStruct ??= (StructAst) TypeTable.Types["string"];
                            var dataField = _stringStruct.Fields[1];

                            var dataPointer = EmitGetStructPointer(block, structFieldPointer, _stringStruct, 1, dataField);
                            return EmitLoad(block, dataField.Type, dataPointer);
                        }
                        return EmitLoad(block, structFieldPointer.Type, structFieldPointer);
                    }
                    return structFieldPointer;
                case CallAst call:
                    return EmitCall(function, call, scope, block);
                case ChangeByOneAst changeByOne:
                    var pointer = EmitGetReference(function, changeByOne.Value, scope, block);
                    var previousValue = EmitLoad(block, pointer.Type, pointer);

                    var constOne = new InstructionValue {ValueType = InstructionValueType.Constant, Type = changeByOne.Type};
                    if (changeByOne.Type.TypeKind == TypeKind.Integer)
                    {
                        constOne.ConstantValue = new Constant {Integer = 1};
                    }
                    else if (changeByOne.Type.Size == 4)
                    {
                        constOne.ConstantValue = new Constant {Float = 1};
                    }
                    else
                    {
                        constOne.ConstantValue = new Constant {Double = 1};
                    }
                    var instructionType = changeByOne.Positive ? InstructionType.Add : InstructionType.Subtract;
                    var newValue = EmitInstruction(instructionType, block, changeByOne.Type, previousValue, constOne);
                    EmitStore(block, pointer, newValue);

                    return changeByOne.Prefix ? newValue : previousValue;
                case UnaryAst unary:
                    InstructionValue value;
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not:
                            value = EmitIR(function, unary.Value, scope, block);
                            return EmitInstruction(InstructionType.Not, block, value.Type, value);
                        case UnaryOperator.Negate:
                            // TODO Get should there be different instructions for integers and floats?
                            value = EmitIR(function, unary.Value, scope, block);
                            return EmitInstruction(InstructionType.Negate, block, value.Type, value);
                        case UnaryOperator.Dereference:
                            value = EmitIR(function, unary.Value, scope, block);
                            return EmitLoad(block, value.Type, value);
                        case UnaryOperator.Reference:
                            return EmitGetReference(function, unary.Value, scope, block);
                    }
                    break;
                case IndexAst index:
                    var indexPointer = EmitGetIndexPointer(function, index, scope, block);

                    return index.CallsOverload ? indexPointer : EmitLoad(block, indexPointer.Type, indexPointer);
                case ExpressionAst expression:
                    // var expressionValue = WriteExpression(expression.Children[0], localVariables);
                    // for (var i = 1; i < expression.Children.Count; i++)
                    // {
                    //     var rhs = WriteExpression(expression.Children[i], localVariables);
                    //     expressionValue.value = BuildExpression(expressionValue, rhs, expression.Operators[i - 1], expression.ResultingTypes[i - 1]);
                    //     expressionValue.type = expression.ResultingTypes[i - 1];
                    // }
                    // return expressionValue;
                    return new InstructionValue();
                case TypeDefinition typeDef:
                    return GetConstantInteger(typeDef.TypeIndex.Value);
                case CastAst cast:
                    var castValue = EmitIR(function, cast.Value, scope);
                    return EmitCastValue(block, castValue, cast.TargetType);
            }
            return null;
        }

        private InstructionValue EmitConstantIR(IAst ast, ScopeAst scope, FunctionIR function = null)
        {
            switch (ast)
            {
                case ConstantAst constant:
                    return GetConstant(constant);
                case NullAst nullAst:
                    return new InstructionValue {ValueType = InstructionValueType.Null};
                case IdentifierAst identifierAst:
                    var identifier = GetScopeIdentifier(scope, identifierAst.Name, out var global);
                    if (identifier is DeclarationAst declaration)
                    {
                        if (declaration.Constant)
                        {
                            return global ? Program.Constants[declaration.Name] : function?.Constants[declaration.Name];
                        }

                        return null;
                    }
                    else if (identifierAst is IType type)
                    {
                        return GetConstantInteger(type.TypeIndex);
                    }
                    else
                    {
                        return GetConstantInteger(TypeTable.Functions[identifierAst.Name][0].TypeIndex);
                    }
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)structField.Types[0];
                        var enumValue = enumDef.Values[structField.ValueIndices[0]].Value;

                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = enumDef.BaseType,
                            ConstantValue = new Constant {Integer = enumValue}
                        };
                    }
                    break;
            }
            return null;
        }

        private InstructionValue GetConstant(ConstantAst constant, bool useRawString = false)
        {
            var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = constant.Type};
            switch (constant.TypeDefinition.TypeKind)
            {
                case TypeKind.Boolean:
                    value.ConstantValue = new Constant {Boolean = constant.Value == "true"};
                    break;
                case TypeKind.String:
                    value.ConstantString = constant.Value;
                    value.UseRawString = useRawString;
                    break;
                case TypeKind.Integer:
                    if (constant.TypeDefinition.Character)
                    {
                        value.ConstantValue = new Constant {UnsignedInteger = (byte)constant.Value[0]};
                    }
                    else if (constant.TypeDefinition.PrimitiveType.Signed)
                    {
                        value.ConstantValue = new Constant {Integer = long.Parse(constant.Value)};
                    }
                    else
                    {
                        value.ConstantValue = new Constant {UnsignedInteger = ulong.Parse(constant.Value)};
                    }
                    break;
                case TypeKind.Float:
                    if (constant.TypeDefinition.PrimitiveType.Bytes == 4)
                    {
                        value.ConstantValue = new Constant {Float = float.Parse(constant.Value)};
                    }
                    else
                    {
                        value.ConstantValue = new Constant {Double = double.Parse(constant.Value)};
                    }
                    break;
            }
            return value;
        }

        private InstructionValue EmitGetReference(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block)
        {
            switch (ast)
            {
                case IdentifierAst identifier:
                    var declaration = (DeclarationAst) GetScopeIdentifier(scope, identifier.Name, out var global);
                    return EmitGetPointer(block, declaration.AllocationIndex, declaration.Type, global);
                case StructFieldRefAst structField:
                    return EmitGetStructPointer(function, structField, scope, block, out _);
                case IndexAst index:
                    return EmitGetIndexPointer(function, index, scope, block);
                case UnaryAst unary:
                    return EmitIR(function, unary.Value, scope, block);
            }
            return null;
        }

        private InstructionValue EmitGetStructPointer(FunctionIR function, StructFieldRefAst structField, ScopeAst scope, BasicBlock block, out bool loaded)
        {
            loaded = false;
            InstructionValue value = null;

            switch (structField.Children[0])
            {
                case IdentifierAst identifier:
                    var declaration = (DeclarationAst) GetScopeIdentifier(scope, identifier.Name, out var global);
                    value = EmitGetPointer(block, declaration.AllocationIndex, declaration.Type, global);
                    break;
                case IndexAst index:
                    value = EmitGetIndexPointer(function, index, scope, block);
                    if (index.CallsOverload && !structField.Pointers[0])
                    {
                        var type = structField.Types[0];
                        var allocationIndex = AddAllocation(function, type);
                        EmitStore(block, allocationIndex, value);
                        value = EmitGetPointer(block, allocationIndex, type);
                    }
                    break;
                case CallAst call:
                    value = EmitCall(function, call, scope, block);
                    if (!structField.Pointers[0])
                    {
                        var type = structField.Types[0];
                        var allocationIndex = AddAllocation(function, type);
                        EmitStore(block, allocationIndex, value);
                        value = EmitGetPointer(block, allocationIndex, type);
                    }
                    break;
                default:
                    // @Cleanup this branch shouldn't be hit
                    Console.WriteLine("Unexpected syntax tree in struct field ref");
                    Environment.Exit(ErrorCodes.BuildError);
                    break;
            }

            var skipPointer = false;
            for (var i = 1; i < structField.Children.Count; i++)
            {
                var type = structField.Types[i-1];

                if (structField.Pointers[i-1])
                {
                    if (!skipPointer)
                    {
                        value = EmitLoad(block, type, value);
                    }
                }
                skipPointer = false;

                if (type.TypeKind == TypeKind.CArray)
                {
                    if (structField.Children[i] is IndexAst index)
                    {
                        var indexValue = EmitIR(function, index.Index, scope, block);
                        var arrayType = (ArrayType) type;
                        var elementType = arrayType.ElementType;
                        value = EmitGetPointer(block, value, indexValue, elementType, true);
                    }
                }
                else
                {
                    var structDefinition = (StructAst) type;
                    var fieldIndex = structField.ValueIndices[i-1];
                    var field = structDefinition.Fields[fieldIndex];

                    value = EmitGetStructPointer(block, value, structDefinition, fieldIndex, field);
                    if (structField.Children[i] is IndexAst index)
                    {
                        value = EmitGetIndexPointer(function, index, scope, block, field.Type, value);

                        if (index.CallsOverload)
                        {
                            skipPointer = true;
                            if (i < structField.Pointers.Length && !structField.Pointers[i])
                            {
                                var nextType = structField.Types[i];
                                var allocationIndex = AddAllocation(function, nextType);
                                EmitStore(block, allocationIndex, value);
                                value = EmitGetPointer(block, allocationIndex, nextType);
                            }
                            else if (i == structField.Pointers.Length)
                            {
                                loaded = true;
                            }
                        }
                    }
                }
            }

            return value;
        }

        private InstructionValue EmitCall(FunctionIR function, CallAst call, ScopeAst scope, BasicBlock block)
        {
            var argumentCount = call.Function.Varargs ? call.Arguments.Count : call.Function.Arguments.Count;
            var arguments = new InstructionValue[argumentCount];

            if (call.Function.Params)
            {
                for (var i = 0; i < argumentCount - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
                    arguments[i] = EmitCastValue(block, argument, call.Function.Arguments[i].Type);
                }

                // Rollup the rest of the arguments into an array
                var paramsType = call.Function.Arguments[^1].Type;
                var elementType = call.Function.ParamsElementType;
                var paramsAllocationIndex = AddAllocation(function, paramsType);
                var paramsPointer = EmitGetPointer(block, paramsAllocationIndex, paramsType);
                var dataPointer = InitializeConstArray(function, block, paramsPointer, (StructAst)paramsType, (uint)(call.Arguments.Count - call.Function.Arguments.Count + 1), elementType);

                uint paramsIndex = 0;
                for (var i = call.Function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                {
                    var index = GetConstantInteger(paramsIndex);
                    var pointer = EmitGetPointer(block, dataPointer, index, elementType, true);

                    var value = EmitIR(function, call.Arguments[i], scope, block);
                    EmitStore(block, pointer, EmitCastValue(block, value, elementType));
                }

                var paramsValue = EmitLoad(block, paramsType, allocationIndex: paramsAllocationIndex);
                arguments[argumentCount - 1] = paramsValue;
            }
            else if (call.Function.Varargs)
            {
                var i = 0;
                for (; i < call.Function.Arguments.Count - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block, true);
                    arguments[i] = EmitCastValue(block, argument, call.Function.Arguments[i].Type);
                }

                // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                for (; i < argumentCount; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block, true);
                    // TODO Make this work
                    // if (argument.Type.TypeKind == TypeKind.Float && argument.Type.Size == 4)
                    // {
                    //     argument = EmitCastValue(block, argument, _float64Type);
                    // }
                    arguments[i] = argument;
                }
            }
            else
            {
                for (var i = 0; i < argumentCount - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block, call.Function.Extern);
                    arguments[i] = EmitCastValue(block, argument, call.Function.Arguments[i].Type);
                }
            }

            return EmitCall(block, GetFunctionName(call.Function), arguments, call.Function.ReturnType);
        }

        private InstructionValue EmitGetIndexPointer(FunctionIR function, IndexAst index, ScopeAst scope, BasicBlock block, IType type = null, InstructionValue variable = null)
        {
            if (type == null)
            {
                var declaration = (DeclarationAst) GetScopeIdentifier(scope, index.Name, out var global);
                type = declaration.Type;
                variable = EmitGetPointer(block, declaration.AllocationIndex, type, global);
            }

            var indexValue = EmitIR(function, index.Index, scope, block);

            if (index.CallsOverload)
            {
                var overloadName = GetOperatorOverloadName(index.Overload.Type, Operator.Subscript);

                var value = EmitLoad(block, type, variable);
                return EmitCall(block, overloadName, new []{value, indexValue}, index.Overload.ReturnType);
            }

            IType elementType;
            if (type.TypeKind == TypeKind.Pointer)
            {
                var pointerType = (PrimitiveAst)type;
                elementType = pointerType.PointerType;

                var dataPointer = EmitLoad(block, type, variable);
                return EmitGetPointer(block, dataPointer, indexValue, elementType);
            }
            else if (type.TypeKind == TypeKind.CArray)
            {
                var arrayType = (ArrayType)type;
                elementType = arrayType.ElementType;

                return EmitGetPointer(block, variable, indexValue, elementType, true);
            }
            else
            {
                var structAst = (StructAst)type;
                var dataField = structAst.Fields[1];
                if (type.TypeKind == TypeKind.String)
                {
                    elementType = _u8Type;
                }
                else
                {
                    var pointerType = (PrimitiveAst)dataField.Type;
                    elementType = pointerType.PointerType;
                }

                var data = EmitGetStructPointer(block, variable, structAst, 1, dataField);
                var dataPointer = EmitLoad(block, data.Type, data);
                return EmitGetPointer(block, dataPointer, indexValue, elementType);
            }
        }

        private InstructionValue EmitCastValue(BasicBlock block, InstructionValue value, IType type)
        {
            if (value.Type == type)
            {
                return value;
            }

            var targetType = new InstructionValue {ValueType = InstructionValueType.Type, Type = type};
            var castInstruction = new Instruction {Type = InstructionType.Cast, Value1 = value, Value2 = targetType};

            var valueIndex = block.Instructions.Count;
            block.Instructions.Add(castInstruction);
            return new InstructionValue {ValueIndex = valueIndex, Type = type};
        }

        private InstructionValue GetConstantInteger(int value)
        {
            return new InstructionValue {ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = new Constant {Integer = value}};
        }

        private InstructionValue GetConstantInteger(uint value)
        {
            return new InstructionValue {ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = new Constant {Integer = value}};
        }

        private IAst GetScopeIdentifier(ScopeAst scope, string name, out bool global)
        {
            do {
                if (scope.Identifiers.TryGetValue(name, out var ast))
                {
                    global = scope.Parent == null;
                    return ast;
                }
                scope = scope.Parent;
            } while (scope != null);
            global = false;
            return null;
        }

        private string GetFunctionName(FunctionAst function)
        {
            return function.Name switch
            {
                "main" => "__main",
                "__start" => "main",
                _ => function.OverloadIndex > 0 ? $"{function.Name}.{function.OverloadIndex}" : function.Name
            };
        }

        private string GetOperatorOverloadName(TypeDefinition type, Operator op)
        {
            return $"operator.{op}.{type.GenericName}";
        }

        private InstructionValue EmitLoad(BasicBlock block, IType type, InstructionValue value = null, int? allocationIndex = null, bool global = false)
        {
            var loadInstruction = new Instruction {Type = InstructionType.Load, Index = allocationIndex, Global = global, Value1 = value};
            return AddInstruction(block, loadInstruction, type);
        }

        // TODO For index value, calculate the size of the element
        private InstructionValue EmitGetPointer(BasicBlock block, InstructionValue pointer, InstructionValue index, IType type, bool getFirstPointer = false)
        {
            var instruction = new Instruction
            {
                Type = InstructionType.GetPointer, Value1 = pointer,
                Value2 = index, GetFirstPointer = getFirstPointer
            };
            return AddInstruction(block, instruction, type);
        }

        public InstructionValue EmitGetPointer(BasicBlock block, int allocationIndex, IType type, bool global = false)
        {
            var instruction = new Instruction {Type = InstructionType.GetPointer, Index = allocationIndex, Global = global};
            return AddInstruction(block, instruction, type);
        }

        // TODO Add the offset size
        private InstructionValue EmitGetStructPointer(BasicBlock block, InstructionValue value, StructAst structDef, int fieldIndex, StructFieldAst field = null)
        {
            if (field == null)
            {
                 field = structDef.Fields[fieldIndex];
            }

            var instruction = new Instruction {Type = InstructionType.GetStructPointer, Index = fieldIndex, Value1 = value};
            return AddInstruction(block, instruction, field.Type);
        }

        private InstructionValue EmitCall(BasicBlock block, string name, InstructionValue[] arguments, IType returnType)
        {
            var callInstruction = new Instruction
            {
                Type = InstructionType.Call, CallFunction = name,
                Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Values = arguments}
            };
            return AddInstruction(block, callInstruction, returnType);
        }

        private void EmitStore(BasicBlock block, int allocationIndex, InstructionValue value)
        {
            var store = new Instruction {Type = InstructionType.Store, Index = allocationIndex, Value1 = value};
            block.Instructions.Add(store);
        }

        private void EmitStore(BasicBlock block, InstructionValue pointer, InstructionValue value)
        {
            var store = new Instruction {Type = InstructionType.Store, Value1 = pointer, Value2 = value};
            block.Instructions.Add(store);
        }

        private InstructionValue EmitInstruction(InstructionType instructionType, BasicBlock block, IType type, InstructionValue value1, InstructionValue value2 = null)
        {
            var instruction = new Instruction {Type = instructionType, Value1 = value1, Value2 = value2};
            return AddInstruction(block, instruction, type);
        }

        private InstructionValue AddInstruction(BasicBlock block, Instruction instruction, IType type)
        {
            var value = new InstructionValue {ValueIndex = block.Instructions.Count, Type = type};
            block.Instructions.Add(instruction);
            return value;
        }
    }
}
