using System;
using System.Collections.Generic;
using System.Linq;

namespace Lang
{
    public interface IProgramIRBuilder
    {
        void Init();
        void AddFunction(FunctionAst function);
        void AddOperatorOverload(OperatorOverloadAst overload);
        FunctionIR CreateRunnableFunction(IAst ast, ScopeAst globalScope);
        FunctionIR CreateRunnableCondition(IAst ast, ScopeAst globalScope);
        void EmitGlobalVariable(DeclarationAst declaration, ScopeAst scope);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        private IType _boolType;
        private IType _u8Type;
        private IType _s32Type;
        private IType _float64Type;
        private StructAst _stringStruct;

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

            var functionIR = new FunctionIR {Index = Program.FunctionCount++, Source = function};

            if (functionName == "main")
            {
                Program.EntryPoint = functionIR;
            }
            else
            {
                Program.Functions[functionName] = functionIR;
            }

            if (!function.Flags.HasFlag(FunctionFlags.Extern) && !function.Flags.HasFlag(FunctionFlags.Compiler))
            {
                functionIR.Allocations = new();
                functionIR.Instructions = new();
                functionIR.BasicBlocks = new();

                var entryBlock = AddBasicBlock(functionIR);

                if (BuildSettings.Release)
                {
                    for (var i = 0; i < function.Arguments.Count; i++)
                    {
                        var argument = function.Arguments[i];
                        var allocationIndex = AddAllocation(functionIR, argument);

                        EmitStore(functionIR, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type});
                    }
                }
                else
                {
                    for (var i = 0; i < function.Arguments.Count; i++)
                    {
                        var argument = function.Arguments[i];
                        var allocationIndex = AddAllocation(functionIR, argument);

                        EmitStore(functionIR, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type});
                        functionIR.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareParameter, Index = i});
                    }
                }

                EmitScopeChildren(functionIR, entryBlock, function.Body, function.ReturnType, null, null);

                if (function.Flags.HasFlag(FunctionFlags.ReturnVoidAtEnd))
                {
                    functionIR.Instructions.Add(new Instruction {Type = InstructionType.ReturnVoid});
                }

                if (function.Flags.HasFlag(FunctionFlags.PrintIR))
                {
                    PrintFunction(function.Name, functionIR);
                }
            }
        }

        public void AddOperatorOverload(OperatorOverloadAst overload)
        {
            overload.Name = $"operator.{overload.Operator}.{overload.Type.GenericName}";

            var functionIR = new FunctionIR
            {
                Index = Program.FunctionCount++, Source = overload, Allocations = new(), Instructions = new(), BasicBlocks = new()
            };
            var entryBlock = AddBasicBlock(functionIR);

            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                var allocationIndex = AddAllocation(functionIR, argument);

                EmitStore(functionIR, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type});
            }

            Program.Functions[overload.Name] = functionIR;

            EmitScopeChildren(functionIR, entryBlock, overload.Body, overload.ReturnType, null, null);
        }

        public FunctionIR CreateRunnableFunction(IAst ast, ScopeAst globalScope)
        {
            var function = new FunctionIR {Allocations = new(), Instructions = new(), BasicBlocks = new()};
            var entryBlock = AddBasicBlock(function);

            var returns = false;
            switch (ast)
            {
                case ReturnAst returnAst:
                    EmitReturn(function, returnAst, null, globalScope);
                    returns = true;
                    break;
                case DeclarationAst declaration:
                    EmitDeclaration(function, declaration, globalScope);
                    break;
                case AssignmentAst assignment:
                    EmitAssignment(function, assignment, globalScope);
                    break;
                case ScopeAst childScope:
                    EmitScopeChildren(function, entryBlock, childScope, null, null, null);
                    returns = childScope.Returns;
                    break;
                case ConditionalAst conditional:
                    EmitConditional(function, entryBlock, conditional, globalScope, null, null, null, out returns);
                    break;
                case WhileAst whileAst:
                    EmitWhile(function, entryBlock, whileAst, globalScope, null);
                    break;
                case EachAst each:
                    EmitEach(function, entryBlock, each, globalScope, null);
                    break;
                default:
                    EmitIR(function, ast, globalScope);
                    break;
            }

            if (!returns)
            {
                function.Instructions.Add(new Instruction {Type = InstructionType.ReturnVoid});
            }

            return function;
        }

        public FunctionIR CreateRunnableCondition(IAst ast, ScopeAst globalScope)
        {
            var function = new FunctionIR {Allocations = new(), Instructions = new(), BasicBlocks = new()};
            var entryBlock = AddBasicBlock(function);

            var value = EmitIR(function, ast, globalScope);

            // This logic is the opposite of EmitConditionExpression because for runnable conditions the returned value
            // should be true if the result of the expression is truthy.
            // For condition expressions in regular functions, the result is notted to jump to the else block
            var returnValue = value.Type.TypeKind switch
            {
                TypeKind.Integer => EmitInstruction(InstructionType.IntegerNotEquals, function, _boolType, value, GetDefaultConstant(value.Type)),
                TypeKind.Float => EmitInstruction(InstructionType.FloatNotEquals, function, _boolType, value, GetDefaultConstant(value.Type)),
                TypeKind.Pointer => EmitInstruction(InstructionType.IsNotNull, function, _boolType, value),
                // Will be type bool
                _ => value
            };

            function.Instructions.Add(new Instruction {Type = InstructionType.Return, Value1 = returnValue});

            return function;
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
                        case InstructionType.Jump:
                            text += instruction.Value1.JumpBlock.Index.ToString();
                            break;
                        case InstructionType.ConditionalJump:
                            text += $"{instruction.Value2.JumpBlock.Index} {PrintInstructionValue(instruction.Value1)}";
                            break;
                        case InstructionType.Return:
                        case InstructionType.ReturnVoid:
                            text += $"{PrintInstructionValue(instruction.Value1)}";
                            break;
                        case InstructionType.Store:
                            text += $"{PrintInstructionValue(instruction.Value1)}, {PrintInstructionValue(instruction.Value2)}";
                            break;
                        case InstructionType.GetStructPointer:
                            text += $"{PrintInstructionValue(instruction.Value1)} {instruction.Index} => v{instruction.ValueIndex}";
                            break;
                        case InstructionType.Call:
                            text += $"{instruction.String} {PrintInstructionValue(instruction.Value1)} => v{instruction.ValueIndex}";
                            break;
                        case InstructionType.Load:
                        case InstructionType.LoadPointer:
                            text += $"{PrintInstructionValue(instruction.Value1)} {instruction.Value1.Type.Name} => v{instruction.ValueIndex}";
                            break;
                        case InstructionType.IsNull:
                        case InstructionType.IsNotNull:
                        case InstructionType.Not:
                        case InstructionType.IntegerNegate:
                        case InstructionType.FloatNegate:
                            text += $"{PrintInstructionValue(instruction.Value1)} => v{instruction.ValueIndex}";
                            break;
                        case InstructionType.DebugSetLocation:
                        case InstructionType.DebugPushLexicalBlock:
                        case InstructionType.DebugPopLexicalBlock:
                        case InstructionType.DebugDeclareParameter:
                        case InstructionType.DebugDeclareVariable:
                            continue;
                        default:
                            text += $"{PrintInstructionValue(instruction.Value1)}, {PrintInstructionValue(instruction.Value2)} => v{instruction.ValueIndex}";
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
                case InstructionValueType.Allocation:
                    return value.Global ? $"global{value.ValueIndex}" : $"a{value.ValueIndex}";
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
                var globalVariable = new GlobalVariable {Name = declaration.Name, FileIndex = declaration.FileIndex, Line = declaration.Line};

                if (declaration.Type is ArrayType arrayType)
                {
                    globalVariable.Array = true;
                    globalVariable.ArrayLength = declaration.TypeDefinition.ConstCount.Value;
                    globalVariable.Type = arrayType.ElementType;
                    globalVariable.Size = globalVariable.ArrayLength * arrayType.ElementType.Size;
                }
                else
                {
                    globalVariable.Type = declaration.Type;
                    globalVariable.Size = declaration.Type.Size;
                }

                if (declaration.Value != null)
                {
                    globalVariable.InitialValue = EmitConstantIR(declaration.Value, scope);
                }
                else
                {
                    switch (declaration.Type.TypeKind)
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
                                    Values = new [] {GetConstantInteger(0), new InstructionValue {ValueType = InstructionValueType.Null, Type = arrayStruct.Fields[1].Type}}
                                };
                                globalVariable.InitialValue = constArray;
                            }
                            break;
                        case TypeKind.CArray:
                            if (declaration.ArrayValues != null)
                            {
                                globalVariable.InitialValue = new InstructionValue
                                {
                                    ValueType = InstructionValueType.ConstantArray, Type = declaration.ArrayElementType,
                                    Values = declaration.ArrayValues.Select(val => EmitConstantIR(val, scope)).ToArray(), ArrayLength = globalVariable.ArrayLength
                                };
                            }
                            break;
                        // Initialize struct field default values
                        case TypeKind.Struct:
                        case TypeKind.String:
                            globalVariable.InitialValue = GetConstantStruct((StructAst)declaration.Type, scope, declaration.Assignments);
                            break;
                        // Initialize pointers to null
                        case TypeKind.Pointer:
                            globalVariable.InitialValue = new InstructionValue {ValueType = InstructionValueType.Null, Type = globalVariable.Type};
                            break;
                        // Or initialize to default
                        default:
                            globalVariable.InitialValue = GetDefaultConstant(declaration.Type);
                            break;
                    }
                }

                globalVariable.Index = declaration.AllocationIndex = Program.GlobalVariables.Count;
                Program.GlobalVariables.Add(globalVariable);
                Program.GlobalVariablesSize += globalVariable.Size;
            }
        }

        private InstructionValue GetConstantStruct(StructAst structDef, ScopeAst scope, Dictionary<string, AssignmentAst> assignments)
        {
            var constantStruct = new InstructionValue {ValueType = InstructionValueType.ConstantStruct, Type = structDef, Values = new InstructionValue[structDef.Fields.Count]};

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
                            Values = new [] {GetConstantInteger(0), new InstructionValue {ValueType = InstructionValueType.Null, Type = arrayStruct.Fields[1].Type}}
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
                    return new InstructionValue {ValueType = InstructionValueType.Null, Type = field.Type};
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
                Name = "____array", Index = arrayIndex, Size = length * elementType.Size,
                Array = true, ArrayLength = length, Type = elementType
            };
            Program.GlobalVariables.Add(arrayVariable);

            if (declaration.ArrayValues != null)
            {
                arrayVariable.InitialValue = new InstructionValue
                {
                    ValueType = InstructionValueType.ConstantArray, Type = declaration.ArrayElementType,
                    Values = declaration.ArrayValues.Select(val => EmitConstantIR(val, scope)).ToArray(), ArrayLength = length
                };
            }

            return new InstructionValue
            {
                ValueType = InstructionValueType.ConstantStruct, Type = arrayStruct,
                Values = new [] {GetConstantInteger(length), new InstructionValue {ValueIndex = arrayIndex}}
            };
        }

        private BasicBlock EmitScope(FunctionIR function, BasicBlock block, ScopeAst scope, IType returnType, BasicBlock breakBlock, BasicBlock continueBlock)
        {
            if (!BuildSettings.Release)
            {
                function.Instructions.Add(new Instruction {Type = InstructionType.DebugPushLexicalBlock, Source = scope});
                block = EmitScopeChildren(function, block, scope, returnType, breakBlock, continueBlock);
                function.Instructions.Add(new Instruction {Type = InstructionType.DebugPopLexicalBlock});
                return block;
            }
            else
            {
                return EmitScopeChildren(function, block, scope, returnType, breakBlock, continueBlock);
            }
        }

        private BasicBlock EmitScopeChildren(FunctionIR function, BasicBlock block, ScopeAst scope, IType returnType, BasicBlock breakBlock, BasicBlock continueBlock)
        {
            foreach (var ast in scope.Children)
            {
                if (!BuildSettings.Release)
                {
                    function.Instructions.Add(new Instruction {Type = InstructionType.DebugSetLocation, Source = ast});
                }

                switch (ast)
                {
                    case ReturnAst returnAst:
                        EmitReturn(function, returnAst, returnType, scope);
                        scope.Returns = true;
                        return block;
                    case DeclarationAst declaration:
                        EmitDeclaration(function, declaration, scope);
                        break;
                    case AssignmentAst assignment:
                        EmitAssignment(function, assignment, scope);
                        break;
                    case ScopeAst childScope:
                        block = EmitScope(function, block, childScope, returnType, breakBlock, continueBlock);
                        if (childScope.Returns)
                        {
                            scope.Returns = true;
                            return block;
                        }
                        break;
                    case ConditionalAst conditional:
                        block = EmitConditional(function, block, conditional, scope, returnType, breakBlock, continueBlock, out var returns);
                        if (returns)
                        {
                            scope.Returns = true;
                            return block;
                        }
                        break;
                    case WhileAst whileAst:
                        block = EmitWhile(function, block, whileAst, scope, returnType);
                        break;
                    case EachAst each:
                        block = EmitEach(function, block, each, scope, returnType);
                        break;
                    case BreakAst:
                        var breakJump = new Instruction {Type = InstructionType.Jump, Value1 = BasicBlockValue(breakBlock)};
                        function.Instructions.Add(breakJump);
                        scope.Returns = true;
                        return block;
                    case ContinueAst:
                        var continueJump = new Instruction {Type = InstructionType.Jump, Value1 = BasicBlockValue(continueBlock)};
                        function.Instructions.Add(continueJump);
                        scope.Returns = true;
                        return block;
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

                if (!BuildSettings.Release)
                {
                    function.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareVariable, String = declaration.Name, Source = declaration, Value1 = new InstructionValue {ValueType = InstructionValueType.Allocation, ValueIndex = allocationIndex, Type = declaration.Type}});
                }

                if (declaration.Value != null)
                {
                    var value = EmitIR(function, declaration.Value, scope);
                    EmitStore(function, allocationIndex, EmitCastValue(function, value, declaration.Type));
                    return;
                }

                switch (declaration.Type.TypeKind)
                {
                    // Initialize arrays
                    case TypeKind.Array:
                        var arrayStruct = (StructAst)declaration.Type;
                        if (declaration.TypeDefinition.ConstCount != null)
                        {
                            var arrayPointer = InitializeConstArray(function, allocationIndex, arrayStruct, declaration.TypeDefinition.ConstCount.Value, declaration.ArrayElementType);

                            if (declaration.ArrayValues != null)
                            {
                                InitializeArrayValues(function, arrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                            }
                        }
                        else if (declaration.TypeDefinition.Count != null)
                        {
                            function.SaveStack = true;
                            var count = EmitIR(function, declaration.TypeDefinition.Count, scope);
                            var countPointer = EmitGetStructPointer(function, allocationIndex, arrayStruct, 0);
                            EmitStore(function, countPointer, count);

                            var elementType = new InstructionValue {ValueType = InstructionValueType.Type, Type = declaration.ArrayElementType};
                            var arrayData = EmitInstruction(InstructionType.AllocateArray, function, arrayStruct.Fields[1].Type, count, elementType);
                            var dataPointer = EmitGetStructPointer(function, allocationIndex, arrayStruct, 1);
                            EmitStore(function, dataPointer, arrayData);
                        }
                        else
                        {
                            var lengthPointer = EmitGetStructPointer(function, allocationIndex, arrayStruct, 0);
                            var lengthValue = GetConstantInteger(0);
                            EmitStore(function, lengthPointer, lengthValue);
                        }
                        break;
                    case TypeKind.CArray:
                        var cArrayPointer = AllocationValue(allocationIndex, declaration.Type);
                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(function, cArrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                        }
                        break;
                        // Initialize struct field default values
                    case TypeKind.Struct:
                    case TypeKind.String:
                        var structPointer = AllocationValue(allocationIndex, declaration.Type);
                        InitializeStruct(function, (StructAst)declaration.Type, structPointer, scope, declaration.Assignments);
                        break;
                        // Initialize pointers to null
                    case TypeKind.Pointer:
                        EmitStore(function, allocationIndex, new InstructionValue {ValueType = InstructionValueType.Null, Type = declaration.Type});
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

        private InstructionValue AllocationValue(int index, IType type, bool global = false)
        {
            return new InstructionValue {ValueType = InstructionValueType.Allocation, ValueIndex = index, Type = type, Global = global};
        }

        private InstructionValue InitializeConstArray(FunctionIR function, int allocationIndex, StructAst arrayStruct, uint length, IType elementType)
        {
            var allocation = AllocationValue(allocationIndex, arrayStruct);
            return InitializeConstArray(function, allocation, arrayStruct, length, elementType);
        }

        private InstructionValue InitializeConstArray(FunctionIR function, InstructionValue arrayPointer, StructAst arrayStruct, uint length, IType elementType)
        {
            var lengthPointer = EmitGetStructPointer(function, arrayPointer, arrayStruct, 0);
            var lengthValue = GetConstantInteger(length);
            EmitStore(function, lengthPointer, lengthValue);

            var dataIndex = AddAllocation(function, elementType, true, length);
            var dataPointerType = arrayStruct.Fields[1].Type;
            var arrayData = AllocationValue(dataIndex, arrayStruct.Fields[1].Type);
            var arrayDataPointer = EmitInstruction(InstructionType.PointerCast, function, dataPointerType, arrayData, new InstructionValue {ValueType = InstructionValueType.Type, Type = dataPointerType});
            var dataPointer = EmitGetStructPointer(function, arrayPointer, arrayStruct, 1);
            EmitStore(function, dataPointer, arrayDataPointer);

            return arrayData;
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
                case TypeKind.String:
                    InitializeStruct(function, (StructAst)field.Type, pointer, scope, field.Assignments);
                    break;
                // Initialize pointers to null
                case TypeKind.Pointer:
                    EmitStore(function, pointer, new InstructionValue {ValueType = InstructionValueType.Null, Type = field.Type});
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
                    value.ConstantValue = new Constant {Double = 0};
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
            if (returnAst.Value == null)
            {
                var instruction = new Instruction {Type = InstructionType.ReturnVoid};
                function.Instructions.Add(instruction);
            }
            else
            {
                var instruction = new Instruction {Type = InstructionType.Return, Value1 = EmitIR(function, returnAst.Value, scope)};
                function.Instructions.Add(instruction);
            }
        }

        private BasicBlock EmitConditional(FunctionIR function, BasicBlock block, ConditionalAst conditional, ScopeAst scope, IType returnType, BasicBlock breakBlock, BasicBlock continueBlock, out bool returns)
        {
            // Run the condition expression in the current basic block and then jump to the following
            var condition = EmitConditionExpression(function, conditional.Condition, scope);
            var conditionJump = new Instruction {Type = InstructionType.ConditionalJump, Value1 = condition};
            function.Instructions.Add(conditionJump);

            var thenBlock = AddBasicBlock(function);
            thenBlock = EmitScope(function, thenBlock, conditional.IfBlock, returnType, breakBlock, continueBlock);
            Instruction jumpToAfter = null;

            // For when the the if block does not return and there is an else block, a jump to the after block is required
            if (!conditional.IfBlock.Returns && conditional.ElseBlock != null)
            {
                jumpToAfter = new Instruction {Type = InstructionType.Jump};
                function.Instructions.Add(jumpToAfter);
            }

            var elseBlock = AddBasicBlock(function);

            // Jump to the else block, otherwise fall through to the then block
            conditionJump.Value2 = BasicBlockValue(elseBlock);

            returns = false;
            if (conditional.ElseBlock == null)
            {
                return elseBlock;
            }

            elseBlock = EmitScope(function, elseBlock, conditional.ElseBlock, returnType, breakBlock, continueBlock);

            if (conditional.IfBlock.Returns && conditional.ElseBlock.Returns)
            {
                returns = true;
                return elseBlock;
            }

            var afterBlock = elseBlock.Location < function.Instructions.Count ? AddBasicBlock(function) : elseBlock;

            if (!conditional.IfBlock.Returns)
            {
                jumpToAfter.Value1 = BasicBlockValue(afterBlock);
            }

            return afterBlock;
        }

        private BasicBlock EmitWhile(FunctionIR function, BasicBlock block, WhileAst whileAst, ScopeAst scope, IType returnType)
        {
            // Create a block for the condition expression and then jump to the following
            var conditionBlock = block.Location < function.Instructions.Count ? AddBasicBlock(function) : block;
            var condition = EmitConditionExpression(function, whileAst.Condition, scope);
            var conditionJump = new Instruction {Type = InstructionType.ConditionalJump, Value1 = condition};
            function.Instructions.Add(conditionJump);

            var whileBodyBlock = AddBasicBlock(function);
            var afterBlock = new BasicBlock();
            whileBodyBlock = EmitScope(function, whileBodyBlock, whileAst.Body, returnType, afterBlock, conditionBlock);
            var jumpToCondition = new Instruction {Type = InstructionType.Jump, Value1 = BasicBlockValue(conditionBlock)};
            function.Instructions.Add(jumpToCondition);

            AddBasicBlock(function, afterBlock);
            conditionJump.Value2 = BasicBlockValue(afterBlock);

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
            if (!BuildSettings.Release)
            {
                function.Instructions.Add(new Instruction {Type = InstructionType.DebugPushLexicalBlock, Source = each.Body});
                function.Instructions.Add(new Instruction {Type = InstructionType.DebugSetLocation, Source = each});
            }

            var indexVariable = AddAllocation(function, _s32Type);
            InstructionValue compareTarget;
            InstructionValue arrayData = null;
            var cArrayIteration = false;

            if (each.Iteration != null)
            {
                if (each.IndexVariable != null)
                {
                    each.IndexVariableVariable.AllocationIndex = indexVariable;
                    if (!BuildSettings.Release)
                    {
                        function.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareVariable, String = each.IndexVariable, Source = each.IndexVariableVariable, Value1 = new InstructionValue {ValueType = InstructionValueType.Allocation, ValueIndex = indexVariable, Type = _s32Type}});
                    }
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

                    var lengthPointer = EmitGetStructPointer(function, iterationVariable, arrayDef, 0);
                    compareTarget = EmitLoad(function, _s32Type, lengthPointer);

                    var dataField = arrayDef.Fields[1];
                    var dataPointer = EmitGetStructPointer(function, iterationVariable, arrayDef, 1, dataField);
                    arrayData = EmitLoad(function, dataField.Type, dataPointer);
                }
            }
            else
            {
                // Begin the loop at the beginning of the range
                var value = EmitIR(function, each.RangeBegin, scope);

                EmitStore(function, indexVariable, value);
                each.IterationVariableVariable.AllocationIndex = indexVariable;
                if (!BuildSettings.Release)
                {
                    function.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareVariable, String = each.IterationVariable, Source = each.IterationVariableVariable, Value1 = new InstructionValue {ValueType = InstructionValueType.Allocation, ValueIndex = indexVariable, Type = _s32Type}});
                }

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

                if (!BuildSettings.Release)
                {
                    function.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareVariable, String = each.IterationVariable, Source = each.IterationVariableVariable, Value1 = iterationVariable});
                }
            }
            var conditionJump = new Instruction {Type = InstructionType.ConditionalJump, Value1 = condition};
            function.Instructions.Add(conditionJump);

            var eachBodyBlock = AddBasicBlock(function);
            var eachIncrementBlock = new BasicBlock();
            var afterBlock = new BasicBlock();
            eachBodyBlock = EmitScopeChildren(function, eachBodyBlock, each.Body, returnType, afterBlock, eachIncrementBlock);

            AddBasicBlock(function, eachIncrementBlock);
            var nextValue = EmitInstruction(InstructionType.IntegerAdd, function, _s32Type, indexValue, GetConstantInteger(1));
            EmitStore(function, indexVariable, nextValue);
            var jumpToCondition = new Instruction {Type = InstructionType.Jump, Value1 = BasicBlockValue(conditionBlock)};
            function.Instructions.Add(jumpToCondition);

            AddBasicBlock(function, afterBlock);
            conditionJump.Value2 = BasicBlockValue(afterBlock);

            if (!BuildSettings.Release)
            {
                function.Instructions.Add(new Instruction {Type = InstructionType.DebugPopLexicalBlock});
            }

            return afterBlock;
        }

        private InstructionValue BasicBlockValue(BasicBlock jumpBlock)
        {
            return new InstructionValue {ValueType = InstructionValueType.BasicBlock, JumpBlock = jumpBlock};
        }

        private BasicBlock AddBasicBlock(FunctionIR function)
        {
            var block = new BasicBlock {Index = function.BasicBlocks.Count, Location = function.Instructions.Count};
            function.BasicBlocks.Add(block);
            return block;
        }

        private BasicBlock AddBasicBlock(FunctionIR function, BasicBlock block)
        {
            block.Index = function.BasicBlocks.Count;
            block.Location = function.Instructions.Count;
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
                    return new InstructionValue {ValueType = InstructionValueType.Null, Type = nullAst.TargetType};
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

                            var dataPointer = EmitGetStructPointer(function, declaration.AllocationIndex, _stringStruct, 1, dataField, global);
                            return EmitLoadPointer(function, dataField.Type, dataPointer);
                        }
                        return EmitLoad(function, declaration.Type, declaration.AllocationIndex, global);
                    }
                    else if (identifier is VariableAst variable)
                    {
                        if (useRawString && variable.Type.TypeKind == TypeKind.String)
                        {
                            _stringStruct ??= (StructAst) variable.Type;
                            var dataField = _stringStruct.Fields[1];

                            var stringPointer = variable.AllocationIndex.HasValue ? AllocationValue(variable.AllocationIndex.Value, variable.Type, global) : variable.Pointer;

                            var dataPointer = EmitGetStructPointer(function, stringPointer, _stringStruct, 1, dataField);
                            return EmitLoadPointer(function, dataField.Type, dataPointer);
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
                        constOne.ConstantValue = new Constant {Double = 1};
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
                            return EmitLoad(function, unary.Type, value);
                        case UnaryOperator.Reference:
                            value = EmitGetReference(function, unary.Value, scope);
                            if (value.Type.TypeKind == TypeKind.CArray)
                            {
                                return EmitInstruction(InstructionType.PointerCast, function, unary.Type, value, new InstructionValue {ValueType = InstructionValueType.Type, Type = unary.Type});
                            }
                            return new InstructionValue {ValueType = value.ValueType, ValueIndex = value.ValueIndex, Type = unary.Type};
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
                            expressionValue = EmitCall(function, overload.Name, new []{expressionValue, rhs}, overload.ReturnType);
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
                    return new InstructionValue {ValueType = InstructionValueType.Null, Type = nullAst.TargetType};
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
                    value.ConstantValue = new Constant {Double = double.Parse(constant.Value)};
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
                            return AllocationValue(declaration.AllocationIndex, declaration.Type, global);
                        case VariableAst variable:
                            if (variable.AllocationIndex.HasValue)
                            {
                                return AllocationValue(variable.AllocationIndex.Value, variable.Type, global);
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
                            value = AllocationValue(declaration.AllocationIndex, declaration.Type, global);
                            break;
                        case VariableAst variable:
                            value = variable.AllocationIndex.HasValue ? AllocationValue(variable.AllocationIndex.Value, variable.Type, global) : variable.Pointer;
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
                        value = AllocationValue(allocationIndex, value.Type);
                    }
                    break;
                case CallAst call:
                    value = EmitCall(function, call, scope);
                    if (!structField.Pointers[0])
                    {
                        var type = structField.Types[0];
                        var allocationIndex = AddAllocation(function, type);
                        EmitStore(function, allocationIndex, value);
                        value = AllocationValue(allocationIndex, value.Type);
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
                        value = EmitLoadPointer(function, type, value);
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
                                value = AllocationValue(allocationIndex, nextType);
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
            var argumentCount = call.Function.Flags.HasFlag(FunctionFlags.Varargs) ? call.Arguments.Count : call.Function.Arguments.Count;
            var arguments = new InstructionValue[argumentCount];

            if (call.Function.Flags.HasFlag(FunctionFlags.Params))
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
                var dataPointer = InitializeConstArray(function, paramsAllocationIndex, (StructAst)paramsType, (uint)(call.Arguments.Count - call.Function.Arguments.Count + 1), elementType);

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
            else if (call.Function.Flags.HasFlag(FunctionFlags.Varargs))
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
                    var argument = EmitIR(function, call.Arguments[i], scope, call.Function.Flags.HasFlag(FunctionFlags.Extern));
                    arguments[i] = EmitCastValue(function, argument, call.Function.Arguments[i].Type);
                }
            }

            return EmitCall(function, GetFunctionName(call.Function), arguments, call.Function.ReturnType, call.VarargsIndex);
        }

        private InstructionValue EmitGetIndexPointer(FunctionIR function, IndexAst index, ScopeAst scope, IType type = null, InstructionValue variable = null)
        {
            if (type == null)
            {
                var declaration = (DeclarationAst) GetScopeIdentifier(scope, index.Name, out var global);
                type = declaration.Type;
                variable = AllocationValue(declaration.AllocationIndex, declaration.Type, global);
            }

            var indexValue = EmitIR(function, index.Index, scope);

            if (index.CallsOverload)
            {
                var value = EmitLoad(function, type, variable);
                return EmitCall(function, index.Overload.Name, new []{value, indexValue}, index.Overload.ReturnType);
            }

            IType elementType;
            if (type.TypeKind == TypeKind.Pointer)
            {
                var pointerType = (PrimitiveAst)type;
                elementType = pointerType.PointerType;

                var dataPointer = EmitLoadPointer(function, type, variable);
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
                var dataPointer = EmitLoadPointer(function, data.Type, data);
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

        private InstructionValue EmitCastValue(FunctionIR function, InstructionValue value, IType targetType)
        {
            if (value.Type == targetType || value.UseRawString)
            {
                return value;
            }

            var castInstruction = new Instruction {Value1 = value, Value2 = new InstructionValue {ValueType = InstructionValueType.Type, Type = targetType}};

            switch (targetType.TypeKind)
            {
                case TypeKind.Integer:
                {
                    var targetIntegerType = (PrimitiveAst)targetType;
                    switch (value.Type.TypeKind)
                    {
                        case TypeKind.Integer:
                            var sourceIntegerType = (PrimitiveAst)value.Type;
                            castInstruction.Type = GetIntegerCastType(sourceIntegerType, targetIntegerType);
                            break;
                        case TypeKind.Enum:
                            var enumType = (EnumAst)value.Type;
                            sourceIntegerType = (PrimitiveAst)enumType.BaseType;
                            castInstruction.Type = GetIntegerCastType(sourceIntegerType, targetIntegerType);
                            break;
                        case TypeKind.Float:
                            castInstruction.Type = targetIntegerType.Primitive.Signed ? InstructionType.FloatToIntegerCast: InstructionType.FloatToUnsignedIntegerCast;
                            break;
                    }
                    break;
                }
                case TypeKind.Float:
                    if (value.Type.TypeKind == TypeKind.Float)
                    {
                        castInstruction.Type = InstructionType.FloatCast;
                    }
                    else
                    {
                        var integerType = (PrimitiveAst)value.Type;
                        castInstruction.Type = integerType.Primitive.Signed ? InstructionType.IntegerToFloatCast : InstructionType.UnsignedIntegerToFloatCast;
                    }
                    break;
                case TypeKind.Pointer:
                    castInstruction.Type = InstructionType.PointerCast;
                    break;
            }

            return AddInstruction(function, castInstruction, targetType);
        }

        private InstructionType GetIntegerCastType(PrimitiveAst sourceType, PrimitiveAst targetType)
        {
            if (targetType.Size >= sourceType.Size)
            {
                if (targetType.Primitive.Signed)
                {
                    return sourceType.Primitive.Signed ? InstructionType.IntegerExtend : InstructionType.UnsignedIntegerToIntegerExtend;
                }
                return sourceType.Primitive.Signed ? InstructionType.IntegerToUnsignedIntegerExtend : InstructionType.UnsignedIntegerExtend;
            }
            else
            {
                if (targetType.Primitive.Signed)
                {
                    return sourceType.Primitive.Signed ? InstructionType.IntegerTruncate : InstructionType.UnsignedIntegerToIntegerTruncate;
                }
                return sourceType.Primitive.Signed ? InstructionType.IntegerToUnsignedIntegerTruncate : InstructionType.UnsignedIntegerTruncate;
            }
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

        private InstructionValue EmitLoad(FunctionIR function, IType type, InstructionValue value)
        {
            var loadInstruction = new Instruction {Type = InstructionType.Load, Value1 = value};
            return AddInstruction(function, loadInstruction, type);
        }

        private InstructionValue EmitLoad(FunctionIR function, IType type, int allocationIndex, bool global = false)
        {
            var loadInstruction = new Instruction {Type = InstructionType.Load, Value1 = AllocationValue(allocationIndex, type, global)};
            return AddInstruction(function, loadInstruction, type);
        }

        private InstructionValue EmitLoadPointer(FunctionIR function, IType type, InstructionValue value)
        {
            var loadInstruction = new Instruction {Type = InstructionType.LoadPointer, Value1 = value};
            return AddInstruction(function, loadInstruction, type);
        }

        public InstructionValue EmitGetPointer(FunctionIR function, int allocationIndex, InstructionValue index, IType type, bool getFirstPointer = false, bool global = false)
        {
            var allocation = AllocationValue(allocationIndex, type, global);
            return EmitGetPointer(function, allocation, index, type, getFirstPointer);
        }

        private InstructionValue EmitGetPointer(FunctionIR function, InstructionValue pointer, InstructionValue index, IType type, bool getFirstPointer = false)
        {
            var instruction = new Instruction
            {
                Type = InstructionType.GetPointer, Offset = type.Size, Value1 = pointer,
                Value2 = index, GetFirstPointer = getFirstPointer
            };
            return AddInstruction(function, instruction, type);
        }

        private InstructionValue EmitGetStructPointer(FunctionIR function, int allocationIndex, StructAst structDef, int fieldIndex, StructFieldAst field = null, bool global = false)
        {
            var allocation = AllocationValue(allocationIndex, structDef, global);
            return EmitGetStructPointer(function, allocation, structDef, fieldIndex, field);
        }

        private InstructionValue EmitGetStructPointer(FunctionIR function, InstructionValue value, StructAst structDef, int fieldIndex, StructFieldAst field = null)
        {
            if (field == null)
            {
                 field = structDef.Fields[fieldIndex];
            }

            var instruction = new Instruction {Type = InstructionType.GetStructPointer, Index = fieldIndex, Offset = field.Offset, Value1 = value};
            return AddInstruction(function, instruction, field.Type);
        }


        private InstructionValue EmitCall(FunctionIR function, string name, InstructionValue[] arguments, IType returnType, int callIndex = 0)
        {
            var callInstruction = new Instruction
            {
                Type = InstructionType.Call, Index = callIndex, String = name,
                Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Values = arguments}
            };
            return AddInstruction(function, callInstruction, returnType);
        }

        private void EmitStore(FunctionIR function, int allocationIndex, InstructionValue value)
        {
            var allocation = AllocationValue(allocationIndex, value.Type);
            EmitStore(function, allocation, value);
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
            /* #if DEBUG
            if (instruction.Type == InstructionType.None)
            {
                Console.WriteLine("Instruction did not have an assigned type");
                throw new Exception();
            }
            #endif */

            var valueIndex = function.ValueCount++;
            instruction.ValueIndex = valueIndex;
            var value = new InstructionValue {ValueIndex = valueIndex, Type = type};
            function.Instructions.Add(instruction);
            return value;
        }
    }
}
