using System;
using System.Collections.Generic;
using System.Linq;

namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }
        void Init();
        void AddFunction(FunctionAst function);
        void AddOperatorOverload(OperatorOverloadAst overload);
        void EmitGlobalVariable(DeclarationAst declaration, ScopeAst scope);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        private IType _boolType;
        private IType _u8Type;
        private IType _s32Type;
        private IType _float64Type;
        private StructAst _stringStruct;

        public ProgramIR Program { get; } = new();

        public void Init()
        {
            _boolType = TypeTable.Types["bool"];
            _u8Type = TypeTable.Types["u8"];
            _s32Type = TypeTable.Types["s32"];
            _float64Type = TypeTable.Types["float64"];
        }

        public void AddFunction(FunctionAst function)
        {
            var functionName = GetFunctionName(function);

            var functionIR = new FunctionIR();

            if (functionName == "main")
            {
                Program.EntryPoint = functionIR;
            }
            else
            {
                Program.Functions[functionName] = functionIR;
            }

            if (!function.Extern && !function.Compiler)
            {
                functionIR.Allocations = new();
                functionIR.Instructions = new();
                functionIR.BasicBlocks = new();

                var entryBlock = AddBasicBlock(functionIR);

                for (var i = 0; i < function.Arguments.Count; i++)
                {
                    var argument = function.Arguments[i];
                    var allocationIndex = AddAllocation(functionIR, argument);

                    EmitStore(functionIR, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i});
                }

                EmitScope(functionIR, entryBlock, function.Body, function.ReturnType);

                if (function.ReturnVoidAtEnd)
                {
                    functionIR.Instructions.Add(new Instruction {Type = InstructionType.Return});
                }

                if (function.PrintIR)
                {
                    PrintFunction(function.Name, functionIR);
                }
            }
        }

        public void AddOperatorOverload(OperatorOverloadAst overload)
        {
            var functionName = GetOperatorOverloadName(overload.Type, overload.Operator);

            var functionIR = new FunctionIR {Allocations = new(), Instructions = new(), BasicBlocks = new()};
            var entryBlock = AddBasicBlock(functionIR);

            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                var allocationIndex = AddAllocation(functionIR, argument);

                EmitStore(functionIR, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i});
            }

            Program.Functions[functionName] = functionIR;

            EmitScope(functionIR, entryBlock, overload.Body, overload.ReturnType);
        }

        private void PrintFunction(string name, FunctionIR function)
        {
            Console.WriteLine($"\nIR for function '{name}'");

            var blockIndex = 0;
            var i = 0;
            while (blockIndex < function.BasicBlocks.Count)
            {
                var instructionToStopAt = blockIndex < function.BasicBlocks.Count - 1 ? function.BasicBlocks[blockIndex + 1].Location : function.Instructions.Count;
                Console.WriteLine($"\n--------------- Basic Block {blockIndex} ---------------\n");
                while (i < instructionToStopAt)
                {
                    var instruction = function.Instructions[i++];

                    var text = $"\t{instruction.Type} ";
                    switch (instruction.Type)
                    {
                        case InstructionType.LoadAllocation:
                        case InstructionType.GetAllocationPointer:
                            text += $"{instruction.Index.Value} {(instruction.Global ? "global" : string.Empty)}";
                            break;
                        case InstructionType.StoreToAllocation:
                        case InstructionType.GetStructPointer:
                            text += $"{instruction.Index.Value} {PrintInstructionValue(instruction.Value1)}";
                            break;
                        case InstructionType.Call:
                            text += $"{instruction.CallFunction} {PrintInstructionValue(instruction.Value1)}";
                            break;
                        case InstructionType.AllocateArray:
                            break;
                        case InstructionType.Jump:
                        case InstructionType.Load:
                        case InstructionType.Return:
                        case InstructionType.IsNull:
                        case InstructionType.IsNotNull:
                        case InstructionType.Not:
                        case InstructionType.IntegerNegate:
                        case InstructionType.FloatNegate:
                            text += PrintInstructionValue(instruction.Value1);
                            break;
                        default:
                            text += $"{PrintInstructionValue(instruction.Value1)}, {PrintInstructionValue(instruction.Value2)}";
                            break;
                    }
                    Console.WriteLine(text);
                }
                blockIndex++;
            }
        }

        private string PrintInstructionValue(InstructionValue value)
        {
            if (value == null) return string.Empty;

            switch (value.ValueType)
            {
                case InstructionValueType.Value:
                    return $"v{value.ValueIndex}";
                case InstructionValueType.Block:
                    return value.ValueIndex.ToString();
                case InstructionValueType.Argument:
                    return $"arg{value.ValueIndex}";
                case InstructionValueType.Constant:
                    switch (value.Type.TypeKind)
                    {
                        case TypeKind.Boolean:
                            return value.ConstantValue.Boolean.ToString();
                        case TypeKind.Integer:
                        case TypeKind.Enum:
                            return value.ConstantValue.Integer.ToString();
                        case TypeKind.Float:
                            if (value.Type.Size == 4)
                            {
                                return value.ConstantValue.Float.ToString();
                            }
                            return value.ConstantValue.Double.ToString();
                        case TypeKind.String:
                            return $"\"{value.ConstantString}\"";
                    }
                    break;
                case InstructionValueType.Null:
                    return "null";
                case InstructionValueType.Type:
                    return value.Type.Name;
                case InstructionValueType.CallArguments:
                    return string.Join(", ", value.Values.Select(PrintInstructionValue));
            }

            return string.Empty;
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
                                          Values = new [] {GetConstantInteger(0), new InstructionValue {ValueType = InstructionValueType.Null}}
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
                                      Values = new [] {GetConstantInteger(0), new InstructionValue {ValueType = InstructionValueType.Null}}
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
                case TypeKind.String:
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

        private BasicBlock EmitScope(FunctionIR function, BasicBlock block, ScopeAst scope, IType returnType)
        {
            foreach (var ast in scope.Children)
            {
                switch (ast)
                {
                    case ReturnAst returnAst:
                        EmitReturn(function, returnAst, returnType, scope);
                        break;
                    case DeclarationAst declaration:
                        EmitDeclaration(function, declaration, scope);
                        break;
                    case AssignmentAst assignment:
                        EmitAssignment(function, assignment, scope);
                        break;
                    case ScopeAst childScope:
                        block = EmitScope(function, block, scope, returnType);
                        break;
                    case ConditionalAst conditional:
                        block = EmitConditional(function, block, conditional, scope, returnType);
                        break;
                    case WhileAst whileAst:
                        block = EmitWhile(function, block, whileAst, scope, returnType);
                        break;
                    case EachAst each:
                        block = EmitEach(function, block, each, scope, returnType);
                        break;
                    case BreakAst:
                    case ContinueAst:
                        // TODO Use the break or continue basic block
                        break;
                    default:
                        EmitIR(function, ast, scope);
                        break;
                }
            }
            return block;
        }

        private void EmitDeclaration(FunctionIR function, DeclarationAst declaration, ScopeAst scope)
        {
            if (declaration.Constant)
            {
                function.Constants ??= new();

                function.Constants[declaration.Name] = EmitConstantIR(declaration.Value, scope, function);
            }
            else
            {
                var allocationIndex = AddAllocation(function, declaration);

                if (declaration.Value != null)
                {
                    var value = EmitIR(function, declaration.Value, scope);
                    EmitStore(function, allocationIndex, EmitCastValue(function, value, declaration.Type));
                    return;
                }

                switch (declaration.TypeDefinition.TypeKind)
                {
                    // Initialize arrays
                    case TypeKind.Array:
                        var pointer = EmitGetPointer(function, allocationIndex, declaration.Type);
                        var arrayStruct = (StructAst)declaration.Type;
                        if (declaration.TypeDefinition.ConstCount != null)
                        {
                            var arrayPointer = InitializeConstArray(function, pointer, arrayStruct, declaration.TypeDefinition.ConstCount.Value, declaration.ArrayElementType);

                            if (declaration.ArrayValues != null)
                            {
                                InitializeArrayValues(function, arrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                            }
                        }
                        else if (declaration.TypeDefinition.Count != null)
                        {
                            function.SaveStack = true;
                            var count = EmitIR(function, declaration.TypeDefinition.Count, scope);
                            var countPointer = EmitGetStructPointer(function, pointer, arrayStruct, 0);
                            EmitStore(function, countPointer, count);

                            var elementType = new InstructionValue {ValueType = InstructionValueType.Type, Type = declaration.ArrayElementType};
                            var arrayData = EmitInstruction(InstructionType.AllocateArray, function, null, count, elementType);
                            var dataPointer = EmitGetStructPointer(function, pointer, arrayStruct, 0);
                            EmitStore(function, dataPointer, arrayData);
                        }
                        else
                        {
                            var lengthPointer = EmitGetStructPointer(function, pointer, arrayStruct, 0);
                            var lengthValue = GetConstantInteger(0);
                            EmitStore(function, lengthPointer, lengthValue);
                        }
                        break;
                    case TypeKind.CArray:
                        var cArrayPointer = EmitGetPointer(function, allocationIndex, declaration.Type);
                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(function, cArrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                        }
                        break;
                        // Initialize struct field default values
                    case TypeKind.Struct:
                    case TypeKind.String:
                        var structPointer = EmitGetPointer(function, allocationIndex, declaration.Type);
                        InitializeStruct(function, (StructAst)declaration.Type, structPointer, scope, declaration.Assignments);
                        break;
                        // Initialize pointers to null
                    case TypeKind.Pointer:
                        EmitStore(function, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Null});
                        break;
                        // Or initialize to default
                    default:
                        var zero = GetDefaultConstant(declaration.Type);
                        EmitStore(function, allocationIndex, zero);
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

        private InstructionValue InitializeConstArray(FunctionIR function, InstructionValue arrayPointer, StructAst arrayStruct, uint length, IType elementType)
        {
            var lengthPointer = EmitGetStructPointer(function, arrayPointer, arrayStruct, 0);
            var lengthValue = GetConstantInteger(length);
            EmitStore(function, lengthPointer, lengthValue);

            var dataIndex = AddAllocation(function, elementType, true, length);
            var arrayDataPointer = EmitGetPointer(function, dataIndex, elementType);
            var dataPointer = EmitGetStructPointer(function, arrayPointer, arrayStruct, 1);
            EmitStore(function, dataPointer, arrayDataPointer);

            return arrayDataPointer;
        }

        private void InitializeArrayValues(FunctionIR function, InstructionValue arrayPointer, IType elementType, List<IAst> arrayValues, ScopeAst scope)
        {
            for (var i = 0; i < arrayValues.Count; i++)
            {
                var index = GetConstantInteger(i);
                var pointer = EmitGetPointer(function, arrayPointer, index, elementType, getFirstPointer: true);

                var value = EmitIR(function, arrayValues[i], scope);
                EmitStore(function, pointer, EmitCastValue(function, value, elementType));
            }
        }

        private void InitializeStruct(FunctionIR function, StructAst structDef, InstructionValue pointer, ScopeAst scope, Dictionary<string, AssignmentAst> assignments)
        {
            if (assignments == null)
            {
                for (var i = 0; i < structDef.Fields.Count; i++)
                {
                    var field = structDef.Fields[i];

                    var fieldPointer = EmitGetStructPointer(function, pointer, structDef, i, field);

                    InitializeField(function, field, fieldPointer, scope);
                }
            }
            else
            {
                for (var i = 0; i < structDef.Fields.Count; i++)
                {
                    var field = structDef.Fields[i];

                    var fieldPointer = EmitGetStructPointer(function, pointer, structDef, i, field);

                    if (assignments.TryGetValue(field.Name, out var assignment))
                    {
                        var value = EmitIR(function, assignment.Value, scope);

                        EmitStore(function, fieldPointer, EmitCastValue(function, value, field.Type));
                    }
                    else
                    {
                        InitializeField(function, field, fieldPointer, scope);
                    }
                }
            }
        }

        private void InitializeField(FunctionIR function, StructFieldAst field, InstructionValue pointer, ScopeAst scope)
        {
            switch (field.Type.TypeKind)
            {
                // Initialize arrays
                case TypeKind.Array:
                    var arrayStruct = (StructAst)field.Type;
                    if (field.TypeDefinition.ConstCount != null)
                    {
                        var arrayPointer = InitializeConstArray(function, pointer, arrayStruct, field.TypeDefinition.ConstCount.Value, field.ArrayElementType);

                        if (field.ArrayValues != null)
                        {
                            InitializeArrayValues(function, arrayPointer, field.ArrayElementType, field.ArrayValues, scope);
                        }
                    }
                    else
                    {
                        var lengthPointer = EmitGetStructPointer(function, pointer, arrayStruct, 0);
                        var lengthValue = GetConstantInteger(0);
                        EmitStore(function, lengthPointer, lengthValue);
                    }
                    break;
                case TypeKind.CArray:
                    if (field.ArrayValues != null)
                    {
                        InitializeArrayValues(function, pointer, field.ArrayElementType, field.ArrayValues, scope);
                    }
                    break;
                    // Initialize struct field default values
                case TypeKind.Struct:
                case TypeKind.String: // TODO String default values
                    InitializeStruct(function, (StructAst)field.Type, pointer, scope, field.Assignments);
                    break;
                    // Initialize pointers to null
                case TypeKind.Pointer:
                    EmitStore(function, pointer, new InstructionValue {ValueType = InstructionValueType.Null});
                    break;
                    // Or initialize to default
                default:
                    var defaultValue = field.Value == null ? GetDefaultConstant(field.Type) : EmitIR(function, field.Value, scope);
                    EmitStore(function, pointer, defaultValue);
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
                case TypeKind.Enum:
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

        private void EmitAssignment(FunctionIR function, AssignmentAst assignment, ScopeAst scope)
        {
            var pointer = EmitGetReference(function, assignment.Reference, scope);

            var value = EmitIR(function, assignment.Value, scope);
            if (assignment.Operator != Operator.None)
            {
                var previousValue = EmitLoad(function, pointer.Type, pointer);
                value = EmitExpression(function, previousValue, value, assignment.Operator, pointer.Type);
            }

            EmitStore(function, pointer, value);
        }

        public void EmitReturn(FunctionIR function, ReturnAst returnAst, IType returnType, ScopeAst scope)
        {
            var instruction = new Instruction {Type = InstructionType.Return};
            if (returnAst.Value != null)
            {
                instruction.Value1 = EmitIR(function, returnAst.Value, scope);
            }

            function.Instructions.Add(instruction);
        }

        private BasicBlock EmitConditional(FunctionIR function, BasicBlock block, ConditionalAst conditional, ScopeAst scope, IType returnType)
        {
            // Run the condition expression in the current basic block and then jump to the following
            var condition = EmitConditionExpression(function, conditional.Condition, scope);

            var thenBlock = AddBasicBlock(function);
            thenBlock = EmitScope(function, thenBlock, conditional.IfBlock, returnType);
            var elseBlock = AddBasicBlock(function);

            // Jump to the else block, otherwise fall through to the then block
            EmitInstruction(InstructionType.ConditionalJump, function, null, condition, new InstructionValue {ValueType = InstructionValueType.Block, ValueIndex = elseBlock.Index});

            if (conditional.ElseBlock == null)
            {
                return elseBlock;
            }

            elseBlock = EmitScope(function, elseBlock, conditional.ElseBlock, returnType);

            if (conditional.IfReturns && conditional.ElseReturns)
            {
                return elseBlock;
            }

            var afterBlock = elseBlock.Location < function.Instructions.Count ? AddBasicBlock(function) : elseBlock;

            // For when the the if block does not return, a jump to the after block is required
            if (!conditional.IfReturns)
            {
                EmitInstruction(InstructionType.Jump, function, null, new InstructionValue {ValueType = InstructionValueType.Block, ValueIndex = afterBlock.Index});
            }

            return afterBlock;
        }

        private BasicBlock EmitWhile(FunctionIR function, BasicBlock block, WhileAst whileAst, ScopeAst scope, IType returnType)
        {
            // Create a block for the condition expression and then jump to the following
            var conditionBlock = block.Location < function.Instructions.Count ? AddBasicBlock(function) : block;
            var condition = EmitConditionExpression(function, whileAst.Condition, scope);

            var whileBodyBlock = AddBasicBlock(function);
            whileBodyBlock = EmitScope(function, whileBodyBlock, whileAst.Body, returnType);
            EmitInstruction(InstructionType.Jump, function, null, new InstructionValue {ValueType = InstructionValueType.Block, ValueIndex = conditionBlock.Index});

            var afterBlock = AddBasicBlock(function);
            EmitInstruction(InstructionType.ConditionalJump, function, null, condition, new InstructionValue {ValueType = InstructionValueType.Block, ValueIndex = afterBlock.Index});

            return afterBlock;
        }

        private InstructionValue EmitConditionExpression(FunctionIR function, IAst ast, ScopeAst scope)
        {
            var value = EmitIR(function, ast, scope);

            switch (value.Type.TypeKind)
            {
                case TypeKind.Integer:
                    return EmitInstruction(InstructionType.IntegerEquals, function, _boolType, value, GetDefaultConstant(value.Type));
                case TypeKind.Float:
                    return EmitInstruction(InstructionType.FloatEquals, function, _boolType, value, GetDefaultConstant(value.Type));
                case TypeKind.Pointer:
                    return EmitInstruction(InstructionType.IsNull, function, _boolType, value);
                // Will be type bool
                default:
                    return EmitInstruction(InstructionType.Not, function, _boolType, value);
            }
        }

        private BasicBlock EmitEach(FunctionIR function, BasicBlock block, EachAst each, ScopeAst scope, IType returnType)
        {
            var indexVariable = AddAllocation(function, _s32Type);
            InstructionValue compareTarget;
            InstructionValue arrayData = null;
            var cArrayIteration = false;

            if (each.Iteration != null)
            {
                if (each.IndexVariable != null)
                {
                    each.IndexVariableVariable.AllocationIndex = indexVariable;
                }
                EmitStore(function, indexVariable, GetConstantInteger(0));

                var iteration = EmitIR(function, each.Iteration, scope);

                // Load the array data and set the compareTarget to the array count
                if (iteration.Type.TypeKind == TypeKind.CArray)
                {
                    cArrayIteration = true;
                    arrayData = iteration;
                    compareTarget = GetConstantInteger(each.CArrayLength);
                }
                else
                {
                    var arrayDef = (StructAst)iteration.Type;
                    var iterationVariable = AddAllocation(function, arrayDef);
                    EmitStore(function, iterationVariable, iteration);
                    var iterationVariablePointer = EmitGetPointer(function, iterationVariable, arrayDef);

                    var lengthPointer = EmitGetStructPointer(function, iterationVariablePointer, arrayDef, 0);
                    compareTarget = EmitLoad(function, _s32Type, lengthPointer);

                    var dataField = arrayDef.Fields[1];
                    var dataPointer = EmitGetStructPointer(function, iterationVariablePointer, arrayDef, 1, dataField);
                    arrayData = EmitLoad(function, dataField.Type, dataPointer);
                }
            }
            else
            {
                // Begin the loop at the beginning of the range
                var value = EmitIR(function, each.RangeBegin, scope);

                EmitStore(function, indexVariable, value);
                each.IterationVariableVariable.AllocationIndex = indexVariable;

                // Get the end of the range
                compareTarget = EmitIR(function, each.RangeEnd, scope);
            }

            var conditionBlock = AddBasicBlock(function);
            var indexValue = EmitLoad(function, _s32Type, indexVariable);
            var condition = EmitInstruction(InstructionType.IntegerGreaterThanOrEqual, function, _boolType, indexValue, compareTarget);
            if (each.Iteration != null)
            {
                var iterationVariable = EmitGetPointer(function, arrayData, indexValue, each.IterationVariableVariable.Type, cArrayIteration);
                each.IterationVariableVariable.Pointer = iterationVariable;
            }

            var eachBodyBlock = AddBasicBlock(function);
            eachBodyBlock = EmitScope(function, eachBodyBlock, each.Body, returnType);

            var eachIncrementBlock = eachBodyBlock.Location < function.Instructions.Count ? AddBasicBlock(function) : eachBodyBlock;
            var nextValue = EmitInstruction(InstructionType.IntegerAdd, function, _s32Type, indexValue, GetConstantInteger(1));
            EmitStore(function, indexVariable, nextValue);
            EmitInstruction(InstructionType.Jump, function, null, new InstructionValue {ValueType = InstructionValueType.Block, ValueIndex = conditionBlock.Index});

            var afterBlock = AddBasicBlock(function);
            EmitInstruction(InstructionType.ConditionalJump, function, null, condition, new InstructionValue {ValueType = InstructionValueType.Block, ValueIndex = afterBlock.Index});

            return afterBlock;
        }

        private BasicBlock AddBasicBlock(FunctionIR function)
        {
            var block = new BasicBlock {Index = function.BasicBlocks.Count, Location = function.Instructions.Count};
            function.BasicBlocks.Add(block);
            return block;
        }

        private InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, bool useRawString = false)
        {
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

                            var stringPointer = EmitGetPointer(function, declaration.AllocationIndex, _stringStruct, global);
                            var dataPointer = EmitGetStructPointer(function, stringPointer, _stringStruct, 1, dataField);
                            return EmitLoad(function, dataField.Type, dataPointer);
                        }
                        return EmitLoad(function, declaration.Type, declaration.AllocationIndex, global);
                    }
                    else if (identifier is VariableAst variable)
                    {
                        if (useRawString && variable.Type.TypeKind == TypeKind.String)
                        {
                            _stringStruct ??= (StructAst) variable.Type;
                            var dataField = _stringStruct.Fields[1];

                            var stringPointer = variable.AllocationIndex.HasValue ? EmitGetPointer(function, variable.AllocationIndex.Value, _stringStruct, global) : variable.Pointer;

                            var dataPointer = EmitGetStructPointer(function, stringPointer, _stringStruct, 1, dataField);
                            return EmitLoad(function, dataField.Type, dataPointer);
                        }
                        else if (variable.AllocationIndex.HasValue)
                        {
                            return EmitLoad(function, variable.Type, variable.AllocationIndex.Value, global);
                        }
                        return EmitLoad(function, variable.Type, variable.Pointer);
                    }
                    else if (identifier is IType type)
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
                            ValueType = InstructionValueType.Constant, Type = enumDef,
                                      ConstantValue = new Constant {Integer = enumValue}
                        };
                    }
                    else if (structField.IsConstant)
                    {
                        return GetConstantInteger(structField.ConstantValue);
                    }
                    var structFieldPointer = EmitGetStructRefPointer(function, structField, scope, out var loaded);
                    if (!loaded)
                    {
                        if (useRawString && structFieldPointer.Type.TypeKind == TypeKind.String)
                        {
                            _stringStruct ??= (StructAst) TypeTable.Types["string"];
                            var dataField = _stringStruct.Fields[1];

                            var dataPointer = EmitGetStructPointer(function, structFieldPointer, _stringStruct, 1, dataField);
                            return EmitLoad(function, dataField.Type, dataPointer);
                        }
                        return EmitLoad(function, structFieldPointer.Type, structFieldPointer);
                    }
                    return structFieldPointer;
                case CallAst call:
                    return EmitCall(function, call, scope);
                case ChangeByOneAst changeByOne:
                    var pointer = EmitGetReference(function, changeByOne.Value, scope);
                    var previousValue = EmitLoad(function, pointer.Type, pointer);

                    var constOne = new InstructionValue {ValueType = InstructionValueType.Constant, Type = changeByOne.Type};
                    InstructionType instructionType;
                    if (changeByOne.Type.TypeKind == TypeKind.Integer)
                    {
                        instructionType = changeByOne.Positive ? InstructionType.IntegerAdd : InstructionType.IntegerSubtract;
                        constOne.ConstantValue = new Constant {Integer = 1};
                    }
                    else
                    {
                        instructionType = changeByOne.Positive ? InstructionType.FloatAdd : InstructionType.FloatSubtract;
                        if (changeByOne.Type.Size == 4)
                        {
                            constOne.ConstantValue = new Constant {Float = 1};
                        }
                        else
                        {
                            constOne.ConstantValue = new Constant {Double = 1};
                        }
                    }
                    var newValue = EmitInstruction(instructionType, function, changeByOne.Type, previousValue, constOne);
                    EmitStore(function, pointer, newValue);

                    return changeByOne.Prefix ? newValue : previousValue;
                case UnaryAst unary:
                    InstructionValue value;
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not:
                            value = EmitIR(function, unary.Value, scope);
                            return EmitInstruction(InstructionType.Not, function, value.Type, value);
                        case UnaryOperator.Negate:
                            value = EmitIR(function, unary.Value, scope);
                            var negate = value.Type.TypeKind == TypeKind.Integer ? InstructionType.IntegerNegate : InstructionType.FloatNegate;
                            return EmitInstruction(negate, function, value.Type, value);
                        case UnaryOperator.Dereference:
                            value = EmitIR(function, unary.Value, scope);
                            return EmitLoad(function, value.Type, value);
                        case UnaryOperator.Reference:
                            return EmitGetReference(function, unary.Value, scope);
                    }
                    break;
                case IndexAst index:
                    var indexPointer = EmitGetIndexPointer(function, index, scope);

                    return index.CallsOverload ? indexPointer : EmitLoad(function, indexPointer.Type, indexPointer);
                case ExpressionAst expression:
                    var expressionValue = EmitIR(function, expression.Children[0], scope);
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var rhs = EmitIR(function, expression.Children[i], scope);
                        if (expression.OperatorOverloads.TryGetValue(i, out var overload))
                        {
                            expressionValue = EmitCall(function, GetOperatorOverloadName(overload.Type, overload.Operator), new []{expressionValue, rhs}, overload.ReturnType);
                        }
                        else
                        {
                            expressionValue = EmitExpression(function, expressionValue, rhs, expression.Operators[i - 1], expression.ResultingTypes[i - 1]);
                        }
                    }
                    return expressionValue;
                case TypeDefinition typeDef:
                    return GetConstantInteger(typeDef.TypeIndex.Value);
                case CastAst cast:
                    var castValue = EmitIR(function, cast.Value, scope);
                    return EmitCastValue(function, castValue, cast.TargetType);
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
                            ValueType = InstructionValueType.Constant, Type = enumDef,
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
            switch (constant.Type.TypeKind)
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

        private InstructionValue EmitGetReference(FunctionIR function, IAst ast, ScopeAst scope)
        {
            switch (ast)
            {
                case IdentifierAst identifier:
                    var ident = GetScopeIdentifier(scope, identifier.Name, out var global);
                    switch (ident)
                    {
                        case DeclarationAst declaration:
                            return EmitGetPointer(function, declaration.AllocationIndex, declaration.Type, global);
                        case VariableAst variable:
                            if (variable.AllocationIndex.HasValue)
                            {
                                return EmitGetPointer(function, variable.AllocationIndex.Value, variable.Type, global);
                            }
                            else
                            {
                                return variable.Pointer;
                            }
                    }
                    break;
                case StructFieldRefAst structField:
                    return EmitGetStructRefPointer(function, structField, scope, out _);
                case IndexAst index:
                    return EmitGetIndexPointer(function, index, scope);
                case UnaryAst unary:
                    return EmitIR(function, unary.Value, scope);
            }
            return null;
        }

        private InstructionValue EmitGetStructRefPointer(FunctionIR function, StructFieldRefAst structField, ScopeAst scope, out bool loaded)
        {
            loaded = false;
            InstructionValue value = null;

            switch (structField.Children[0])
            {
                case IdentifierAst identifier:
                    var ident = GetScopeIdentifier(scope, identifier.Name, out var global);
                    switch (ident)
                    {
                        case DeclarationAst declaration:
                            value = EmitGetPointer(function, declaration.AllocationIndex, declaration.Type, global);
                            break;
                        case VariableAst variable:
                            value = variable.AllocationIndex.HasValue ? EmitGetPointer(function, variable.AllocationIndex.Value, variable.Type, global) : variable.Pointer;
                            break;
                    }
                    break;
                case IndexAst index:
                    value = EmitGetIndexPointer(function, index, scope);
                    if (index.CallsOverload && !structField.Pointers[0])
                    {
                        var type = structField.Types[0];
                        var allocationIndex = AddAllocation(function, type);
                        EmitStore(function, allocationIndex, value);
                        value = EmitGetPointer(function, allocationIndex, type);
                    }
                    break;
                case CallAst call:
                    value = EmitCall(function, call, scope);
                    if (!structField.Pointers[0])
                    {
                        var type = structField.Types[0];
                        var allocationIndex = AddAllocation(function, type);
                        EmitStore(function, allocationIndex, value);
                        value = EmitGetPointer(function, allocationIndex, type);
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
                        value = EmitLoad(function, type, value);
                    }
                }
                skipPointer = false;

                if (type.TypeKind == TypeKind.CArray)
                {
                    if (structField.Children[i] is IndexAst index)
                    {
                        var indexValue = EmitIR(function, index.Index, scope);
                        var arrayType = (ArrayType) type;
                        var elementType = arrayType.ElementType;
                        value = EmitGetPointer(function, value, indexValue, elementType, true);
                    }
                }
                else
                {
                    var structDefinition = (StructAst) type;
                    var fieldIndex = structField.ValueIndices[i-1];
                    var field = structDefinition.Fields[fieldIndex];

                    value = EmitGetStructPointer(function, value, structDefinition, fieldIndex, field);
                    if (structField.Children[i] is IndexAst index)
                    {
                        value = EmitGetIndexPointer(function, index, scope, field.Type, value);

                        if (index.CallsOverload)
                        {
                            skipPointer = true;
                            if (i < structField.Pointers.Length && !structField.Pointers[i])
                            {
                                var nextType = structField.Types[i];
                                var allocationIndex = AddAllocation(function, nextType);
                                EmitStore(function, allocationIndex, value);
                                value = EmitGetPointer(function, allocationIndex, nextType);
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

        private InstructionValue EmitCall(FunctionIR function, CallAst call, ScopeAst scope)
        {
            var argumentCount = call.Function.Varargs ? call.Arguments.Count : call.Function.Arguments.Count;
            var arguments = new InstructionValue[argumentCount];

            if (call.Function.Params)
            {
                for (var i = 0; i < argumentCount - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope);
                    arguments[i] = EmitCastValue(function, argument, call.Function.Arguments[i].Type);
                }

                // Rollup the rest of the arguments into an array
                var paramsType = call.Function.Arguments[^1].Type;
                var elementType = call.Function.ParamsElementType;
                var paramsAllocationIndex = AddAllocation(function, paramsType);
                var paramsPointer = EmitGetPointer(function, paramsAllocationIndex, paramsType);
                var dataPointer = InitializeConstArray(function, paramsPointer, (StructAst)paramsType, (uint)(call.Arguments.Count - call.Function.Arguments.Count + 1), elementType);

                uint paramsIndex = 0;
                for (var i = call.Function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                {
                    var index = GetConstantInteger(paramsIndex);
                    var pointer = EmitGetPointer(function, dataPointer, index, elementType, true);

                    var value = EmitIR(function, call.Arguments[i], scope);
                    EmitStore(function, pointer, EmitCastValue(function, value, elementType));
                }

                var paramsValue = EmitLoad(function, paramsType, paramsAllocationIndex);
                arguments[argumentCount - 1] = paramsValue;
            }
            else if (call.Function.Varargs)
            {
                var i = 0;
                for (; i < call.Function.Arguments.Count - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, true);
                    arguments[i] = EmitCastValue(function, argument, call.Function.Arguments[i].Type);
                }

                // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                for (; i < argumentCount; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, true);
                    if (argument.Type?.TypeKind == TypeKind.Float && argument.Type.Size == 4)
                    {
                        argument = EmitCastValue(function, argument, _float64Type);
                    }
                    arguments[i] = argument;
                }
            }
            else
            {
                for (var i = 0; i < argumentCount; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, call.Function.Extern);
                    arguments[i] = EmitCastValue(function, argument, call.Function.Arguments[i].Type);
                }
            }

            return EmitCall(function, GetFunctionName(call.Function), arguments, call.Function.ReturnType);
        }

        private InstructionValue EmitGetIndexPointer(FunctionIR function, IndexAst index, ScopeAst scope, IType type = null, InstructionValue variable = null)
        {
            if (type == null)
            {
                var declaration = (DeclarationAst) GetScopeIdentifier(scope, index.Name, out var global);
                type = declaration.Type;
                variable = EmitGetPointer(function, declaration.AllocationIndex, type, global);
            }

            var indexValue = EmitIR(function, index.Index, scope);

            if (index.CallsOverload)
            {
                var overloadName = GetOperatorOverloadName(index.Overload.Type, Operator.Subscript);

                var value = EmitLoad(function, type, variable);
                return EmitCall(function, overloadName, new []{value, indexValue}, index.Overload.ReturnType);
            }

            IType elementType;
            if (type.TypeKind == TypeKind.Pointer)
            {
                var pointerType = (PrimitiveAst)type;
                elementType = pointerType.PointerType;

                var dataPointer = EmitLoad(function, type, variable);
                return EmitGetPointer(function, dataPointer, indexValue, elementType);
            }
            else if (type.TypeKind == TypeKind.CArray)
            {
                var arrayType = (ArrayType)type;
                elementType = arrayType.ElementType;

                return EmitGetPointer(function, variable, indexValue, elementType, true);
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

                var data = EmitGetStructPointer(function, variable, structAst, 1, dataField);
                var dataPointer = EmitLoad(function, data.Type, data);
                return EmitGetPointer(function, dataPointer, indexValue, elementType);
            }
        }

        private InstructionValue EmitExpression(FunctionIR function, InstructionValue lhs, InstructionValue rhs, Operator op, IType type)
        {
            // 1. Handle pointer math
            if (lhs.Type?.TypeKind == TypeKind.Pointer)
            {
                return EmitPointerOperation(function, lhs, rhs, op, type);
            }
            if (rhs.Type?.TypeKind == TypeKind.Pointer)
            {
                return EmitPointerOperation(function, rhs, lhs, op, type);
            }

            // 2. Handle compares and shifts, since the lhs and rhs should not be cast to the target type
            switch (op)
            {
                case Operator.And:
                    return EmitInstruction(InstructionType.And, function, type, lhs, rhs);
                case Operator.Or:
                    return EmitInstruction(InstructionType.Or, function, type, lhs, rhs);
                case Operator.ShiftLeft:
                    return EmitInstruction(InstructionType.ShiftLeft, function, type, lhs, rhs);
                case Operator.ShiftRight:
                    return EmitInstruction(InstructionType.ShiftRight, function, type, lhs, rhs);
                case Operator.RotateLeft:
                    return EmitInstruction(InstructionType.RotateLeft, function, type, lhs, rhs);
                case Operator.RotateRight:
                    return EmitInstruction(InstructionType.RotateRight, function, type, lhs, rhs);
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return EmitCompare(function, lhs, rhs, op, type);
            }

            // 3. Cast lhs and rhs to the target types
            lhs = EmitCastValue(function, lhs, type);
            rhs = EmitCastValue(function, rhs, type);

            // 4. Handle the rest of the simple operators
            switch (op)
            {
                case Operator.BitwiseAnd:
                    return EmitInstruction(InstructionType.BitwiseAnd, function, type, lhs, rhs);
                case Operator.BitwiseOr:
                    return EmitInstruction(InstructionType.BitwiseOr, function, type, lhs, rhs);
                case Operator.Xor:
                    return EmitInstruction(InstructionType.Xor, function, type, lhs, rhs);
            }

            InstructionType instructionType;
            if (type.TypeKind == TypeKind.Integer)
            {
                var integerType = (PrimitiveAst)type;
                instructionType = op switch
                {
                    Operator.Add => InstructionType.IntegerAdd,
                        Operator.Subtract => InstructionType.IntegerSubtract,
                        Operator.Multiply => InstructionType.IntegerMultiply,
                        Operator.Divide => integerType.Primitive.Signed ? InstructionType.IntegerDivide : InstructionType.UnsignedIntegerDivide,
                        Operator.Modulus => integerType.Primitive.Signed ? InstructionType.IntegerDivide : InstructionType.UnsignedIntegerModulus,
                        // @Cleanup this branch should never be hit
                        _ => InstructionType.IntegerAdd
                };
            }
            else
            {
                instructionType = op switch
                {
                    Operator.Add => InstructionType.FloatAdd,
                        Operator.Subtract => InstructionType.FloatAdd,
                        Operator.Multiply => InstructionType.FloatAdd,
                        Operator.Divide => InstructionType.FloatAdd,
                        Operator.Modulus => InstructionType.FloatModulus,
                        // @Cleanup this branch should never be hit
                        _ => InstructionType.FloatAdd
                };
            }
            return EmitInstruction(instructionType, function, type, lhs, rhs);
        }

        private InstructionValue EmitPointerOperation(FunctionIR function, InstructionValue lhs, InstructionValue rhs, Operator op, IType type)
        {
            if (op == Operator.Equality)
            {
                if (rhs.ValueType == InstructionValueType.Null)
                {
                    return EmitInstruction(InstructionType.IsNull, function, type, lhs);
                }
                return EmitInstruction(InstructionType.PointerEquals, function, type, lhs, rhs);
            }
            if (op == Operator.NotEqual)
            {
                if (rhs.ValueType == InstructionValueType.Null)
                {
                    return EmitInstruction(InstructionType.IsNotNull, function, type, lhs);
                }
                return EmitInstruction(InstructionType.PointerNotEquals, function, type, lhs, rhs);
            }
            if (op == Operator.Subtract)
            {
                return EmitInstruction(InstructionType.PointerSubtract, function, type, lhs, rhs);
            }
            return EmitInstruction(InstructionType.PointerAdd, function, type, lhs, rhs);
        }

        private InstructionValue EmitCompare(FunctionIR function, InstructionValue lhs, InstructionValue rhs, Operator op, IType type)
        {
            switch (lhs.Type.TypeKind)
            {
                case TypeKind.Integer:
                    switch (rhs.Type.TypeKind)
                    {
                        case TypeKind.Integer:
                            if (lhs.Type.Size > rhs.Type.Size)
                            {
                                rhs = EmitCastValue(function, rhs, lhs.Type);
                            }
                            else if (lhs.Type.Size < rhs.Type.Size)
                            {
                                lhs = EmitCastValue(function, lhs, rhs.Type);
                            }
                            var integerType = (PrimitiveAst)lhs.Type;
                            return EmitInstruction(GetIntCompareInstructionType(op, integerType.Primitive.Signed), function, type, lhs, rhs);
                        case TypeKind.Float:
                            lhs = EmitCastValue(function, lhs, rhs.Type);
                            return EmitInstruction(GetFloatCompareInstructionType(op), function, type, lhs, rhs);
                    }
                    break;
                case TypeKind.Float:
                    switch (rhs.Type.TypeKind)
                    {
                        case TypeKind.Integer:
                            rhs = EmitCastValue(function, rhs, lhs.Type);
                            return EmitInstruction(GetFloatCompareInstructionType(op), function, type, lhs, rhs);
                        case TypeKind.Float:
                            if (lhs.Type.Size > rhs.Type.Size)
                            {
                                rhs = EmitCastValue(function, rhs, _float64Type);
                            }
                            else if (lhs.Type.Size < rhs.Type.Size)
                            {
                                lhs = EmitCastValue(function, lhs, _float64Type);
                            }
                            return EmitInstruction(GetFloatCompareInstructionType(op), function, type, lhs, rhs);
                    }
                    break;
                case TypeKind.Enum:
                    var enumAst = (EnumAst)lhs.Type;
                    var baseType = (PrimitiveAst)enumAst.BaseType;
                    return EmitInstruction(GetIntCompareInstructionType(op, baseType.Primitive.Signed), function, type, lhs, rhs);
            }

            Console.WriteLine("Unexpected type in compare");
            Environment.Exit(ErrorCodes.BuildError);
            return null;
        }

        private static InstructionType GetIntCompareInstructionType(Operator op, bool signed)
        {
            return op switch
            {
                Operator.Equality => InstructionType.IntegerEquals,
                    Operator.NotEqual => InstructionType.IntegerNotEquals,
                    Operator.GreaterThan => signed ? InstructionType.IntegerGreaterThan : InstructionType.UnsignedIntegerGreaterThan,
                    Operator.GreaterThanEqual => signed ? InstructionType.IntegerGreaterThanOrEqual : InstructionType.UnsignedIntegerGreaterThanOrEqual,
                    Operator.LessThan => signed ? InstructionType.IntegerLessThan : InstructionType.UnsignedIntegerLessThan,
                    Operator.LessThanEqual => signed ? InstructionType.IntegerLessThanOrEqual : InstructionType.UnsignedIntegerLessThanOrEqual,
                    // @Cleanup This branch should never be hit
                    _ => InstructionType.IntegerEquals
            };
        }

        private static InstructionType GetFloatCompareInstructionType(Operator op)
        {
            return op switch
            {
                Operator.Equality => InstructionType.FloatEquals,
                    Operator.NotEqual => InstructionType.FloatNotEquals,
                    Operator.GreaterThan => InstructionType.FloatGreaterThan,
                    Operator.GreaterThanEqual => InstructionType.FloatGreaterThanOrEqual,
                    Operator.LessThan => InstructionType.FloatLessThan,
                    Operator.LessThanEqual => InstructionType.FloatLessThanOrEqual,
                    // @Cleanup This branch should never be hit
                    _ => InstructionType.FloatEquals
            };
        }

        private InstructionValue EmitCastValue(FunctionIR function, InstructionValue value, IType type)
        {
            if (value.Type == type)
            {
                return value;
            }

            // TODO Make this more robust
            var targetType = new InstructionValue {ValueType = InstructionValueType.Type, Type = type};
            var castInstruction = new Instruction {Type = InstructionType.Cast, Value1 = value, Value2 = targetType};
            return AddInstruction(function, castInstruction, type);
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

        private InstructionValue EmitLoad(FunctionIR function, IType type, InstructionValue value)
        {
            var loadInstruction = new Instruction {Type = InstructionType.Load, Value1 = value};
            return AddInstruction(function, loadInstruction, type);
        }

        private InstructionValue EmitLoad(FunctionIR function, IType type, int allocationIndex, bool global = false)
        {
            var loadInstruction = new Instruction {Type = InstructionType.LoadAllocation, Index = allocationIndex, Global = global};
            return AddInstruction(function, loadInstruction, type);
        }

        // TODO For index value, calculate the size of the element
        private InstructionValue EmitGetPointer(FunctionIR function, InstructionValue pointer, InstructionValue index, IType type, bool getFirstPointer = false)
        {
            var instruction = new Instruction
            {
                Type = InstructionType.GetPointer, Value1 = pointer,
                     Value2 = index, GetFirstPointer = getFirstPointer
            };
            return AddInstruction(function, instruction, type);
        }

        public InstructionValue EmitGetPointer(FunctionIR function, int allocationIndex, IType type, bool global = false)
        {
            var instruction = new Instruction {Type = InstructionType.GetAllocationPointer, Index = allocationIndex, Global = global};
            return AddInstruction(function, instruction, type);
        }

        // TODO Add the offset size
        private InstructionValue EmitGetStructPointer(FunctionIR function, InstructionValue value, StructAst structDef, int fieldIndex, StructFieldAst field = null)
        {
            if (field == null)
            {
                 field = structDef.Fields[fieldIndex];
            }

            var instruction = new Instruction {Type = InstructionType.GetStructPointer, Index = fieldIndex, Value1 = value};
            return AddInstruction(function, instruction, field.Type);
        }

        private InstructionValue EmitCall(FunctionIR function, string name, InstructionValue[] arguments, IType returnType)
        {
            var callInstruction = new Instruction
            {
                Type = InstructionType.Call, CallFunction = name,
                Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Values = arguments}
            };
            return AddInstruction(function, callInstruction, returnType);
        }

        private void EmitStore(FunctionIR function, int allocationIndex, InstructionValue value)
        {
            var store = new Instruction {Type = InstructionType.StoreToAllocation, Index = allocationIndex, Value1 = value};
            function.Instructions.Add(store);
        }

        private void EmitStore(FunctionIR function, InstructionValue pointer, InstructionValue value)
        {
            var store = new Instruction {Type = InstructionType.Store, Value1 = pointer, Value2 = value};
            function.Instructions.Add(store);
        }

        private InstructionValue EmitInstruction(InstructionType instructionType, FunctionIR function, IType type, InstructionValue value1, InstructionValue value2 = null)
        {
            var instruction = new Instruction {Type = instructionType, Value1 = value1, Value2 = value2};
            return AddInstruction(function, instruction, type);
        }

        private InstructionValue AddInstruction(FunctionIR function, Instruction instruction, IType type)
        {
            var value = new InstructionValue {ValueIndex = function.Instructions.Count, Type = type};
            function.Instructions.Add(instruction);
            return value;
        }
    }
}
