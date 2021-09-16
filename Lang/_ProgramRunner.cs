using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

    [StructLayout(LayoutKind.Explicit)]
    public struct String
    {
        [FieldOffset(0)] public int Length;
        [FieldOffset(4)] public IntPtr Data;
    }

    public interface _IProgramRunner
    {
        void Init();
        void RunProgram(FunctionIR function, IAst source);
        bool ExecuteCondition(FunctionIR function, IAst source);
    }

    public unsafe class _ProgramRunner : _IProgramRunner
    {
        private ModuleBuilder _moduleBuilder;
        private int _version;
        private readonly Dictionary<string, List<int>> _functionIndices = new();
        private readonly List<(Type type, object libraryObject)> _functionLibraries = new();

        private int _typeCount;
        private IntPtr _typeTablePointer;
        private IntPtr[] _typeInfoPointers;

        private int _globalVariablesSize;
        private IntPtr[] _globals;

        public void Init()
        {
            // How this should work
            // - When a type/function is added to the TypeTable, add the TypeInfo object to the array
            // - When function IR is built and the function is extern, create the function ref
            // - When a global variable is added, store them in the global space


            // Initialize the runner
            if (_moduleBuilder == null)
            {
                var assemblyName = new AssemblyName("Runner");
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
                _moduleBuilder = assemblyBuilder.DefineDynamicModule("Runner");
            }

            TypeBuilder functionTypeBuilder = null;
            foreach (var functions in TypeTable.Functions.Values)
            {
                foreach (var function in functions)
                {
                    if (!function.Flags.HasFlag(FunctionFlags.Extern)) continue;

                    if (!_functionIndices.TryGetValue(function.Name, out var functionIndex))
                        _functionIndices[function.Name] = functionIndex = new List<int>();

                    if (function.Flags.HasFlag(FunctionFlags.Varargs))
                    {
                        for (var i = functionIndex.Count; i < function.VarargsCalls.Count; i++)
                        {
                            functionTypeBuilder ??= _moduleBuilder.DefineType($"Functions{_version}", TypeAttributes.Class | TypeAttributes.Public);
                            var callTypes = function.VarargsCalls[i];
                            var varargsTypes = new Type[callTypes.Count];
                            Array.Fill(varargsTypes, typeof(Register));
                            CreateFunction(functionTypeBuilder, function.Name, function.ExternLib, varargsTypes);
                            functionIndex.Add(_version);
                        }
                    }
                    else
                    {
                        if (!functionIndex.Any())
                        {
                            functionTypeBuilder ??= _moduleBuilder.DefineType($"Functions{_version}", TypeAttributes.Class | TypeAttributes.Public);
                            var args = function.Arguments.Select(_ => typeof(Register)).ToArray();
                            CreateFunction(functionTypeBuilder, function.Name, function.ExternLib, args);
                            functionIndex.Add(_version);
                        }
                    }
                }
            }

            if (functionTypeBuilder != null)
            {
                var library = functionTypeBuilder.CreateType();
                var functionObject = Activator.CreateInstance(library);
                _functionLibraries.Add((library, functionObject));
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

                var pointer = Marshal.AllocHGlobal((int)Program.GlobalVariablesSize - _globalVariablesSize);
                _globalVariablesSize = (int)Program.GlobalVariablesSize;

                for (; i < Program.GlobalVariables.Count; i++)
                {
                    var variable = Program.GlobalVariables[i];

                    if (_typeTablePointer == IntPtr.Zero && variable.Name == "__type_table")
                    {
                        _typeTablePointer = pointer;
                    }

                    // TODO Set initial value

                    _globals[i] = pointer;
                    pointer += (int)variable.Size;
                }
            }

            if (_typeCount != TypeTable.Count)
            {
                _typeCount = TypeTable.Count;

                if (_typeInfoPointers == null)
                {
                    _typeInfoPointers = new IntPtr[TypeTable.Count];
                }
                else
                {
                    var newTypeInfoPointers = new IntPtr[TypeTable.Count];
                    _typeInfoPointers.CopyTo(newTypeInfoPointers, 0);
                    _typeInfoPointers = newTypeInfoPointers;
                }

                Marshal.StructureToPtr(_typeCount, _typeTablePointer, false);
                // TODO Implement me
                // // Get required types and allocate the array
                // var typeInfoArrayType = _types["Array.*.TypeInfo"];
                // var typeInfoType = _types["TypeInfo"];
                // var typeInfoSize = Marshal.SizeOf(typeInfoType);

                // const int pointerSize = 8;
                // var typeTable = Activator.CreateInstance(typeInfoArrayType);
                // _typeDataPointer = InitializeConstArray(typeTable, typeInfoArrayType, pointerSize, _typeCount);

                // // Create TypeInfo pointers
                // var newTypeInfos = new List<(IType type, object typeInfo, IntPtr typeInfoPointer)>();
                // foreach (var (name, type) in TypeTable.Types)
                // {
                //     if (!_typeInfoPointers.TryGetValue(name, out var typeInfoPointer))
                //     {
                //         var typeInfo = Activator.CreateInstance(typeInfoType);

                //         var typeNameField = typeInfoType.GetField("name");
                //         typeNameField.SetValue(typeInfo, GetString(type.Name));
                //         var typeKindField = typeInfoType.GetField("type");
                //         typeKindField.SetValue(typeInfo, type.TypeKind);
                //         var typeSizeField = typeInfoType.GetField("size");
                //         typeSizeField.SetValue(typeInfo, type.Size);

                //         _typeInfoPointers[name] = typeInfoPointer = Marshal.AllocHGlobal(typeInfoSize);
                //         newTypeInfos.Add((type, typeInfo, typeInfoPointer));
                //     }

                //     var arrayPointer = IntPtr.Add(_typeDataPointer, type.TypeIndex * pointerSize);
                //     Marshal.StructureToPtr(typeInfoPointer, arrayPointer, false);
                // }

                // foreach (var (name, functions) in TypeTable.Functions)
                // {
                //     for (var i = 0; i < functions.Count; i++)
                //     {
                //         var function = functions[i];
                //         if (!_typeInfoPointers.TryGetValue($"{name}.{i}", out var typeInfoPointer))
                //         {
                //             var typeInfo = Activator.CreateInstance(typeInfoType);

                //             var typeNameField = typeInfoType.GetField("name");
                //             typeNameField.SetValue(typeInfo, GetString(function.Name));
                //             var typeKindField = typeInfoType.GetField("type");
                //             typeKindField.SetValue(typeInfo, function.TypeKind);

                //             _typeInfoPointers[$"{name}.{i}"] = typeInfoPointer = Marshal.AllocHGlobal(typeInfoSize);
                //             newTypeInfos.Add((function, typeInfo, typeInfoPointer));
                //         }

                //         var arrayPointer = IntPtr.Add(_typeDataPointer, function.TypeIndex * pointerSize);
                //         Marshal.StructureToPtr(typeInfoPointer, arrayPointer, false);
                //     }
                // }

                // // Set fields and enum values on TypeInfo objects
                // if (newTypeInfos.Any())
                // {
                //     var typeFieldArrayType = _types["Array.TypeField"];
                //     var typeFieldType = _types["TypeField"];
                //     var typeFieldSize = Marshal.SizeOf(typeFieldType);

                //     var enumValueArrayType = _types["Array.EnumValue"];
                //     var enumValueType = _types["EnumValue"];
                //     var enumValueSize = Marshal.SizeOf(enumValueType);

                //     var argumentArrayType = _types["Array.ArgumentType"];
                //     var argumentType = _types["ArgumentType"];
                //     var argumentSize = Marshal.SizeOf(argumentType);

                //     foreach (var (type, typeInfo, typeInfoPointer) in newTypeInfos)
                //     {
                //         switch (type)
                //         {
                //             case StructAst structAst:
                //                 var typeFieldArray = Activator.CreateInstance(typeFieldArrayType);
                //                 InitializeConstArray(typeFieldArray, typeFieldArrayType, typeFieldSize, structAst.Fields.Count);

                //                 var typeFieldsField = typeInfoType.GetField("fields");
                //                 typeFieldsField.SetValue(typeInfo, typeFieldArray);

                //                 var typeFieldArrayDataField = typeFieldArrayType.GetField("data");
                //                 var typeFieldsDataPointer = GetPointer(typeFieldArrayDataField.GetValue(typeFieldArray));

                //                 for (var i = 0; i < structAst.Fields.Count; i++)
                //                 {
                //                     var field = structAst.Fields[i];
                //                     var typeField = Activator.CreateInstance(typeFieldType);

                //                     var typeFieldName = typeFieldType.GetField("name");
                //                     typeFieldName.SetValue(typeField, GetString(field.Name));
                //                     var typeFieldOffset = typeFieldType.GetField("offset");
                //                     typeFieldOffset.SetValue(typeField, field.Offset);
                //                     var typeFieldInfo = typeFieldType.GetField("type_info");
                //                     var typePointer = _typeInfoPointers[field.TypeDefinition.GenericName];
                //                     typeFieldInfo.SetValue(typeField, typePointer);

                //                     var arrayPointer = IntPtr.Add(typeFieldsDataPointer, typeFieldSize * i);
                //                     Marshal.StructureToPtr(typeField, arrayPointer, false);
                //                 }
                //                 break;
                //             case EnumAst enumAst:
                //                 var enumValueArray = Activator.CreateInstance(enumValueArrayType);
                //                 InitializeConstArray(enumValueArray, enumValueArrayType, enumValueSize, enumAst.Values.Count);

                //                 var enumValuesField = typeInfoType.GetField("enum_values");
                //                 enumValuesField.SetValue(typeInfo, enumValueArray);

                //                 var enumValuesArrayDataField = enumValueArrayType.GetField("data");
                //                 var enumValuesDataPointer = GetPointer(enumValuesArrayDataField.GetValue(enumValueArray));

                //                 for (var i = 0; i < enumAst.Values.Count; i++)
                //                 {
                //                     var value = enumAst.Values[i];
                //                     var enumValue = Activator.CreateInstance(enumValueType);

                //                     var enumValueName = enumValueType.GetField("name");
                //                     enumValueName.SetValue(enumValue, GetString(value.Name));
                //                     var enumValueValue = enumValueType.GetField("value");
                //                     enumValueValue.SetValue(enumValue, value.Value);

                //                     var arrayPointer = IntPtr.Add(enumValuesDataPointer, enumValueSize * i);
                //                     Marshal.StructureToPtr(enumValue, arrayPointer, false);
                //                 }
                //                 break;
                //             case FunctionAst function:
                //                 var returnTypeField = typeInfoType.GetField("return_type");
                //                 returnTypeField.SetValue(typeInfo, _typeInfoPointers[function.ReturnTypeDefinition.GenericName]);

                //                 var argumentArray = Activator.CreateInstance(argumentArrayType);
                //                 var argumentCount = function.Flags.HasFlag(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
                //                 InitializeConstArray(argumentArray, argumentArrayType, argumentSize, argumentCount);

                //                 var argumentsField = typeInfoType.GetField("arguments");
                //                 argumentsField.SetValue(typeInfo, argumentArray);

                //                 var argumentArrayDataField = argumentArrayType.GetField("data");
                //                 var argumentArrayDataPointer = GetPointer(argumentArrayDataField.GetValue(argumentArray));

                //                 for (var i = 0; i < argumentCount; i++)
                //                 {
                //                     var argument = function.Arguments[i];
                //                     var argumentValue = Activator.CreateInstance(argumentType);

                //                     var argumentName = argumentType.GetField("name");
                //                     argumentName.SetValue(argumentValue, GetString(argument.Name));
                //                     var argumentTypeField = argumentType.GetField("type_info");

                //                     var argumentTypeInfoPointer = argument.TypeDefinition.TypeKind switch
                //                     {
                //                         TypeKind.Type => _typeInfoPointers["s32"],
                //                         TypeKind.Params => _typeInfoPointers[$"Array.{argument.TypeDefinition.Generics[0].GenericName}"],
                //                         _ => _typeInfoPointers[argument.TypeDefinition.GenericName]
                //                     };
                //                     argumentTypeField.SetValue(argumentValue, argumentTypeInfoPointer);

                //                     var arrayPointer = IntPtr.Add(argumentArrayDataPointer, argumentSize * i);
                //                     Marshal.StructureToPtr(argumentValue, arrayPointer, false);
                //                 }
                //                 break;
                //         }
                //         Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);
                //     }
                // }

                // Set the data pointer
                // fixed (IntPtr* pointer = &_typeInfoPointers[0])
                // {
                //     var dataPointer = _typeTablePointer + 4;
                //     Buffer.MemoryCopy(pointer, dataPointer.ToPointer(), 8, 8);
                // }
            }
        }

        private void CreateFunction(TypeBuilder typeBuilder, string name, string library, Type[] args)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(Register), args);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});
            method.SetCustomAttribute(caBuilder);
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
                                register.Bool = Marshal.PtrToStructure<bool>(pointer.Pointer);
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
                            case TypeKind.Struct:
                            case TypeKind.CArray:
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
                                Marshal.StructureToPtr(value.Bool, pointer.Pointer, false);
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
                            case TypeKind.Struct:
                                var copyBytes = instruction.Value2.Type.Size;
                                Buffer.MemoryCopy(value.Pointer.ToPointer(), pointer.Pointer.ToPointer(), copyBytes, copyBytes);
                                break;
                            case TypeKind.CArray:
                                // TODO How should this work?
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
                                args[i] = GetValue(instruction.Value1.Values[i], registers, stackPointer, function, arguments);
                            }

                            if (callingFunction.Source.Flags.HasFlag(FunctionFlags.Varargs))
                            {
                                var functionIndex = _functionIndices[instruction.String][instruction.Index];
                                var (type, functionObject) = _functionLibraries[functionIndex];
                                var argumentTypes = new Type[args.Length];
                                Array.Fill(argumentTypes, typeof(Register));
                                var functionDecl = type.GetMethod(instruction.String, argumentTypes!);
                                var returnValue = functionDecl.Invoke(functionObject, args);
                                registers[instruction.ValueIndex] = (Register)returnValue;
                            }
                            else
                            {
                                var functionIndex = _functionIndices[instruction.String][instruction.Index];
                                var (type, functionObject) = _functionLibraries[functionIndex];
                                var functionDecl = type.GetMethod(instruction.String);
                                var returnValue = functionDecl.Invoke(functionObject, args);
                                registers[instruction.ValueIndex] = (Register)returnValue;
                            }
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
                        // This instruction is mostly for LLVM, so this is pretty much a no-op
                        registers[instruction.ValueIndex] = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        break;
                    }
                    case InstructionType.AllocateArray:
                    {
                        var length = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                        var arrayPointer = Marshal.AllocHGlobal((int)instruction.Value2.Type.Size * length.Integer);
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
                        var pointerAdd = lhs.Pointer + rhs.Integer; // TODO Get the pointer offset
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
                        var pointerSubtract = lhs.Pointer - rhs.Integer; // TODO Get the pointer offset
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
                    sourceIntegerType = (PrimitiveAst)enumType.BaseType;
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
                    register.Pointer = GetString(value.ConstantString, value.UseRawString);
                    break;
            }
            return register;
        }

        private static Register GetIntegerValue(Constant value, PrimitiveAst integerType)
        {
            var register = new Register();

            if (integerType.Primitive.Signed)
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

        private static IntPtr GetString(string value, bool useRawString = false)
        {
            if (useRawString)
            {
                return Marshal.StringToHGlobalAnsi(value);
            }

            const int stringLength = 12;
            var stringPointer = Marshal.AllocHGlobal(stringLength);

            Marshal.StructureToPtr(value.Length, stringPointer, false);
            var s = Marshal.StringToHGlobalAnsi(value);
            Marshal.StructureToPtr(s, stringPointer + 4, false);

            return stringPointer;
        }
    }
}
