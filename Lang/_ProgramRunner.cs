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
                        var condition = GetValue(instruction.Value1, registers, stackPointer);
                        if (condition.Bool)
                        {
                            instructionPointer = instruction.Value2.JumpBlock.Location;
                        }
                        break;
                    }
                    case InstructionType.Return:
                    {
                        return GetValue(instruction.Value1, registers, stackPointer);
                    }
                    case InstructionType.ReturnVoid:
                    {
                        return new Register();
                    }
                    case InstructionType.Load:
                    {
                        // TODO Implement me
                        // var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildLoad(value);
                        break;
                    }
                    case InstructionType.Store:
                    {
                        // TODO Implement me
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var value = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // _builder.BuildStore(value, pointer);
                        break;
                    }
                    case InstructionType.GetPointer:
                    {
                        // TODO Implement me
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildGEP(pointer, instruction.GetFirstPointer ? new []{_zeroInt, index} : new []{index});
                        break;
                    }
                    case InstructionType.GetStructPointer:
                    {
                        // TODO Implement me
                        // var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        // values[instruction.ValueIndex] = _builder.BuildStructGEP(pointer, (uint)instruction.Index);
                        break;
                    }
                    case InstructionType.Call:
                    {
                        // TODO Implement me
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
                    case InstructionType.IntegerTruncate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        switch (instruction.Value2.Type.Size)
                        {
                            case 1:
                                register.SByte = instruction.Value1.Type.Size switch
                                {
                                    1 => value.SByte,
                                    2 => (sbyte)value.Short,
                                    4 => (sbyte)value.Integer,
                                    8 => (sbyte)value.Long,
                                    _ => (sbyte)value.Integer,
                                };
                                break;
                            case 2:
                                register.Short = instruction.Value1.Type.Size switch
                                {
                                    1 => (short)value.SByte,
                                    2 => value.Short,
                                    4 => (short)value.Integer,
                                    8 => (short)value.Long,
                                    _ => (short)value.Integer,
                                };
                                break;
                            case 4:
                                register.Integer = instruction.Value1.Type.Size switch
                                {
                                    1 => (int)value.SByte,
                                    2 => (int)value.Short,
                                    8 => (int)value.Long,
                                    _ => value.Integer,
                                };
                                break;
                            case 8:
                                register.Long = instruction.Value1.Type.Size switch
                                {
                                    1 => (long)value.SByte,
                                    2 => (long)value.Short,
                                    4 => (long)value.Integer,
                                    8 => value.Long,
                                    _ => (long)value.Integer,
                                };
                                break;
                            default:
                                register.Integer = instruction.Value1.Type.Size switch
                                {
                                    1 => (int)value.SByte,
                                    2 => (int)value.Short,
                                    8 => (int)value.Long,
                                    _ => value.Integer,
                                };
                                break;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.UnsignedIntegerToIntegerExtend:
                    case InstructionType.UnsignedIntegerToIntegerTruncate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        switch (instruction.Value2.Type.Size)
                        {
                            case 1:
                                register.SByte = (sbyte)value.ULong;
                                break;
                            case 2:
                                register.Short = (short)value.ULong;
                                break;
                            case 4:
                                register.Integer = (int)value.ULong;
                                break;
                            case 8:
                                register.Long = (long)value.ULong;
                                break;
                            default:
                                register.Integer = (int)value.ULong;
                                break;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.UnsignedIntegerExtend:
                    case InstructionType.UnsignedIntegerTruncate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        switch (instruction.Value2.Type.Size)
                        {
                            case 1:
                                register.Byte = (byte)value.ULong;
                                break;
                            case 2:
                                register.UShort = (ushort)value.ULong;
                                break;
                            case 4:
                                register.UInteger = (uint)value.ULong;
                                break;
                            case 8:
                                register.ULong = value.ULong;
                                break;
                            default:
                                register.UInteger = (uint)value.ULong;
                                break;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.IntegerToUnsignedIntegerExtend:
                    case InstructionType.IntegerToUnsignedIntegerTruncate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        switch (instruction.Value2.Type.Size)
                        {
                            case 1:
                                register.Byte = instruction.Value1.Type.Size switch
                                {
                                    1 => (byte)value.SByte,
                                    2 => (byte)value.Short,
                                    4 => (byte)value.Integer,
                                    8 => (byte)value.Long,
                                    _ => (byte)value.Integer,
                                };
                                break;
                            case 2:
                                register.UShort = instruction.Value1.Type.Size switch
                                {
                                    1 => (ushort)value.SByte,
                                    2 => (ushort)value.Short,
                                    4 => (ushort)value.Integer,
                                    8 => (ushort)value.Long,
                                    _ => (ushort)value.Integer,
                                };
                                break;
                            case 4:
                                register.UInteger = instruction.Value1.Type.Size switch
                                {
                                    1 => (uint)value.SByte,
                                    2 => (uint)value.Short,
                                    8 => (uint)value.Long,
                                    _ => (uint)value.Integer,
                                };
                                break;
                            case 8:
                                register.ULong = instruction.Value1.Type.Size switch
                                {
                                    1 => (ulong)value.SByte,
                                    2 => (ulong)value.Short,
                                    4 => (ulong)value.Integer,
                                    8 => (ulong)value.Long,
                                    _ => (ulong)value.Integer,
                                };
                                break;
                            default:
                                register.UInteger = instruction.Value1.Type.Size switch
                                {
                                    1 => (uint)value.SByte,
                                    2 => (uint)value.Short,
                                    8 => (uint)value.Long,
                                    _ => (uint)value.Integer,
                                };
                                break;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.IntegerToFloatCast:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value2.Type.Size == 4)
                        {
                            register.Float = instruction.Value1.Type.Size switch
                            {
                                1 => (float)value.SByte,
                                2 => (float)value.Short,
                                4 => (float)value.Integer,
                                8 => (float)value.Long,
                                _ => (float)value.Integer,
                            };
                        }
                        else
                        {
                            register.Double = instruction.Value1.Type.Size switch
                            {
                                1 => (double)value.SByte,
                                2 => (double)value.Short,
                                4 => (double)value.Integer,
                                8 => (double)value.Long,
                                _ => (double)value.Integer,
                            };
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.UnsignedIntegerToFloatCast:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value2.Type.Size == 4)
                        {
                            register.Float = (float)value.ULong;
                        }
                        else
                        {
                            register.Double = (double)value.ULong;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.FloatCast:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value2.Type.Size == 4)
                        {
                            register.Float = (float)value.Double;
                        }
                        else
                        {
                            register.Double = (double)value.Float;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.FloatToIntegerCast:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            switch (instruction.Value2.Type.Size)
                            {
                                case 1:
                                    register.SByte = (sbyte)value.Float;
                                    break;
                                case 2:
                                    register.Short = (short)value.Float;
                                    break;
                                case 4:
                                    register.Integer = (int)value.Float;
                                    break;
                                case 8:
                                    register.Long = (long)value.Float;
                                    break;
                                default:
                                    register.Integer = (int)value.Float;
                                    break;
                            };
                        }
                        else
                        {
                            switch (instruction.Value2.Type.Size)
                            {
                                case 1:
                                    register.SByte = (sbyte)value.Double;
                                    break;
                                case 2:
                                    register.Short = (short)value.Double;
                                    break;
                                case 4:
                                    register.Integer = (int)value.Double;
                                    break;
                                case 8:
                                    register.Long = (long)value.Double;
                                    break;
                                default:
                                    register.Integer = (int)value.Double;
                                    break;
                            };
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.FloatToUnsignedIntegerCast:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.ULong = (ulong)value.Float;
                        }
                        else
                        {
                            register.ULong = (ulong)value.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.PointerCast:
                    {
                        // This instruction is mostly for LLVM, so this is pretty much a no-op
                        registers[instruction.ValueIndex] = GetValue(instruction.Value1, registers, stackPointer);
                        break;
                    }
                    case InstructionType.AllocateArray:
                    {
                        var length = GetValue(instruction.Value1, registers, stackPointer);
                        var arrayPointer = Marshal.AllocHGlobal((int)instruction.Value2.Type.Size * length.Integer);
                        registers[instruction.ValueIndex] = new Register {Pointer = arrayPointer};
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
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        switch (instruction.Value1.Type.Size)
                        {
                            case 1:
                                register.SByte = (sbyte)-value.SByte;
                                break;
                            case 2:
                                register.Short = (short)-value.Short;
                                break;
                            case 4:
                                register.Integer = -value.Integer;
                                break;
                            case 8:
                                register.Long = -value.Long;
                                break;
                            default:
                                register.Integer = -value.Integer;
                                break;
                        };
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.FloatNegate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = -value.Float;
                        }
                        else
                        {
                            register.Double = -value.Double;
                        }
                        registers[instruction.ValueIndex] = register;
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var bitwiseAnd = lhs.ULong & rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = bitwiseAnd};
                        break;
                    }
                    case InstructionType.BitwiseOr:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var bitwiseOr = lhs.ULong | rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = bitwiseOr};
                        break;
                    }
                    case InstructionType.Xor:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var xor = lhs.ULong ^ rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = xor};
                        break;
                    }
                    case InstructionType.PointerEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var equals = lhs.Pointer == rhs.Pointer;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.IntegerEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var equals = lhs.Long == rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.FloatEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var equals = instruction.Value1.Type.Size == 4 ? lhs.Float == rhs.Float : lhs.Double == rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.PointerNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var notEquals = lhs.Pointer != rhs.Pointer;
                        registers[instruction.ValueIndex] = new Register {Bool = notEquals};
                        break;
                    }
                    case InstructionType.IntegerNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var notEquals = lhs.Long != rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = notEquals};
                        break;
                    }
                    case InstructionType.FloatNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var equals = instruction.Value1.Type.Size == 4 ? lhs.Float != rhs.Float : lhs.Double != rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.IntegerGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var greaterThan = lhs.Long > rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThan};
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var greaterThan = lhs.ULong > rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThan};
                        break;
                    }
                    case InstructionType.FloatGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var greaterThan = instruction.Value1.Type.Size == 4 ? lhs.Float > rhs.Float : lhs.Double > rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThan};
                        break;
                    }
                    case InstructionType.IntegerGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var greaterThanOrEqual = lhs.Long >= rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThanOrEqual};
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var greaterThanOrEqual = lhs.ULong >= rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThanOrEqual};
                        break;
                    }
                    case InstructionType.FloatGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var greaterThanOrEqual = instruction.Value1.Type.Size == 4 ? lhs.Float >= rhs.Float : lhs.Double >= rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThanOrEqual};
                        break;
                    }
                    case InstructionType.IntegerLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var lessThan = lhs.Long < rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThan};
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var lessThan = lhs.ULong < rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThan};
                        break;
                    }
                    case InstructionType.FloatLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var lessThan = instruction.Value1.Type.Size == 4 ? lhs.Float < rhs.Float : lhs.Double < rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThan};
                        break;
                    }
                    case InstructionType.IntegerLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var lessThanOrEqual = lhs.Long <= rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThanOrEqual};
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var lessThanOrEqual = lhs.ULong <= rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThanOrEqual};
                        break;
                    }
                    case InstructionType.FloatLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var lessThanOrEqual = instruction.Value1.Type.Size == 4 ? lhs.Float <= rhs.Float : lhs.Double <= rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThanOrEqual};
                        break;
                    }
                    case InstructionType.PointerAdd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var pointerAdd = lhs.Pointer + rhs.Integer; // TODO Get the pointer offset
                        registers[instruction.ValueIndex] = new Register {Pointer = pointerAdd};
                        break;
                    }
                    case InstructionType.IntegerAdd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var add = lhs.Long + rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = add};
                        break;
                    }
                    case InstructionType.FloatAdd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = lhs.Float + rhs.Float;
                        }
                        else
                        {
                            register.Double = lhs.Double + rhs.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.PointerSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var pointerSubtract = lhs.Pointer - rhs.Integer; // TODO Get the pointer offset
                        registers[instruction.ValueIndex] = new Register {Pointer = pointerSubtract};
                        break;
                    }
                    case InstructionType.IntegerSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var subtract = lhs.Long - rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = subtract};
                        break;
                    }
                    case InstructionType.FloatSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = lhs.Float + rhs.Float;
                        }
                        else
                        {
                            register.Double = lhs.Double + rhs.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.IntegerMultiply:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var multiply = lhs.Long * rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = multiply};
                        break;
                    }
                    case InstructionType.FloatMultiply:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = lhs.Float * rhs.Float;
                        }
                        else
                        {
                            register.Double = lhs.Double * rhs.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.IntegerDivide:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var divide = lhs.Long / rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = divide};
                        break;
                    }
                    case InstructionType.UnsignedIntegerDivide:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var divide = lhs.ULong / rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = divide};
                        break;
                    }
                    case InstructionType.FloatDivide:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = lhs.Float / rhs.Float;
                        }
                        else
                        {
                            register.Double = lhs.Double / rhs.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.IntegerModulus:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var modulus = lhs.Long % rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = modulus};
                        break;
                    }
                    case InstructionType.UnsignedIntegerModulus:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var modulus = lhs.ULong % rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = modulus};
                        break;
                    }
                    case InstructionType.FloatModulus:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = lhs.Float % rhs.Float;
                        }
                        else
                        {
                            register.Double = lhs.Double % rhs.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.ShiftRight:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var shift = lhs.ULong >> rhs.Integer;
                        registers[instruction.ValueIndex] = new Register {ULong = shift};
                        break;
                    }
                    case InstructionType.ShiftLeft:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var shift = lhs.ULong << rhs.Integer;
                        registers[instruction.ValueIndex] = new Register {ULong = shift};
                        break;
                    }
                    case InstructionType.RotateRight:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var shift = lhs.ULong >> rhs.Integer;
                        registers[instruction.ValueIndex] = new Register {ULong = shift};

                        var maskSize = (int)instruction.Value1.Type.Size * 8;
                        var maskShift = maskSize - rhs.Integer;
                        var mask = lhs.ULong << maskShift;

                        var result = shift | mask;
                        registers[instruction.ValueIndex] = new Register {ULong = result};
                        break;
                    }
                    case InstructionType.RotateLeft:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer);
                        var shift = lhs.ULong << rhs.Integer;
                        registers[instruction.ValueIndex] = new Register {ULong = shift};

                        var maskSize = (int)instruction.Value1.Type.Size * 8;
                        var maskShift = maskSize - rhs.Integer;
                        var mask = lhs.ULong >> maskShift;

                        var result = shift | mask;
                        registers[instruction.ValueIndex] = new Register {ULong = result};
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
