using System;
using System.Collections.Generic;

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
        InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block = null);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        private IType _u8Type;
        private IType _s32Type;
        private IType _float64Type;

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
                var globalVariable = new GlobalVariable
                {
                    Name = declaration.Name, Index = globalIndex,
                    Size = declaration.Type.Size, Type = declaration.Type
                };
                Program.GlobalVariables.Add(globalVariable);

                // TODO Add initialization values
                if (declaration.Value != null)
                {
                    // value = CastValue(ExecuteExpression(declaration.Value, variables).Value, declaration.Type);
                }
                else if (declaration.ArrayValues != null)
                {
                    // value = InitializeArray(declaration.Type, variables, declaration.ArrayValues);
                }
                else
                {
                    // value = GetUninitializedValue(declaration.Type, variables, declaration.Assignments);
                }
            }
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
                    var value = EmitIR(function, declaration.Value, scope, block); // TODO CastValue
                    EmitStore(block, allocationIndex, value);
                    return;
                }

                switch (declaration.TypeDefinition.TypeKind)
                {
                    // Initialize arrays
                    case TypeKind.Array:
                        var pointer = EmitGetPointer(block, allocationIndex: allocationIndex);
                        var arrayStruct = (StructAst)declaration.Type;
                        if (declaration.TypeDefinition.ConstCount != null)
                        {
                            var arrayPointer = InitializeConstArray(function, block, pointer, arrayStruct, declaration.TypeDefinition.ConstCount.Value);

                            if (declaration.ArrayValues != null)
                            {
                                InitializeArrayValues(function, block, arrayPointer, declaration.ArrayValues, scope);
                            }
                        }
                        else if (declaration.TypeDefinition.Count != null)
                        {
                            // TODO Implement me
                            // BuildStackSave();
                            // var (_, count) = WriteExpression(declaration.TypeDefinition.Count, localVariables);

                            // var countPointer = _builder.BuildStructGEP(variable, 0, "countptr");
                            // LLVM.BuildStore(_builder, count, countPointer);

                            // var targetType = ConvertTypeDefinition(elementType);
                            // var arrayData = _builder.BuildArrayAlloca(targetType, count, "arraydata");
                            // var dataPointer = _builder.BuildStructGEP(variable, 1, "dataptr");
                            // LLVM.BuildStore(_builder, arrayData, dataPointer);
                        }
                        else
                        {
                            var lengthPointer = EmitGetStructPointer(block, pointer, arrayStruct, 0);
                            var lengthValue = GetDefaultConstant(_s32Type);
                            EmitStore(block, lengthPointer, lengthValue);
                        }
                        break;
                    case TypeKind.CArray:
                        var cArrayPointer = EmitGetPointer(block, allocationIndex: allocationIndex);
                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(function, block, cArrayPointer, declaration.ArrayValues, scope);
                        }
                        break;
                    // Initialize struct field default values
                    case TypeKind.Struct:
                    case TypeKind.String:
                        var structPointer = EmitGetPointer(block, allocationIndex: allocationIndex);
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

        private InstructionValue InitializeConstArray(FunctionIR function, BasicBlock block, InstructionValue arrayPointer, StructAst arrayStruct, uint length, IType elementType = null)
        {
            var lengthPointer = EmitGetStructPointer(block, arrayPointer, arrayStruct, 0);
            var lengthValue = new InstructionValue {ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = new InstructionConstant {Integer = length}};
            EmitStore(block, lengthPointer, lengthValue);

            if (elementType == null)
            {
                var pointerType = (PrimitiveAst)arrayStruct.Fields[1].Type;
                elementType = pointerType.PointerType;
            }
            var dataIndex = AddAllocation(function, elementType, true, length);
            var arrayDataPointer = EmitGetPointer(block, allocationIndex: dataIndex);
            var dataPointer = EmitGetStructPointer(block, arrayPointer, arrayStruct, 1);
            EmitStore(block, dataPointer, arrayDataPointer);

            return arrayDataPointer;
        }

        private void InitializeArrayValues(FunctionIR function, BasicBlock block, InstructionValue arrayPointer, List<IAst> arrayValues, ScopeAst scope)
        {
            for (var i = 0; i < arrayValues.Count; i++)
            {
                var index = new InstructionValue {ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = new InstructionConstant {Integer = i}};
                var pointer = EmitGetPointer(block, arrayPointer, index, getFirstPointer: true);

                var value = EmitIR(function, arrayValues[i], scope, block);
                // TODO Cast value
                EmitStore(block, pointer, value);
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
                        var arrayPointer = InitializeConstArray(function, block, pointer, arrayStruct, field.TypeDefinition.ConstCount.Value);

                        if (field.ArrayValues != null)
                        {
                            InitializeArrayValues(function, block, arrayPointer, field.ArrayValues, scope);
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
                        InitializeArrayValues(function, block, pointer, field.ArrayValues, scope);
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
                    value.ConstantValue = new InstructionConstant {Boolean = false};
                    break;
                case TypeKind.Integer:
                    value.ConstantValue = new InstructionConstant {Integer = 0};
                    break;
                case TypeKind.Float:
                    if (type.Size == 4)
                    {
                        value.ConstantValue = new InstructionConstant {Float = 0};
                    }
                    else
                    {
                        value.ConstantValue = new InstructionConstant {Double = 0};
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
                // TODO Translate BulidExpression from LLVMBackend
                // var previousValue = EmitLoad(block, pointer);
                // value = EmitExpression(block, previousValue, value);
            }

            EmitInstruction(InstructionType.Store, block, pointer, value);
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

        public InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block = null)
        {
            if (block == null)
            {
                block = function.BasicBlocks[^1];
            }
            switch (ast)
            {
                case ConstantAst constantAst:
                    return GetConstant(constantAst);
                case NullAst nullAst:
                    return new InstructionValue {ValueType = InstructionValueType.Null};
                case IdentifierAst identifierAst:
                    var identifier = GetScopeIdentifier(scope, identifierAst.Name, out var global);
                    if (identifier is DeclarationAst declaration)
                    {
                        if (declaration.Constant)
                        {
                            return global ? Program.Constants[declaration.Name] : function.Constants[declaration.Name];
                        }

                        return EmitLoad(block, declaration.Type, allocationIndex: declaration.AllocationIndex, global: global);
                    }
                    else if (identifierAst is IType type)
                    {
                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = _s32Type,
                            ConstantValue = new InstructionConstant {Integer = type.TypeIndex}
                        };
                    }
                    else
                    {
                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = _s32Type,
                            ConstantValue = new InstructionConstant {Integer = TypeTable.Functions[identifierAst.Name][0].TypeIndex}
                        };
                    }
                    // TODO Implement getStringPointer
                    // if (type.TypeKind == TypeKind.String)
                    // {
                    //     if (getStringPointer)
                    //     {
                    //         value = _builder.BuildStructGEP(value, 1, "stringdata");
                    //     }
                    //     value = _builder.BuildLoad(value, identifier.Name);
                    // }
                    // else if (!type.Constant)
                    // {
                    //     value = _builder.BuildLoad(value, identifier.Name);
                    // }
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)structField.Types[0];
                        var enumValue = enumDef.Values[structField.ValueIndices[0]].Value;

                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = enumDef.BaseType,
                            ConstantValue = new InstructionConstant {Integer = enumValue}
                        };
                    }
                    else if (structField.IsConstant)
                    {
                        return EmitIR(function, structField.ConstantValue, scope, block);
                    }
                    var structFieldPointer = EmitGetStructPointer(function, structField, scope, block, out var loaded);
                    if (!loaded)
                    {
                        // TODO Implement getStringPointer
                        // if (getStringPointer && type.TypeKind == TypeKind.String)
                        // {
                        //     field = _builder.BuildStructGEP(field, 1, "stringdata");
                        // }
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
                        constOne.ConstantValue = new InstructionConstant {Integer = 1};
                    }
                    else if (changeByOne.Type.Size == 4)
                    {
                        constOne.ConstantValue = new InstructionConstant {Float = 1};
                    }
                    else
                    {
                        constOne.ConstantValue = new InstructionConstant {Double = 1};
                    }
                    var instructionType = changeByOne.Positive ? InstructionType.Add : InstructionType.Subtract;
                    var newValue = EmitInstruction(instructionType, block, previousValue, constOne);

                    EmitInstruction(InstructionType.Store, block, pointer, newValue);

                    return changeByOne.Prefix ? newValue : previousValue;
                case UnaryAst unary:
                    InstructionValue value;
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not:
                            value = EmitIR(function, unary.Value, scope, block);
                            return EmitInstruction(InstructionType.Not, block, value);
                        case UnaryOperator.Negate:
                            // TODO Get should there be different instructions for integers and floats?
                            value = EmitIR(function, unary.Value, scope, block);
                            return EmitInstruction(InstructionType.Negate, block, value);
                        case UnaryOperator.Dereference:
                            value = EmitIR(function, unary.Value, scope, block);
                            return EmitLoad(block, value.Type, value);
                        case UnaryOperator.Reference:
                            // TODO Get type?
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
                    return new InstructionValue
                    {
                        ValueType = InstructionValueType.Constant, Type = _s32Type,
                        ConstantValue = new InstructionConstant {Integer = typeDef.TypeIndex.Value}
                    };
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
                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = _s32Type,
                            ConstantValue = new InstructionConstant {Integer = (uint)type.TypeIndex}
                        };
                    }
                    else
                    {
                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = _s32Type,
                            ConstantValue = new InstructionConstant {Integer = TypeTable.Functions[identifierAst.Name][0].TypeIndex}
                        };
                    }
                    // TODO Implement getStringPointer
                    // if (type.TypeKind == TypeKind.String)
                    // {
                    //     if (getStringPointer)
                    //     {
                    //         value = _builder.BuildStructGEP(value, 1, "stringdata");
                    //     }
                    //     value = _builder.BuildLoad(value, identifier.Name);
                    // }
                    // else if (!type.Constant)
                    // {
                    //     value = _builder.BuildLoad(value, identifier.Name);
                    // }
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)structField.Types[0];
                        var enumValue = enumDef.Values[structField.ValueIndices[0]].Value;

                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = enumDef.BaseType,
                            ConstantValue = new InstructionConstant {Integer = enumValue}
                        };
                    }
                    break;
            }
            return null;
        }

        private InstructionValue GetConstant(ConstantAst constant)
        {
            var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = constant.Type};
            switch (constant.TypeDefinition.TypeKind)
            {
                case TypeKind.Boolean:
                    value.ConstantValue = new InstructionConstant {Boolean = constant.Value == "true"};
                    break;
                case TypeKind.String:
                    value.ConstantString = constant.Value;
                    break;
                case TypeKind.Integer:
                    if (constant.TypeDefinition.Character)
                    {
                        value.ConstantValue = new InstructionConstant {UnsignedInteger = (byte)constant.Value[0]};
                    }
                    else if (constant.TypeDefinition.PrimitiveType.Signed)
                    {
                        value.ConstantValue = new InstructionConstant {Integer = long.Parse(constant.Value)};
                    }
                    else
                    {
                        value.ConstantValue = new InstructionConstant {UnsignedInteger = ulong.Parse(constant.Value)};
                    }
                    break;
                case TypeKind.Float:
                    if (constant.TypeDefinition.PrimitiveType.Bytes == 4)
                    {
                        value.ConstantValue = new InstructionConstant {Float = float.Parse(constant.Value)};
                    }
                    else
                    {
                        value.ConstantValue = new InstructionConstant {Double = double.Parse(constant.Value)};
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
                    return EmitGetPointer(block, allocationIndex: declaration.AllocationIndex, global: global);
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
                    value = EmitGetPointer(block, allocationIndex: declaration.AllocationIndex, global: global);
                    break;
                case IndexAst index:
                    value = EmitGetIndexPointer(function, index, scope, block);
                    if (index.CallsOverload && !structField.Pointers[0])
                    {
                        var allocationIndex = AddAllocation(function, structField.Types[0]);
                        EmitStore(block, allocationIndex, value);
                        value = EmitGetPointer(block, allocationIndex: allocationIndex);
                    }
                    break;
                case CallAst call:
                    value = EmitCall(function, call, scope, block);
                    if (!structField.Pointers[0])
                    {
                        var allocationIndex = AddAllocation(function, structField.Types[0]);
                        EmitStore(block, allocationIndex, value);
                        value = EmitGetPointer(block, allocationIndex: allocationIndex);
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
                                var allocationIndex = AddAllocation(function, structField.Types[i]);
                                EmitStore(block, allocationIndex, value);
                                value = EmitGetPointer(block, allocationIndex: allocationIndex);
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
                var paramsPointer = EmitGetPointer(block, allocationIndex: paramsAllocationIndex);
                var dataPointer = InitializeConstArray(function, block, paramsPointer, (StructAst)paramsType, (uint)(call.Arguments.Count - call.Function.Arguments.Count + 1), elementType);

                uint paramsIndex = 0;
                for (var i = call.Function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                {
                    var index = new InstructionValue {ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = new InstructionConstant {Integer = paramsIndex}};
                    var pointer = EmitGetPointer(block, dataPointer, index, getFirstPointer: true);

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
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
                    arguments[i] = EmitCastValue(block, argument, call.Function.Arguments[i].Type);
                }

                // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                for (; i < argumentCount; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
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
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
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
                variable = EmitGetPointer(block, allocationIndex: declaration.AllocationIndex, global: global);
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
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = type};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        // TODO For index value, calculate the size of the element
        private InstructionValue EmitGetPointer(BasicBlock block, InstructionValue pointer = null, InstructionValue index = null, IType type = null, bool getFirstPointer = false, int? allocationIndex = null, bool global = false)
        {
            var loadInstruction = new Instruction
            {
                Type = InstructionType.GetPointer, Index = allocationIndex, Global = global,
                Value1 = pointer, Value2 = index, GetFirstPointer = getFirstPointer
            };
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = type};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        // TODO Add the offset size
        private InstructionValue EmitGetStructPointer(BasicBlock block, InstructionValue value, StructAst structDef, int fieldIndex, StructFieldAst field = null)
        {
            if (field == null)
            {
                 field = structDef.Fields[fieldIndex];
            }

            var loadInstruction = new Instruction {Type = InstructionType.GetStructPointer, Index = fieldIndex, Value1 = value};
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = field.Type};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        private InstructionValue EmitCall(BasicBlock block, string name, InstructionValue[] arguments, IType returnType)
        {
            var callInstruction = new Instruction
            {
                Type = InstructionType.Call, CallFunction = name,
                Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Arguments = arguments}
            };
            var callValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = returnType};
            block.Instructions.Add(callInstruction);
            return callValue;
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

        private InstructionValue EmitInstruction(InstructionType type, BasicBlock block, InstructionValue value1, InstructionValue value2 = null)
        {
            var instruction = new Instruction {Type = type, Value1 = value1, Value2 = value2};
            var value = new InstructionValue {ValueIndex = block.Instructions.Count};
            block.Instructions.Add(instruction);
            return value;
        }
    }
}
