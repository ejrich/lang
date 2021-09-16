using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lang
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Register
    {
        [FieldOffset(0)] public bool Bool;
        [FieldOffset(0)] public sbyte SByte;
        [FieldOffset(0)] public byte Byte;
        [FieldOffset(0)] public short Short;
        [FieldOffset(0)] public ushort UShort;
        [FieldOffset(0)] public int Integer;
        [FieldOffset(0)] public uint UInteger;
        [FieldOffset(0)] public long Long;
        [FieldOffset(0)] public ulong ULong;
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public double Double;
        [FieldOffset(0)] public IntPtr Pointer;
    }

    public class _ProgramRunner //: IProgramRunner
    {
        private readonly Dictionary<string, string> _compilerFunctions = new() {
            { "add_dependency", "AddDependency" }
        };

        public void Init()
        {
            // How this should work
            // - When a type/function is added to the TypeTable, add the TypeInfo object to the array
            // - When function IR is built and the function is extern, create the function ref
            // - When a global variable is added, store them in the global space
        }

        public void RunProgram(FunctionIR function, IAst source)
        {
            try
            {
                var returnRegister = ExecuteFunction(function, new IntPtr[0]);
            }
            catch (Exception e)
            {
                ErrorReporter.Report("Internal compiler error running program", source);
                #if DEBUG
                Console.WriteLine(e);
                #endif
            }
        }

        public bool ExecuteCondition(FunctionIR function, IAst source)
        {
            try
            {
                var returnRegister = ExecuteFunction(function, new IntPtr[0]);
                return returnRegister.Bool;
            }
            catch (Exception e)
            {
                ErrorReporter.Report("Internal compiler error executing condition", source);
                #if DEBUG
                Console.WriteLine(e);
                #endif
                return false;
            }
        }

        private void AddDependency(string library)
        {
            BuildSettings.Dependencies.Add(library);
        }

        private Register ExecuteFunction(FunctionIR function, IntPtr[] arguments)
        {
            var instructionPointer = 0;
            var stackPointer = Marshal.AllocHGlobal((int)function.StackSize);
            var registers = new Register[function.ValueCount];

            while (true)
            {
                var instruction = function.Instructions[instructionPointer++];

                switch (instruction.Type)
                {
                    case InstructionType.Jump:
                    {
                        instructionPointer = instruction.Value1.JumpBlock.Location;
                        break;
                    }
                    case InstructionType.ConditionalJump:
                    {
                        // var condition = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // instructionPointer = instruction.Value2.JumpBlock.Location;
                        break;
                    }
                    case InstructionType.Return:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // _builder.BuildRet(value);
                        return new Register();
                    }
                    case InstructionType.ReturnVoid:
                    {
                        return new Register();
                    }
                    case InstructionType.Load:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildLoad(value);
                        break;
                    }
                    case InstructionType.Store:
                    {
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var value = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // _builder.BuildStore(value, pointer);
                        break;
                    }
                    case InstructionType.GetPointer:
                    {
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildGEP(pointer, instruction.GetFirstPointer ? new []{_zeroInt, index} : new []{index});
                        break;
                    }
                    case InstructionType.GetStructPointer:
                    {
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildStructGEP(pointer, (uint)instruction.Index);
                        break;
                    }
                    case InstructionType.Call:
                    {
                        // var callFunction = GetOrCreateFunctionDefinition(instruction.String);
                        // var arguments = new LLVMValueRef[instruction.Value1.Values.Length];
                        // for (var i = 0; i < instruction.Value1.Values.Length; i++)
                        // {
                        //     arguments[i] = GetValue(instruction.Value1.Values[i], values, allocations, functionPointer);
                        // }
                        // values[instruction.ValueIndex] = _builder.BuildCall(callFunction, arguments);
                        break;
                    }
                    case InstructionType.IntegerExtend:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildSExtOrBitCast(value, targetType);
                        break;
                    }
                    case InstructionType.UnsignedIntegerExtend:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildZExtOrBitCast(value, targetType);
                        break;
                    }
                    case InstructionType.IntegerTruncate:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildTrunc(value, targetType);
                        break;
                    }
                    case InstructionType.IntegerToFloatCast:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildSIToFP(value, targetType);
                        break;
                    }
                    case InstructionType.UnsignedIntegerToFloatCast:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildUIToFP(value, targetType);
                        break;
                    }
                    case InstructionType.FloatCast:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildFPCast(value, targetType);
                        break;
                    }
                    case InstructionType.FloatToIntegerCast:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildFPToSI(value, targetType);
                        break;
                    }
                    case InstructionType.FloatToUnsignedIntegerCast:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildFPToUI(value, targetType);
                        break;
                    }
                    case InstructionType.PointerCast:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var targetType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildBitCast(value, targetType);
                        break;
                    }
                    case InstructionType.AllocateArray:
                    {
                        // var length = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var elementType = _types[instruction.Value2.Type.TypeIndex];
                        // values[instruction.ValueIndex] = _builder.BuildArrayAlloca(elementType, length);
                        break;
                    }
                    case InstructionType.IsNull:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var isNull = value.Pointer == IntPtr.Zero;
                        registers[instruction.ValueIndex] = new Register {Bool = isNull};
                        break;
                    }
                    case InstructionType.IsNotNull:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var isNotNull = value.Pointer != IntPtr.Zero;
                        registers[instruction.ValueIndex] = new Register {Bool = isNotNull};
                        break;
                    }
                    case InstructionType.Not:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var not = !value.Bool;
                        registers[instruction.ValueIndex] = new Register {Bool = not};
                        break;
                    }
                    case InstructionType.IntegerNegate:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildNeg(value);
                        break;
                    }
                    case InstructionType.FloatNegate:
                    {
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFNeg(value);
                        break;
                    }
                    case InstructionType.And:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var and = lhs.Bool && rhs.Bool;
                        registers[instruction.ValueIndex] = new Register {Bool = and};
                        break;
                    }
                    case InstructionType.Or:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var or = lhs.Bool || rhs.Bool;
                        registers[instruction.ValueIndex] = new Register {Bool = or};
                        break;
                    }
                    case InstructionType.BitwiseAnd:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildAnd(lhs, rhs);
                        break;
                    }
                    case InstructionType.BitwiseOr:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildOr(lhs, rhs);
                        break;
                    }
                    case InstructionType.Xor:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildXor(lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerEquals:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // var diff = _builder.BuildPtrDiff(lhs, rhs);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, diff, LLVMValueRef.CreateConstInt(LLVM.TypeOf(diff), 0, false));
                        break;
                    }
                    case InstructionType.IntegerEquals:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatEquals:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOEQ, lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerNotEquals:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // var diff = _builder.BuildPtrDiff(lhs, rhs);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, diff, LLVMValueRef.CreateConstInt(LLVM.TypeOf(diff), 0, false));
                        break;
                    }
                    case InstructionType.IntegerNotEquals:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatNotEquals:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealONE, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerGreaterThan:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThan:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatGreaterThan:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerGreaterThanOrEqual:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThanOrEqual:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntUGE, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatGreaterThanOrEqual:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGE, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerLessThan:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThan:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULT, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatLessThan:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerLessThanOrEqual:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThanOrEqual:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULE, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatLessThanOrEqual:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLE, lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerAdd:
                    {
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildGEP(pointer, new []{index});
                        break;
                    }
                    case InstructionType.IntegerAdd:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildAdd(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatAdd:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFAdd(lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerSubtract:
                    {
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // index = _builder.BuildNeg(index);
                        // values[instruction.ValueIndex] = _builder.BuildGEP(pointer, new []{index});
                        break;
                    }
                    case InstructionType.IntegerSubtract:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildSub(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatSubtract:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFSub(lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerMultiply:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildMul(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatMultiply:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFMul(lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerDivide:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildSDiv(lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerDivide:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildUDiv(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatDivide:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFDiv(lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerModulus:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildSRem(lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerModulus:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildURem(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatModulus:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildFRem(lhs, rhs);
                        break;
                    }
                    case InstructionType.ShiftRight:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildAShr(lhs, rhs);
                        break;
                    }
                    case InstructionType.ShiftLeft:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildShl(lhs, rhs);
                        break;
                    }
                    case InstructionType.RotateRight:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // var result = _builder.BuildAShr(lhs, rhs);

                        // var type = instruction.Value1.Type;
                        // var maskSize = LLVMValueRef.CreateConstInt(_types[type.TypeIndex], type.Size * 8, false);
                        // var maskShift = _builder.BuildSub(maskSize, rhs);

                        // var mask = _builder.BuildShl(lhs, maskShift);

                        // values[instruction.ValueIndex] = result.IsUndef ? mask : _builder.BuildOr(result, mask);
                        break;
                    }
                    case InstructionType.RotateLeft:
                    {
                        // var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // var result = _builder.BuildShl(lhs, rhs);

                        // var type = instruction.Value1.Type;
                        // var maskSize = LLVMValueRef.CreateConstInt(_types[type.TypeIndex], type.Size * 8, false);
                        // var maskShift = _builder.BuildSub(maskSize, rhs);

                        // var mask = _builder.BuildAShr(lhs, maskShift);

                        // values[instruction.ValueIndex] = result.IsUndef ? mask : _builder.BuildOr(result, mask);
                        break;
                    }
                }
            }
        }

        private Register GetValue(InstructionValue value, Register[] registers, IntPtr stackPointer)
        {
            switch (value.ValueType)
            {
                case InstructionValueType.Value:
                    return registers[value.ValueIndex];
                case InstructionValueType.Allocation:
                    if (value.Global)
                    {
                        // return _globals[value.ValueIndex];
                    }
                    // return allocations[value.ValueIndex];
                    break;
                case InstructionValueType.Argument:
                    // return functionPointer.GetParam((uint)value.ValueIndex);
                    break;
                case InstructionValueType.Constant:
                    return GetConstant(value);
                case InstructionValueType.Null:
                    return new Register {Pointer = IntPtr.Zero};
            }

            return new Register();
        }

        private static Register GetConstant(InstructionValue value, bool constant = false)
        {
            var register = new Register();
            switch (value.Type.TypeKind)
            {
                case TypeKind.Boolean:
                    register.Bool = value.ConstantValue.Boolean;
                    break;
                case TypeKind.Integer:
                case TypeKind.Enum:
                    // TODO Implement me
                    // return LLVMValueRef.CreateConstInt(_types[value.Type.TypeIndex], value.ConstantValue.UnsignedInteger, false);
                    break;
                case TypeKind.Float:
                    if (value.Type.Size == 4)
                    {
                        register.Float = (float)value.ConstantValue.Double;
                    }
                    else
                    {
                        register.Double = value.ConstantValue.Double;
                    }
                    break;
                case TypeKind.String:
                    register.Pointer = GetString(value.ConstantString, value.UseRawString);
                    break;
            }
            return register;
        }

        private static IntPtr GetString(string value, bool useRawString = false)
        {
            if (useRawString)
            {
                return Marshal.StringToHGlobalAnsi(value);
            }

            const int stringLength = 12;
            var stringPointer = Marshal.AllocHGlobal(stringLength);

            Marshal.StructureToPtr<int>(value.Length, stringPointer, false);
            var s = Marshal.StringToHGlobalAnsi(value);
            Marshal.StructureToPtr<IntPtr>(s, stringPointer + 4, false);

            return stringPointer;
        }
    }
}
