using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Lang
{
    [StructLayout(LayoutKind.Explicit, Size=8)]
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

    [StructLayout(LayoutKind.Explicit, Size=12)]
    public struct String
    {
        [FieldOffset(0)] public int Length;
        [FieldOffset(4)] public IntPtr Data;
    }

    public interface IProgramRunner
    {
        void Init();
        void InitExternFunction(FunctionAst function);
        void InitVarargsFunction(FunctionAst function, IType[] types);
        void RunProgram(FunctionIR function, IAst source);
        bool ExecuteCondition(FunctionIR function, IAst source);
    }

    public unsafe class ProgramRunner : IProgramRunner
    {
        private ModuleBuilder _moduleBuilder;
        private TypeBuilder _functionTypeBuilder;
        private int _version;
        private readonly Dictionary<string, List<MethodInfo>> _externFunctions = new();

        private int _typeCount;
        private IntPtr _typeTablePointer;

        private uint _globalVariablesSize;
        private IntPtr[] _globals;

        public ProgramRunner()
        {
            var assemblyName = new AssemblyName("Runner");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            _moduleBuilder = assemblyBuilder.DefineDynamicModule("Runner");
        }

        public void InitExternFunction(FunctionAst function)
        {
            var functionTypes = new Type[function.Arguments.Count];

            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var argument = function.Arguments[i];
                functionTypes[i] = GetType(argument.Type);
            }

            CreateFunction(function.Name, function.ExternLib, functionTypes);
        }

        public void InitVarargsFunction(FunctionAst function, IType[] types)
        {
            var functionTypes = new Type[types.Length];

            for (var i = 0; i < types.Length; i++)
            {
                functionTypes[i] = GetType(types[i]);
            }

            CreateFunction(function.Name, function.ExternLib, functionTypes);
        }

        private Type GetType(IType type)
        {
            switch (type?.TypeKind)
            {
                case TypeKind.Boolean:
                    return typeof(bool);
                case TypeKind.Integer:
                case TypeKind.Enum:
                case TypeKind.Type:
                    switch (type.Size)
                    {
                        case 1:
                            return typeof(byte);
                        case 2:
                            return typeof(ushort);
                        case 8:
                            return typeof(ulong);
                        default:
                            return typeof(uint);
                    }
                case TypeKind.Float:
                    if (type.Size == 4)
                    {
                        return typeof(float);
                    }
                    else
                    {
                        return typeof(double);
                    }
                default:
                    return typeof(IntPtr);
            }
        }

        private void CreateFunction(string name, string library, Type[] argumentTypes)
        {
            _functionTypeBuilder ??= _moduleBuilder.DefineType($"Functions{_version}", TypeAttributes.Class | TypeAttributes.Public);

            var method = _functionTypeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(Register), argumentTypes);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});

            /* @Future Uncomment this for when shipping on Windows
            var dllImport = typeof(DllImportAttribute);
            var callingConvention = dllImport.GetField("CallingConvention");
            var caBuilder = new CustomAttributeBuilder(dllImport.GetConstructor(new []{typeof(string)}), new []{library}, new []{callingConvention}, new object[]{CallingConvention.Cdecl});
            */
            method.SetCustomAttribute(caBuilder);
        }

        public void Init()
        {
            if (_functionTypeBuilder != null)
            {
                var library = _functionTypeBuilder.CreateType();
                _functionTypeBuilder = null;

                foreach (var function in library.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var argumentCount = function.GetParameters().Length;
                    if (!_externFunctions.TryGetValue(function.Name, out var functions))
                    {
                        _externFunctions[function.Name] = new List<MethodInfo> {function};
                    }
                    else
                    {
                        functions.Add(function);
                    }
                }
                _version++;
            }

            if (_globalVariablesSize < Program.GlobalVariablesSize)
            {
                var i = 0;
                if (_globals == null)
                {
                    _globals = new IntPtr[Program.GlobalVariables.Count];
                }
                else
                {
                    i = _globals.Length;
                    var newGlobals = new IntPtr[Program.GlobalVariables.Count];
                    _globals.CopyTo(newGlobals, 0);
                    _globals = newGlobals;
                }

                var pointer = Allocator.Allocate(Program.GlobalVariablesSize - _globalVariablesSize);
                _globalVariablesSize = Program.GlobalVariablesSize;

                for (; i < Program.GlobalVariables.Count; i++)
                {
                    var variable = Program.GlobalVariables[i];

                    if (_typeTablePointer == IntPtr.Zero && variable.Name == "__type_table")
                    {
                        _typeTablePointer = pointer;
                    }
                    else if (variable.InitialValue != null)
                    {
                         InitializeGlobalVariable(pointer, variable.InitialValue);
                    }

                    _globals[i] = pointer;
                    pointer += (int)variable.Size;
                }
            }

            if (_typeCount != TypeTable.Count)
            {
                _typeCount = TypeTable.Count;

                // Set the data pointer
                var typeInfosArray = TypeTable.TypeInfos.ToArray();
                var arraySize = _typeCount * sizeof(IntPtr);
                var typeTableArrayPointer = Allocator.Allocate(arraySize);
                fixed (IntPtr* pointer = &typeInfosArray[0])
                {
                    Buffer.MemoryCopy(pointer, typeTableArrayPointer.ToPointer(), arraySize, arraySize);
                }

                var typeTableArray = new TypeTable.Array {Length = TypeTable.Count, Data = typeTableArrayPointer};
                Marshal.StructureToPtr(typeTableArray, _typeTablePointer, false);
            }
        }

        private void InitializeGlobalVariable(IntPtr pointer, InstructionValue value)
        {
            switch (value.ValueType)
            {
                case InstructionValueType.Value:
                    var globalPointer = _globals[value.ValueIndex];
                    Marshal.StructureToPtr(globalPointer, pointer, false);
                    break;
                case InstructionValueType.Constant:
                    var constant = GetConstant(value);
                    switch (value.Type.TypeKind)
                    {
                        case TypeKind.Boolean:
                            Marshal.StructureToPtr(constant.Bool, pointer, false);
                            break;
                        case TypeKind.Integer:
                        case TypeKind.Enum:
                            switch (value.Type.Size)
                            {
                                case 1:
                                    Marshal.StructureToPtr(constant.Byte, pointer, false);
                                    break;
                                case 2:
                                    Marshal.StructureToPtr(constant.UShort, pointer, false);
                                    break;
                                case 4:
                                    Marshal.StructureToPtr(constant.UInteger, pointer, false);
                                    break;
                                case 8:
                                    Marshal.StructureToPtr(constant.ULong, pointer, false);
                                    break;
                            }
                            break;
                        case TypeKind.Float:
                            if (value.Type.Size == 4)
                            {
                                Marshal.StructureToPtr(constant.Float, pointer, false);
                            }
                            else
                            {
                                Marshal.StructureToPtr(constant.Double, pointer, false);
                            }
                            break;
                        case TypeKind.String:
                            Buffer.MemoryCopy(constant.Pointer.ToPointer(), pointer.ToPointer(), Allocator.StringLength, Allocator.StringLength);
                            break;
                    }
                    break;
                case InstructionValueType.Null:
                    Marshal.StructureToPtr(IntPtr.Zero, pointer, false);
                    break;
                case InstructionValueType.ConstantStruct:
                    var structDef = (StructAst)value.Type;
                    for (var i = 0; i < value.Values.Length; i++)
                    {
                        var field = structDef.Fields[i];
                        var fieldPointer = pointer + (int)field.Offset;
                        InitializeGlobalVariable(fieldPointer, value.Values[i]);
                    }
                    break;
                case InstructionValueType.ConstantArray when value.Values != null:
                    for (var i = 0; i < value.ArrayLength; i++)
                    {
                        var arrayPointer = pointer + i * (int)value.Type.Size;
                        InitializeGlobalVariable(arrayPointer, value.Values[i]);
                    }
                    break;
            }
        }

        public void RunProgram(FunctionIR function, IAst source)
        {
            try
            {
                var returnRegister = ExecuteFunction(function, new Register[0]);
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
                var returnRegister = ExecuteFunction(function, new Register[0]);
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

        private void AddDependency(String library)
        {
            var lib = Marshal.PtrToStringAnsi(library.Data);
            BuildSettings.Dependencies.Add(lib);
        }

        private Register ExecuteFunction(FunctionIR function, Register[] arguments)
        {
            var instructionPointer = 0;
            var stackPointer = Allocator.Allocate(function.StackSize);
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
                        var condition = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        if (condition.Bool)
                        {
                            instructionPointer = instruction.Value2.JumpBlock.Location;
                        }
                        break;
                    }
                    case InstructionType.Return:
                    {
                        return GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    }
                    case InstructionType.ReturnVoid:
                    {
                        return new Register();
                    }
                    case InstructionType.Load:
                    {
                        var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var register = new Register();
                        switch (instruction.Value1.Type.TypeKind)
                        {
                            case TypeKind.Boolean:
                                var rawValue = Marshal.PtrToStructure<byte>(pointer.Pointer);
                                register.Bool = Convert.ToBoolean(rawValue);
                                break;
                            case TypeKind.Integer:
                            case TypeKind.Enum:
                                switch (instruction.Value1.Type.Size)
                                {
                                    case 1:
                                        register.Byte = Marshal.PtrToStructure<byte>(pointer.Pointer);
                                        break;
                                    case 2:
                                        register.UShort = Marshal.PtrToStructure<ushort>(pointer.Pointer);
                                        break;
                                    case 4:
                                        register.UInteger = Marshal.PtrToStructure<uint>(pointer.Pointer);
                                        break;
                                    case 8:
                                        register.ULong = Marshal.PtrToStructure<ulong>(pointer.Pointer);
                                        break;
                                }
                                break;
                            case TypeKind.Type:
                                register.UInteger = Marshal.PtrToStructure<uint>(pointer.Pointer);
                                break;
                            case TypeKind.Float:
                                if (instruction.Value1.Type.Size == 4)
                                {
                                    register.Float = Marshal.PtrToStructure<float>(pointer.Pointer);
                                }
                                else
                                {
                                    register.Double = Marshal.PtrToStructure<double>(pointer.Pointer);
                                }
                                break;
                            case TypeKind.Pointer:
                                register.Pointer = Marshal.ReadIntPtr(pointer.Pointer);
                                break;
                            case TypeKind.String:
                            case TypeKind.Array:
                            case TypeKind.CArray:
                            case TypeKind.Struct:
                            case TypeKind.Any:
                                // For structs, the pointer is kept in its original state, and any loads will copy the bytes if necessary
                                register.Pointer = pointer.Pointer;
                                break;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.LoadPointer:
                    {
                        var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var loadedPointer = Marshal.ReadIntPtr(pointer.Pointer);
                        registers[instruction.ValueIndex] = new Register {Pointer = loadedPointer};
                        break;
                    }
                    case InstructionType.Store:
                    {
                        var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var value = GetValue(instruction.Value2, registers, stackPointer, function, arguments);

                        switch (instruction.Value2.Type.TypeKind)
                        {
                            case TypeKind.Boolean:
                                Marshal.StructureToPtr(value.Byte, pointer.Pointer, false);
                                break;
                            case TypeKind.Integer:
                            case TypeKind.Enum:
                                switch (instruction.Value2.Type.Size)
                                {
                                    case 1:
                                        Marshal.StructureToPtr(value.Byte, pointer.Pointer, false);
                                        break;
                                    case 2:
                                        Marshal.StructureToPtr(value.UShort, pointer.Pointer, false);
                                        break;
                                    case 4:
                                        Marshal.StructureToPtr(value.UInteger, pointer.Pointer, false);
                                        break;
                                    case 8:
                                        Marshal.StructureToPtr(value.ULong, pointer.Pointer, false);
                                        break;
                                }
                                break;
                            case TypeKind.Type:
                                Marshal.StructureToPtr(value.UInteger, pointer.Pointer, false);
                                break;
                            case TypeKind.Float:
                                if (instruction.Value2.Type.Size == 4)
                                {
                                    Marshal.StructureToPtr(value.Float, pointer.Pointer, false);
                                }
                                else
                                {
                                    Marshal.StructureToPtr(value.Double, pointer.Pointer, false);
                                }
                                break;
                            case TypeKind.Pointer:
                                Marshal.StructureToPtr(value.Pointer, pointer.Pointer, false);
                                break;
                            case TypeKind.String:
                            case TypeKind.Array:
                            case TypeKind.CArray:
                            case TypeKind.Struct:
                            case TypeKind.Any:
                                var copyBytes = instruction.Value2.Type.Size;
                                Buffer.MemoryCopy(value.Pointer.ToPointer(), pointer.Pointer.ToPointer(), copyBytes, copyBytes);
                                break;
                        }
                        break;
                    }
                    case InstructionType.GetPointer:
                    {
                        var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var index = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var indexedPointer = pointer.Pointer + (int)instruction.Offset * index.Integer;
                        registers[instruction.ValueIndex] = new Register {Pointer = indexedPointer};
                        break;
                    }
                    case InstructionType.GetStructPointer:
                    {
                        var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var structPointer = pointer.Pointer + (int)instruction.Offset;
                        registers[instruction.ValueIndex] = new Register {Pointer = structPointer};
                        break;
                    }
                    case InstructionType.Call:
                    {
                        var callingFunction = Program.Functions[instruction.String];

                        if (callingFunction.Source.Flags.HasFlag(FunctionFlags.Extern))
                        {
                            var args = new object[instruction.Value1.Values.Length];
                            for (var i = 0; i < args.Length; i++)
                            {
                                var argument = instruction.Value1.Values[i];
                                var value = GetValue(argument, registers, stackPointer, function, arguments);

                                switch (argument.Type.TypeKind)
                                {
                                    case TypeKind.Boolean:
                                        args[i] = value.Bool;
                                        break;
                                    case TypeKind.Integer:
                                    case TypeKind.Enum:
                                        switch (argument.Type.Size)
                                        {
                                            case 1:
                                                args[i] = value.Byte;
                                                break;
                                            case 2:
                                                args[i] = value.UShort;
                                                break;
                                            case 8:
                                                args[i] = value.ULong;
                                                break;
                                            default:
                                                args[i] = value.UInteger;
                                                break;
                                        }
                                        break;
                                    case TypeKind.Float:
                                        if (argument.Type.Size == 4)
                                        {
                                            args[i] = value.Float;
                                        }
                                        else
                                        {
                                            args[i] = value.Double;
                                        }
                                        break;
                                    default:
                                        args[i] = value.Pointer;
                                        break;
                                }
                            }

                            var functionDecl = _externFunctions[instruction.String][instruction.Index];
                            var returnValue = functionDecl.Invoke(null, args);
                            registers[instruction.ValueIndex] = (Register)returnValue;
                        }
                        else if (callingFunction.Source.Flags.HasFlag(FunctionFlags.Compiler))
                        {
                            var returnValue = new Register();
                            switch (instruction.String)
                            {
                                case "add_dependency":
                                    var value = GetValue(instruction.Value1.Values[0], registers, stackPointer, function, arguments);
                                    var library = Marshal.PtrToStructure<String>(value.Pointer);
                                    AddDependency(library);
                                    break;
                                default:
                                    ErrorReporter.Report($"Undefined compiler function '{callingFunction.Source.Name}'", callingFunction.Source);
                                    break;
                            }
                            registers[instruction.ValueIndex] = returnValue;
                        }
                        else
                        {
                            var args = new Register[instruction.Value1.Values.Length];
                            for (var i = 0; i < instruction.Value1.Values.Length; i++)
                            {
                                args[i] = GetValue(instruction.Value1.Values[i], registers, stackPointer, function, arguments);
                            }

                            registers[instruction.ValueIndex] = ExecuteFunction(callingFunction, args);
                        }
                        break;
                    }
                    case InstructionType.IntegerExtend:
                    case InstructionType.IntegerTruncate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        // These instructions are for LLVM, so this is a no-op
                        registers[instruction.ValueIndex] = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        break;
                    }
                    case InstructionType.AllocateArray:
                    {
                        var length = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var arrayPointer = Allocator.Allocate(instruction.Value2.Type.Size * length.UInteger);
                        registers[instruction.ValueIndex] = new Register {Pointer = arrayPointer};
                        break;
                    }
                    case InstructionType.IsNull:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var isNull = value.Pointer == IntPtr.Zero;
                        registers[instruction.ValueIndex] = new Register {Bool = isNull};
                        break;
                    }
                    case InstructionType.IsNotNull:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var isNotNull = value.Pointer != IntPtr.Zero;
                        registers[instruction.ValueIndex] = new Register {Bool = isNotNull};
                        break;
                    }
                    case InstructionType.Not:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var not = !value.Bool;
                        registers[instruction.ValueIndex] = new Register {Bool = not};
                        break;
                    }
                    case InstructionType.IntegerNegate:
                    {
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var and = lhs.Bool && rhs.Bool;
                        registers[instruction.ValueIndex] = new Register {Bool = and};
                        break;
                    }
                    case InstructionType.Or:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var or = lhs.Bool || rhs.Bool;
                        registers[instruction.ValueIndex] = new Register {Bool = or};
                        break;
                    }
                    case InstructionType.BitwiseAnd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var bitwiseAnd = lhs.ULong & rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = bitwiseAnd};
                        break;
                    }
                    case InstructionType.BitwiseOr:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var bitwiseOr = lhs.ULong | rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = bitwiseOr};
                        break;
                    }
                    case InstructionType.Xor:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var xor = lhs.ULong ^ rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = xor};
                        break;
                    }
                    case InstructionType.PointerEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var equals = lhs.Pointer == rhs.Pointer;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.IntegerEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var equals = lhs.Long == rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.FloatEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var equals = instruction.Value1.Type.Size == 4 ? lhs.Float == rhs.Float : lhs.Double == rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.PointerNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var notEquals = lhs.Pointer != rhs.Pointer;
                        registers[instruction.ValueIndex] = new Register {Bool = notEquals};
                        break;
                    }
                    case InstructionType.IntegerNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var notEquals = lhs.Long != rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = notEquals};
                        break;
                    }
                    case InstructionType.FloatNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var equals = instruction.Value1.Type.Size == 4 ? lhs.Float != rhs.Float : lhs.Double != rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = equals};
                        break;
                    }
                    case InstructionType.IntegerGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var greaterThan = lhs.Long > rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThan};
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var greaterThan = lhs.ULong > rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThan};
                        break;
                    }
                    case InstructionType.FloatGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var greaterThan = instruction.Value1.Type.Size == 4 ? lhs.Float > rhs.Float : lhs.Double > rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThan};
                        break;
                    }
                    case InstructionType.IntegerGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var greaterThanOrEqual = lhs.Long >= rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThanOrEqual};
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var greaterThanOrEqual = lhs.ULong >= rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThanOrEqual};
                        break;
                    }
                    case InstructionType.FloatGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var greaterThanOrEqual = instruction.Value1.Type.Size == 4 ? lhs.Float >= rhs.Float : lhs.Double >= rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = greaterThanOrEqual};
                        break;
                    }
                    case InstructionType.IntegerLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var lessThan = lhs.Long < rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThan};
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var lessThan = lhs.ULong < rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThan};
                        break;
                    }
                    case InstructionType.FloatLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var lessThan = instruction.Value1.Type.Size == 4 ? lhs.Float < rhs.Float : lhs.Double < rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThan};
                        break;
                    }
                    case InstructionType.IntegerLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var lessThanOrEqual = lhs.Long <= rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThanOrEqual};
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var lessThanOrEqual = lhs.ULong <= rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThanOrEqual};
                        break;
                    }
                    case InstructionType.FloatLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var lessThanOrEqual = instruction.Value1.Type.Size == 4 ? lhs.Float <= rhs.Float : lhs.Double <= rhs.Double;
                        registers[instruction.ValueIndex] = new Register {Bool = lessThanOrEqual};
                        break;
                    }
                    case InstructionType.PointerAdd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var pointerType = (PrimitiveAst)instruction.Value1.Type;
                        var pointerAdd = lhs.Pointer + (rhs.Integer * (int)pointerType.PointerType.Size);
                        registers[instruction.ValueIndex] = new Register {Pointer = pointerAdd};
                        break;
                    }
                    case InstructionType.IntegerAdd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var add = lhs.Long + rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = add};
                        break;
                    }
                    case InstructionType.FloatAdd:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var pointerType = (PrimitiveAst)instruction.Value1.Type;
                        var pointerSubtract = lhs.Pointer - (rhs.Integer * (int)pointerType.PointerType.Size);
                        registers[instruction.ValueIndex] = new Register {Pointer = pointerSubtract};
                        break;
                    }
                    case InstructionType.IntegerSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var subtract = lhs.Long - rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = subtract};
                        break;
                    }
                    case InstructionType.FloatSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var register = new Register();
                        if (instruction.Value1.Type.Size == 4)
                        {
                            register.Float = lhs.Float - rhs.Float;
                        }
                        else
                        {
                            register.Double = lhs.Double - rhs.Double;
                        }
                        registers[instruction.ValueIndex] = register;
                        break;
                    }
                    case InstructionType.IntegerMultiply:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var multiply = lhs.Long * rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = multiply};
                        break;
                    }
                    case InstructionType.FloatMultiply:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var divide = lhs.Long / rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = divide};
                        break;
                    }
                    case InstructionType.UnsignedIntegerDivide:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var divide = lhs.ULong / rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = divide};
                        break;
                    }
                    case InstructionType.FloatDivide:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var modulus = lhs.Long % rhs.Long;
                        registers[instruction.ValueIndex] = new Register {Long = modulus};
                        break;
                    }
                    case InstructionType.UnsignedIntegerModulus:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var modulus = lhs.ULong % rhs.ULong;
                        registers[instruction.ValueIndex] = new Register {ULong = modulus};
                        break;
                    }
                    case InstructionType.FloatModulus:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var shift = lhs.ULong >> rhs.Integer;
                        registers[instruction.ValueIndex] = new Register {ULong = shift};
                        break;
                    }
                    case InstructionType.ShiftLeft:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                        var shift = lhs.ULong << rhs.Integer;
                        registers[instruction.ValueIndex] = new Register {ULong = shift};
                        break;
                    }
                    case InstructionType.RotateRight:
                    {
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
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
                        var lhs = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var rhs = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
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

        private Register GetValue(InstructionValue value, Register[] registers, IntPtr stackPointer, FunctionIR function, Register[] arguments)
        {
            switch (value.ValueType)
            {
                case InstructionValueType.Value:
                    return registers[value.ValueIndex];
                case InstructionValueType.Allocation:
                    if (value.Global)
                    {
                        var globalPointer = _globals[value.ValueIndex];
                        return new Register {Pointer = globalPointer};
                    }
                    var allocation = function.Allocations[value.ValueIndex];
                    var pointer = stackPointer + (int)allocation.Offset;
                    return new Register {Pointer = pointer};
                case InstructionValueType.Argument:
                    return arguments[value.ValueIndex];
                case InstructionValueType.Constant:
                    return GetConstant(value);
                case InstructionValueType.Null:
                    return new Register {Pointer = IntPtr.Zero};
                case InstructionValueType.TypeInfo:
                    var typeInfoPointer = TypeTable.TypeInfos[value.ValueIndex];
                    return new Register {Pointer = typeInfoPointer};
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
                    var sourceIntegerType = (PrimitiveAst)value.Type;
                    return GetIntegerValue(value.ConstantValue, sourceIntegerType);
                case TypeKind.Enum:
                    var enumType = (EnumAst)value.Type;
                    sourceIntegerType = enumType.BaseType;
                    return GetIntegerValue(value.ConstantValue, sourceIntegerType);
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
                    register.Pointer = Allocator.MakeString(value.ConstantString, value.UseRawString);
                    break;
            }
            return register;
        }

        private static Register GetIntegerValue(Constant value, PrimitiveAst integerType)
        {
            var register = new Register();

            if (integerType.Signed)
            {
                switch (integerType.Size)
                {
                    case 1:
                        register.SByte = (sbyte)value.Integer;
                        break;
                    case 2:
                        register.Short = (short)value.Integer;
                        break;
                    case 8:
                        register.Long = value.Integer;
                        break;
                    default:
                        register.Integer = (int)value.Integer;
                        break;
                }
            }
            else
            {
                switch (integerType.Size)
                {
                    case 1:
                        register.Byte = (byte)value.UnsignedInteger;
                        break;
                    case 2:
                        register.UShort = (ushort)value.UnsignedInteger;
                        break;
                    case 8:
                        register.ULong = value.UnsignedInteger;
                        break;
                    default:
                        register.UInteger = (uint)value.UnsignedInteger;
                        break;
                }
            }

            return register;
        }
    }
}
