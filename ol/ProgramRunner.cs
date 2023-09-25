using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace ol;

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

public static unsafe class ProgramRunner
{
    private static readonly ModuleBuilder ModuleBuilder;

    private static readonly Dictionary<string, IntPtr> LibraryPointers = new();
    private static readonly Dictionary<string, Dictionary<string, IntPtr>> LibraryFunctionPointers = new();
    private static readonly List<IntPtr> Globals = new();

    private static int _typeCount;
    private static int _typeTableIndex;
    private static IntPtr _typeTablePointer;

    private static int _assemblyDataLength;
    private static IntPtr _assemblyDataPointer;

    static ProgramRunner()
    {
        var assemblyName = new AssemblyName("Runner");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder = assemblyBuilder.DefineDynamicModule("Runner");
    }

    private static void UpdateTypeTable()
    {
        lock (TypeTable.TypeInfos)
        {
            if (_typeCount != TypeTable.Count && _typeTablePointer != IntPtr.Zero)
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

                var typeTableArray = new ArrayStruct {Length = TypeTable.Count, Data = typeTableArrayPointer};
                Marshal.StructureToPtr(typeTableArray, _typeTablePointer, false);
            }
        }
    }

    public static void AddGlobalVariable(GlobalVariable variable)
    {
        var pointer = Allocator.Allocate(variable.Size);
        var index = Globals.Count;
        Globals.Add(pointer);

        if (_typeTablePointer == IntPtr.Zero && variable.Name == "__type_table")
        {
            _typeTablePointer = pointer;
            _typeTableIndex = index;
        }
        else if (variable.InitialValue != null)
        {
            InitializeGlobalVariable(pointer, variable.InitialValue);
        }
    }

    private static void InitializeGlobalVariable(IntPtr pointer, InstructionValue value)
    {
        switch (value.ValueType)
        {
            case InstructionValueType.Value:
                var globalPointer = Globals[value.ValueIndex];
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
            case InstructionValueType.ConstantArray:
                var size = (int)value.Type.Size;
                if (value.Values == null)
                {
                    var length = size * (int)value.ArrayLength;
                    ClearMemory(pointer, length);
                }
                else
                {
                    var i = 0;
                    for (; i < value.Values.Length; i++)
                    {
                        var arrayPointer = pointer + i * size;
                        InitializeGlobalVariable(arrayPointer, value.Values[i]);
                    }

                    if (value.ArrayLength > value.Values.Length)
                    {
                        var length = size * (int)(value.ArrayLength - i);
                        ClearMemory(pointer + i * size, length);
                    }
                }
                break;
            case InstructionValueType.ConstantUnion:
                ClearMemory(pointer, (int)value.Type.Size);
                break;
        }
    }

    public static void RunProgram(FunctionIR function, IAst source)
    {
        try
        {
            var returnRegister = ExecuteFunction(function);
        }
        catch (Exception e)
        {
            ErrorReporter.Report("Internal compiler error running program", source);
            #if DEBUG
            Console.WriteLine(e);
            #endif
        }
    }

    public static bool ExecuteCondition(FunctionIR function, IAst source)
    {
        try
        {
            var returnRegister = ExecuteFunction(function);
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

    public static string ExecuteInsert(FunctionIR function, IAst source)
    {
        try
        {
            var returnRegister = ExecuteFunction(function);
            var code = Marshal.PtrToStructure<String>(returnRegister.Pointer);
            return Marshal.PtrToStringAnsi(code.Data, (int)code.Length);
        }
        catch (Exception e)
        {
            ErrorReporter.Report("Internal compiler error executing insert code", source);
            #if DEBUG
            Console.WriteLine(e);
            #endif
            return string.Empty;
        }
    }

    private static void SetLinker(byte linker)
    {
        BuildSettings.Linker = (LinkerType)linker;
    }

    private static void SetExecutableName(String name)
    {
        BuildSettings.Name = Marshal.PtrToStringAnsi(name.Data, (int)name.Length);
    }

    private static void SetOutputTypeTable(byte config)
    {
        BuildSettings.OutputTypeTable = (OutputTypeTableConfiguration)config;
    }

    private static void SetOutputDirectory(String directory, int fileIndex, uint line, uint column)
    {
        var directoryPath = Marshal.PtrToStringAnsi(directory.Data, (int)directory.Length);
        BuildSettings.OutputDirectory = Path.IsPathRooted(directoryPath) ? directoryPath : Path.Combine(BuildSettings.Path, directoryPath);

        if (!Directory.Exists(BuildSettings.OutputDirectory))
        {
            ErrorReporter.Report($"Directory '{directoryPath}' not found, unable to set as output directory", fileIndex, line, column);
        }
    }

    private static void AddLibraryDirectory(String directory, int fileIndex, uint line, uint column)
    {
        var directoryPath = Marshal.PtrToStringAnsi(directory.Data, (int)directory.Length);
        if (Path.IsPathRooted(directoryPath))
        {
            BuildSettings.LibraryDirectories.Add(directoryPath);
        }
        else
        {
            directoryPath = Path.Combine(BuildSettings.Path, directoryPath);
            BuildSettings.LibraryDirectories.Add(directoryPath);
        }

        if (!Directory.Exists(directoryPath))
        {
            ErrorReporter.Report($"Directory '{directoryPath}' not found, unable to set as library directory", fileIndex, line, column);
        }
    }

    private static void CopyToOutputDirectory(String file, int fileIndex, uint line, uint column)
    {
        var filePath = Marshal.PtrToStringAnsi(file.Data, (int)file.Length);
        FileInfo fileInfo;
        if (Path.IsPathRooted(filePath))
        {
            fileInfo = new(filePath);
        }
        else
        {
            var fullPath = Path.Combine(BuildSettings.Path, filePath);
            fileInfo = new(fullPath);
        }

        if (!fileInfo.Exists)
        {
            ErrorReporter.Report($"File '{filePath}' not found, unable to copy to output directory", fileIndex, line, column);
        }
        else
        {
            BuildSettings.FilesToCopy.Add(fileInfo);
        }
    }

    private static void SetOutputArchitecture(byte arch)
    {
        BuildSettings.OutputArchitecture = (OutputArchitecture)arch;
    }

    private static IntPtr GetFunction(String name)
    {
        var functionName = Marshal.PtrToStringAnsi(name.Data, (int)name.Length);
        if (!TypeChecker.GlobalScope.Functions.TryGetValue(functionName, out var functions)) return IntPtr.Zero;

        var functionDef = functions[0];
        return functionDef.MessagePointer;
    }

    private static void InsertCode(IntPtr functionPointer, String code, int fileIndex, uint line, uint column)
    {
        if (functionPointer == IntPtr.Zero)
        {
            ErrorReporter.Report("Attempted to insert code into function that does not exist", fileIndex, line, column);
            return;
        }

        var codeString = Marshal.PtrToStringAnsi(code.Data, (int)code.Length);
        var function = Marshal.PtrToStructure<Messages.Function>(functionPointer);

        var handle = GCHandle.FromIntPtr(function.Source);
        var functionDef = handle.Target as IFunction;

        if (functionDef?.Body != null)
        {
            var inserted = TypeChecker.InsertCode(codeString, functionDef, functionDef.Body, fileIndex, line, column);
            if (functionDef.Flags.HasFlag(FunctionFlags.Verified))
            {
                TypeChecker.VerifyScope(functionDef.Body, functionDef, endIndex: inserted);
                if (!ErrorReporter.Errors.Any())
                {
                    var functionIR = Program.Functions[functionDef.FunctionIndex];
                    functionIR.Writing = 0;
                    functionIR.Written = false;
                    if (functionDef is FunctionAst functionAst)
                    {
                        ProgramIRBuilder.QueueBuildFunction(functionAst);
                    }
                    if (functionDef is OperatorOverloadAst overload)
                    {
                        ProgramIRBuilder.QueueBuildOperatorOverload(overload);
                    }
                }
            }
        }
        else
        {
            ErrorReporter.Report($"Cannot insert code into function '{functionDef.Name}' without a body'", fileIndex, line, column);
        }
    }

    private static void AddCode(String code, int fileIndex, uint line, uint column)
    {
        var codeString = Marshal.PtrToStringAnsi(code.Data, (int)code.Length);
        TypeChecker.AddCode(codeString, fileIndex, line, column);
    }

    private static void SetGlobalVariableValue(IntPtr variablePointer, String code, int fileIndex, uint line, uint column)
    {
        if (variablePointer == IntPtr.Zero)
        {
            ErrorReporter.Report("Attempted to set value of global variable that does not exist", fileIndex, line, column);
            return;
        }

        var codeString = Marshal.PtrToStringAnsi(code.Data, (int)code.Length);
        var globalVariable = Marshal.PtrToStructure<Messages.GlobalVariable>(variablePointer);

        var handle = GCHandle.FromIntPtr(globalVariable.Source);
        var variable = handle.Target as DeclarationAst;

        if (variable != null)
        {
            TypeChecker.SetGlobalVariableValue(variable, codeString, fileIndex, line, column);
        }
    }

    public static Register ExecuteFunction(FunctionIR function)
    {
        return ExecuteFunction(function, ReadOnlySpan<Register>.Empty);
    }

    private static Register ExecuteFunction(FunctionIR function, ReadOnlySpan<Register> arguments)
    {
        if (function.Source != null && !function.Written)
        {
            if (function.Writing == 1)
            {
                while (!function.Written);
            }
            else if (function.Source is FunctionAst functionAst)
            {
                if (!functionAst.Flags.HasFlag(FunctionFlags.Verified))
                {
                    TypeChecker.VerifyFunction(functionAst, false);
                }
                ProgramIRBuilder.BuildFunction(functionAst);
            }
            else if (function.Source is OperatorOverloadAst overload)
            {
                if (!overload.Flags.HasFlag(FunctionFlags.Verified))
                {
                    TypeChecker.VerifyOperatorOverload(overload, false);
                }
                ProgramIRBuilder.BuildOperatorOverload(overload);
            }
        }

        function.Executing = true;

        var instructionPointer = 0;
        Span<Register> registers = stackalloc Register[function.ValueCount];
        #if _LINUX
        Span<long> syscallArgs = stackalloc long[6];
        #endif

        var (stackPointer, stackCursor, stackBlock) = Allocator.StackAllocate((int)function.StackSize);
        var additionalBlocks = new List<(MemoryBlock block, int cursor)>();
        int fileIndex = 0; uint line = 0, column = 0;

        while (true)
        {
            Debug.Assert(instructionPointer < function.Instructions.Count);
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
                    var value = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    stackBlock.Cursor = stackCursor;
                    foreach (var (block, cursor) in additionalBlocks)
                    {
                        if (block.Cursor > cursor)
                        {
                            block.Cursor = cursor;
                        }
                    }
                    function.Executing = false;
                    return value;
                }
                case InstructionType.ReturnVoid:
                {
                    stackBlock.Cursor = stackCursor;
                    foreach (var (block, cursor) in additionalBlocks)
                    {
                        if (block.Cursor > cursor)
                        {
                            block.Cursor = cursor;
                        }
                    }
                    function.Executing = false;
                    return new Register();
                }
                case InstructionType.Load:
                {
                    var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    var register = new Register();
                    switch (instruction.LoadType.TypeKind)
                    {
                        case TypeKind.Boolean:
                            var rawValue = Marshal.PtrToStructure<byte>(pointer.Pointer);
                            register.Bool = Convert.ToBoolean(rawValue);
                            break;
                        case TypeKind.Integer:
                        case TypeKind.Enum:
                            switch (instruction.LoadType.Size)
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
                            if (instruction.LoadType.Size == 4)
                            {
                                register.Float = Marshal.PtrToStructure<float>(pointer.Pointer);
                            }
                            else
                            {
                                register.Double = Marshal.PtrToStructure<double>(pointer.Pointer);
                            }
                            break;
                        case TypeKind.Pointer:
                        case TypeKind.Interface:
                            register.Pointer = Marshal.ReadIntPtr(pointer.Pointer);
                            break;
                        case TypeKind.String:
                        case TypeKind.Array:
                        case TypeKind.CArray:
                        case TypeKind.Struct:
                        case TypeKind.Union:
                        case TypeKind.Any:
                        case TypeKind.Compound:
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
                        case TypeKind.Interface:
                        case TypeKind.Function:
                            Marshal.StructureToPtr(value.Pointer, pointer.Pointer, false);
                            break;
                        case TypeKind.String:
                        case TypeKind.Array:
                        case TypeKind.CArray:
                        case TypeKind.Struct:
                        case TypeKind.Union:
                        case TypeKind.Any:
                        case TypeKind.Compound:
                            var copyBytes = instruction.Value2.Type.Size;
                            Buffer.MemoryCopy(value.Pointer.ToPointer(), pointer.Pointer.ToPointer(), copyBytes, copyBytes);
                            break;
                    }
                    break;
                }
                case InstructionType.InitializeUnion:
                {
                    var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    ClearMemory(pointer.Pointer, instruction.Int);
                    break;
                }
                case InstructionType.GetPointer:
                {
                    var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    var index = GetValue(instruction.Value2, registers, stackPointer, function, arguments);
                    var indexedPointer = pointer.Pointer + instruction.Int * index.Integer;
                    registers[instruction.ValueIndex] = new Register {Pointer = indexedPointer};
                    break;
                }
                case InstructionType.GetStructPointer:
                {
                    var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    var structPointer = pointer.Pointer + instruction.Int;
                    registers[instruction.ValueIndex] = new Register {Pointer = structPointer};
                    break;
                }
                case InstructionType.Call:
                {
                    var callingFunction = Program.Functions[instruction.Index];
                    registers[instruction.ValueIndex] = MakeCall(callingFunction, instruction.Value1.Values, registers, stackPointer, function, arguments, instruction.Int, fileIndex, line, column);
                    break;
                }
                case InstructionType.CallFunctionPointer:
                {
                    var functionPointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);

                    var interfaceAst = (InterfaceAst)instruction.Value1.Type;
                    registers[instruction.ValueIndex] = MakeCallToFunctionPointer(functionPointer.Pointer, interfaceAst.ReturnType, instruction.Value2.Values, registers, stackPointer, function, arguments);
                    break;
                }
                #if _LINUX
                case InstructionType.SystemCall:
                {
                    syscallArgs.Fill(0);
                    for (var i = 0; i < instruction.Value1.Values.Length; i++)
                    {
                        var value = GetValue(instruction.Value1.Values[i], registers, stackPointer, function, arguments);
                        syscallArgs[i] = value.Long;
                    }

                    var returnValue = syscall(instruction.Index, syscallArgs[0], syscallArgs[1], syscallArgs[2], syscallArgs[3], syscallArgs[4], syscallArgs[5]);
                    registers[instruction.ValueIndex] = new Register {Long = returnValue};
                    break;
                }
                #endif
                case InstructionType.InlineAssembly:
                {
                    var assembly = (AssemblyAst)instruction.Source;

                    var estimatedBytes = assembly.InRegisters.Count * 10 + assembly.Instructions.Count * 3 + assembly.OutValues.Count * 10;
                    var assemblyCode = new List<byte>(estimatedBytes);
                    var mov = Assembly.Instructions["mov"][0];

                    // Declare the inputs and write the assembly instructions
                    RegisterDefinition stagingRegister = null;
                    if (assembly.FindStagingInputRegister)
                    {
                        foreach (var (register, definition) in Assembly.Registers)
                        {
                            if (definition.Type != RegisterType.General)
                            {
                                break;
                            }
                            if (!assembly.InRegisters.ContainsKey(register))
                            {
                                stagingRegister = definition;
                                break;
                            }
                        }
                        Debug.Assert(stagingRegister != null, "Unable to set staging register for capturing inputs");
                    }

                    var i = 0;
                    foreach (var (_, input) in assembly.InRegisters)
                    {
                        var inputValue = instruction.Value1.Values[i++];
                        var value = GetValue(inputValue, registers, stackPointer, function, arguments);
                        if (input.RegisterDefinition.Type == RegisterType.General)
                        {
                            WriteAssemblyInstruction(mov, input.RegisterDefinition, null, assemblyCode, null, value.ULong);
                        }
                        else
                        {
                            WriteAssemblyInstruction(mov, stagingRegister, null, assemblyCode, null, value.ULong);
                            WriteAssemblyInstruction(Assembly.Instructions["movq"][0], input.RegisterDefinition, stagingRegister, assemblyCode);
                        }
                    }

                    if (assembly.AssemblyBytes == null)
                    {
                        var bodyAssembly = new List<byte>(assembly.Instructions.Count * 3);
                        foreach (var instr in assembly.Instructions)
                        {
                            WriteAssemblyInstruction(instr.Definition, instr.Value1?.RegisterDefinition, instr.Value2?.RegisterDefinition, bodyAssembly, instr.Value1?.Constant?.Value.UnsignedInteger, instr.Value2?.Constant?.Value.UnsignedInteger);
                        }
                        assembly.AssemblyBytes = bodyAssembly.ToArray();
                    }
                    assemblyCode.AddRange(assembly.AssemblyBytes);

                    // Capture the output registers if necessary
                    if (assembly.OutValues.Count > 0)
                    {
                        stagingRegister = null;
                        foreach (var (_, definition) in Assembly.Registers)
                        {
                            if (definition.Type != RegisterType.General)
                            {
                                break;
                            }
                            var inOutputs = false;
                            foreach (var output in assembly.OutValues)
                            {
                                if (output.RegisterDefinition == definition)
                                {
                                    inOutputs = true;
                                    break;
                                }
                            }
                            if (!inOutputs)
                            {
                                stagingRegister = definition;
                                break;
                            }
                        }
                        Debug.Assert(stagingRegister != null, "Unable to set staging register for capturing outputs");

                        i = 0;
                        var movPointer = Assembly.Instructions["mov"][1];
                        foreach (var output in assembly.OutValues)
                        {
                            var outputValue = instruction.Value2.Values[i++];
                            var value = GetValue(outputValue, registers, stackPointer, function, arguments);

                            WriteAssemblyInstruction(mov, stagingRegister, null, assemblyCode, null, value.ULong);
                            if (outputValue.Type.TypeKind == TypeKind.Float)
                            {
                                if (outputValue.Type.Size == 4)
                                {
                                    WriteAssemblyInstruction(Assembly.Instructions["movss"][0], stagingRegister, output.RegisterDefinition, assemblyCode);
                                }
                                else
                                {
                                    WriteAssemblyInstruction(Assembly.Instructions["movsd"][0], stagingRegister, output.RegisterDefinition, assemblyCode);
                                }
                            }
                            else
                            {
                                WriteAssemblyInstruction(movPointer, stagingRegister, output.RegisterDefinition, assemblyCode);
                            }
                        }
                    }

                    // Add ret instruction
                    assemblyCode.Add(0xC3);

                    var assemblyBytes = assemblyCode.ToArray();

                    if (assemblyBytes.Length > _assemblyDataLength)
                    {
                        if (_assemblyDataPointer != IntPtr.Zero)
                        {
                            #if _LINUX
                            munmap(_assemblyDataPointer, _assemblyDataLength);
                            #elif _WINDOWS
                            VirtualFree(_assemblyDataPointer, 0, 0x8000);
                            #endif
                        }
                        #if _LINUX
                        _assemblyDataPointer = mmap(IntPtr.Zero, assemblyBytes.Length, 0x7, 0x4022, 0, 0);
                        #elif _WINDOWS
                        _assemblyDataPointer = VirtualAlloc(IntPtr.Zero, assemblyBytes.Length, 0x3000, 0x40);
                        #endif
                        _assemblyDataLength = assemblyBytes.Length;
                    }

                    Marshal.Copy(assemblyBytes, 0, _assemblyDataPointer, assemblyBytes.Length);

                    var inlineAssembly = Marshal.GetDelegateForFunctionPointer<VoidMethod>(_assemblyDataPointer);
                    inlineAssembly();
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
                                _ => (sbyte)value.Integer
                            };
                            break;
                        case 2:
                            register.Short = instruction.Value1.Type.Size switch
                            {
                                1 => value.SByte,
                                2 => value.Short,
                                4 => (short)value.Integer,
                                8 => (short)value.Long,
                                _ => (short)value.Integer
                            };
                            break;
                        case 4:
                            register.Integer = instruction.Value1.Type.Size switch
                            {
                                1 => value.SByte,
                                2 => value.Short,
                                8 => (int)value.Long,
                                _ => value.Integer
                            };
                            break;
                        case 8:
                            register.Long = instruction.Value1.Type.Size switch
                            {
                                1 => value.SByte,
                                2 => value.Short,
                                4 => value.Integer,
                                8 => value.Long,
                                _ => value.Integer
                            };
                            break;
                        default:
                            register.Integer = instruction.Value1.Type.Size switch
                            {
                                1 => value.SByte,
                                2 => value.Short,
                                8 => (int)value.Long,
                                _ => value.Integer
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
                                _ => (byte)value.Integer
                            };
                            break;
                        case 2:
                            register.UShort = instruction.Value1.Type.Size switch
                            {
                                1 => (ushort)value.SByte,
                                2 => (ushort)value.Short,
                                4 => (ushort)value.Integer,
                                8 => (ushort)value.Long,
                                _ => (ushort)value.Integer
                            };
                            break;
                        case 4:
                            register.UInteger = instruction.Value1.Type.Size switch
                            {
                                1 => (uint)value.SByte,
                                2 => (uint)value.Short,
                                8 => (uint)value.Long,
                                _ => (uint)value.Integer
                            };
                            break;
                        case 8:
                            register.ULong = instruction.Value1.Type.Size switch
                            {
                                1 => (ulong)value.SByte,
                                2 => (ulong)value.Short,
                                4 => (ulong)value.Integer,
                                8 => (ulong)value.Long,
                                _ => (ulong)value.Integer
                            };
                            break;
                        default:
                            register.UInteger = instruction.Value1.Type.Size switch
                            {
                                1 => (uint)value.SByte,
                                2 => (uint)value.Short,
                                8 => (uint)value.Long,
                                _ => (uint)value.Integer
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
                            1 => value.SByte,
                            2 => value.Short,
                            4 => value.Integer,
                            8 => value.Long,
                            _ => value.Integer
                        };
                    }
                    else
                    {
                        register.Double = instruction.Value1.Type.Size switch
                        {
                            1 => value.SByte,
                            2 => value.Short,
                            4 => value.Integer,
                            8 => value.Long,
                            _ => value.Integer
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
                        register.Float = value.ULong;
                    }
                    else
                    {
                        register.Double = value.ULong;
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
                        register.Double = value.Float;
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
                case InstructionType.GetUnionPointer:
                case InstructionType.PointerCast:
                case InstructionType.PointerToIntegerCast:
                case InstructionType.IntegerToPointerCast:
                case InstructionType.IntegerToEnumCast:
                {
                    // These instructions are for LLVM, so this is a no-op
                    registers[instruction.ValueIndex] = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    break;
                }
                case InstructionType.AllocateArray:
                {
                    var pointer = GetValue(instruction.Value1, registers, stackPointer, function, arguments);
                    var length = GetValue(instruction.Value2, registers, stackPointer, function, arguments);

                    var size = instruction.LoadType.Size * length.Long;
                    var (arrayPointer, arrayCursor, arrayBlock) = Allocator.StackAllocate((int)size);
                    Marshal.StructureToPtr(arrayPointer, pointer.Pointer, false);

                    if (arrayBlock != stackBlock)
                    {
                        var add = true;
                        foreach (var (block, _) in additionalBlocks)
                        {
                            if (arrayBlock == block)
                            {
                                add = false;
                                break;
                            }
                        }
                        if (add)
                        {
                            additionalBlocks.Add((arrayBlock, arrayCursor));
                        }
                    }
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
                    var greaterThan = instruction.Value1.Type.Size switch {
                        1 => lhs.SByte > rhs.SByte,
                        2 => lhs.Short > rhs.Short,
                        4 => lhs.Integer > rhs.Integer,
                        _ => lhs.Long > rhs.Long,
                    };
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
                    var greaterThanOrEqual = instruction.Value1.Type.Size switch {
                        1 => lhs.SByte >= rhs.SByte,
                        2 => lhs.Short >= rhs.Short,
                        4 => lhs.Integer >= rhs.Integer,
                        _ => lhs.Long >= rhs.Long,
                    };
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
                    var lessThan = instruction.Value1.Type.Size switch {
                        1 => lhs.SByte < rhs.SByte,
                        2 => lhs.Short < rhs.Short,
                        4 => lhs.Integer < rhs.Integer,
                        _ => lhs.Long < rhs.Long,
                    };
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
                    var lessThanOrEqual = instruction.Value1.Type.Size switch {
                        1 => lhs.SByte <= rhs.SByte,
                        2 => lhs.Short <= rhs.Short,
                        4 => lhs.Integer <= rhs.Integer,
                        _ => lhs.Long <= rhs.Long,
                    };
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
                    var pointerType = (PointerType)instruction.Value1.Type;
                    var pointerAdd = lhs.Pointer + (rhs.Integer * (int)pointerType.PointedType.Size);
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
                    var pointerType = (PointerType)instruction.Value1.Type;
                    var pointerSubtract = lhs.Pointer - (rhs.Integer * (int)pointerType.PointedType.Size);
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
                case InstructionType.DebugSetLocation:
                {
                    var source = instruction.Source;
                    fileIndex = source.FileIndex;
                    line = source.Line;
                    column = source.Column;
                    break;
                }
            }
        }
    }

    private static Register GetValue(InstructionValue value, ReadOnlySpan<Register> registers, IntPtr stackPointer, FunctionIR function, ReadOnlySpan<Register> arguments)
    {
        switch (value.ValueType)
        {
            case InstructionValueType.Value:
                return registers[value.ValueIndex];
            case InstructionValueType.Allocation:
                if (value.Global)
                {
                    if (value.ValueIndex == _typeTableIndex)
                    {
                        UpdateTypeTable();
                    }
                    var globalPointer = Globals[value.ValueIndex];
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
            case InstructionValueType.Function:
                var functionDef = Program.Functions[value.ValueIndex];
                return new Register {Pointer = CreateFunctionPointer(functionDef)};
            case InstructionValueType.FileName:
                return new Register {Pointer = BuildSettings.FileNames[value.ValueIndex]};
        }

        return new Register();
    }

    private static Register GetConstant(InstructionValue value)
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
            default:
                register.Pointer = Allocator.MakeString(value.ConstantString, value.UseRawString);
                break;
        }
        return register;
    }

    private static Register MakeCall(FunctionIR callingFunction, InstructionValue[] arguments, ReadOnlySpan<Register> registers, IntPtr stackPointer, FunctionIR function, ReadOnlySpan<Register> functionArgs, int externIndex, int fileIndex, uint line, uint column)
    {
        if (callingFunction.Source.Flags.HasFlag(FunctionFlags.Extern))
        {
            var functionAst = (FunctionAst)callingFunction.Source;
            var functionPointer = GetExternFunctionPointer(functionAst);
            return MakeCallToFunctionPointer(functionPointer, functionAst.ReturnType, arguments, registers, stackPointer, function, functionArgs);
        }

        if (callingFunction.Source.Flags.HasFlag(FunctionFlags.Compiler))
        {
            var returnValue = new Register();
            switch (callingFunction.Source.Name)
            {
                case "set_linker":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    SetLinker(value.Byte);
                    break;
                }
                case "set_executable_name":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var name = Marshal.PtrToStructure<String>(value.Pointer);
                    SetExecutableName(name);
                    break;
                }
                case "set_output_type_table":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    SetOutputTypeTable(value.Byte);
                    break;
                }
                case "set_output_directory":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var directory = Marshal.PtrToStructure<String>(value.Pointer);
                    SetOutputDirectory(directory, fileIndex, line, column);
                    break;
                }
                case "add_library_directory":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var directory = Marshal.PtrToStructure<String>(value.Pointer);
                    AddLibraryDirectory(directory, fileIndex, line, column);
                    break;
                }
                case "copy_to_output_directory":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var file = Marshal.PtrToStructure<String>(value.Pointer);
                    CopyToOutputDirectory(file, fileIndex, line, column);
                    break;
                }
                case "set_output_architecture":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    SetOutputArchitecture(value.Byte);
                    break;
                }
                case "add_source_file":
                {
                    if (Messages.Intercepting)
                    {
                        ErrorReporter.Report("Must call add_source_file before intercept_compiler_messages", fileIndex, line, column);
                    }
                    else
                    {
                        var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                        var file = Marshal.PtrToStructure<String>(value.Pointer);
                        var fileString = Marshal.PtrToStringAnsi(file.Data, (int)file.Length);
                        if (Parser.AddFile(fileString, fileIndex, line, column))
                        {
                            ThreadPool.CompleteWork(ThreadPool.ParseQueue);
                            TypeChecker.ClearDirectiveQueue();
                        }
                    }
                    break;
                }
                case "get_compiler_working_directory":
                {
                    returnValue.Pointer = Allocator.MakeString(BuildSettings.Path, false);
                    break;
                }
                case "report_error":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var error = Marshal.PtrToStructure<String>(value.Pointer);
                    var errorString = Marshal.PtrToStringAnsi(error.Data, (int)error.Length);
                    ErrorReporter.Report(errorString, fileIndex, line, column);
                    break;
                }
                case "intercept_compiler_messages":
                {
                    if (Messages.Intercepting)
                    {
                        returnValue.Bool = false;
                    }
                    else
                    {
                        Messages.Intercepting = returnValue.Bool = true;
                        ThreadPool.ReleaseRunExecuting();
                    }
                    break;
                }
                case "get_next_compiler_message":
                {
                    if (Messages.Intercepting)
                    {
                        var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                        returnValue.Bool = Messages.GetNextMessage(value.Pointer);
                    }
                    else
                    {
                        ErrorReporter.Report("Must call intercept_compiler_messages successfully before get_next_compiler_message", fileIndex, line, column);
                    }
                    break;
                }
                case "get_function":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var name = Marshal.PtrToStructure<String>(value.Pointer);
                    returnValue.Pointer = GetFunction(name);
                    break;
                }
                case "insert_code":
                {
                    var functionPointer = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var value = GetValue(arguments[1], registers, stackPointer, function, functionArgs);
                    var code = Marshal.PtrToStructure<String>(value.Pointer);
                    InsertCode(functionPointer.Pointer, code, fileIndex, line, column);
                    break;
                }
                case "add_code":
                {
                    var value = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var code = Marshal.PtrToStructure<String>(value.Pointer);
                    AddCode(code, fileIndex, line, column);
                    break;
                }
                case "set_global_variable_value":
                {
                    var variablePointer = GetValue(arguments[0], registers, stackPointer, function, functionArgs);
                    var codeValue = GetValue(arguments[1], registers, stackPointer, function, functionArgs);
                    var code = Marshal.PtrToStructure<String>(codeValue.Pointer);
                    SetGlobalVariableValue(variablePointer.Pointer, code, fileIndex, line, column);
                    break;
                }
                default:
                    ErrorReporter.Report($"Undefined compiler function '{callingFunction.Source.Name}'", callingFunction.Source);
                    break;
            }
            return returnValue;
        }

        {
            Span<Register> args = stackalloc Register[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                args[i] = GetValue(arguments[i], registers, stackPointer, function, functionArgs);
            }

            return ExecuteFunction(callingFunction, args);
        }
    }

    private static Register MakeCallToFunctionPointer(IntPtr functionPointer, IType returnedType, InstructionValue[] arguments, ReadOnlySpan<Register> registers, IntPtr stackPointer, FunctionIR function, ReadOnlySpan<Register> functionArgs)
    {
        var returnType = GetType(returnedType);
        var dynamicMethod = new DynamicMethod("FunctionPtrCall", returnType, Array.Empty<Type>());
        var ilGenerator = dynamicMethod.GetILGenerator();

        var argumentTypes = new Type[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var value = GetValue(argument, registers, stackPointer, function, functionArgs);

            switch (argument.Type.TypeKind)
            {
                case TypeKind.Boolean:
                    argumentTypes[i] = typeof(bool);
                    ilGenerator.Emit(value.Bool ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    break;
                case TypeKind.Integer:
                case TypeKind.Enum:
                case TypeKind.Type:
                    switch (argument.Type.Size)
                    {
                        case 1:
                            argumentTypes[i] = typeof(byte);
                            ilGenerator.Emit(OpCodes.Ldc_I4, value.Byte);
                            break;
                        case 2:
                            argumentTypes[i] = typeof(ushort);
                            ilGenerator.Emit(OpCodes.Ldc_I4, value.Short);
                            break;
                        case 8:
                            argumentTypes[i] = typeof(ulong);
                            ilGenerator.Emit(OpCodes.Ldc_I8, value.Long);
                            break;
                        default:
                            argumentTypes[i] = typeof(uint);
                            ilGenerator.Emit(OpCodes.Ldc_I4, value.Integer);
                            break;
                    }
                    break;
                case TypeKind.Float:
                    if (argument.Type.Size == 4)
                    {
                        argumentTypes[i] = typeof(float);
                        ilGenerator.Emit(OpCodes.Ldc_R4, value.Float);
                    }
                    else
                    {
                        argumentTypes[i] = typeof(double);
                        ilGenerator.Emit(OpCodes.Ldc_R8, value.Double);
                    }
                    break;
                default:
                    argumentTypes[i] = typeof(ulong);
                    ilGenerator.Emit(OpCodes.Ldc_I8, value.Long);
                    break;
            }
        }

        ilGenerator.Emit(OpCodes.Ldc_I8, functionPointer.ToInt64());
        ilGenerator.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, returnType, argumentTypes);
        ilGenerator.Emit(OpCodes.Ret);

        var returnRegister = new Register();
        switch (returnedType.TypeKind)
        {
            case TypeKind.Void:
            {
                var func = dynamicMethod.CreateDelegate<VoidMethod>();
                func();
                break;
            }
            case TypeKind.Boolean:
            {
                var func = dynamicMethod.CreateDelegate<BoolMethod>();
                returnRegister.Bool = func();
                break;
            }
            case TypeKind.Integer:
            case TypeKind.Enum:
            case TypeKind.Type:
                switch (returnedType.Size)
                {
                    case 1:
                    {
                        var func = dynamicMethod.CreateDelegate<ByteMethod>();
                        returnRegister.Byte = func();
                        break;
                    }
                    case 2:
                    {
                        var func = dynamicMethod.CreateDelegate<ShortMethod>();
                        returnRegister.UShort = func();
                        break;
                    }
                    case 8:
                    {
                        var func = dynamicMethod.CreateDelegate<LongMethod>();
                        returnRegister.ULong = func();
                        break;
                    }
                    default:
                    {
                        var func = dynamicMethod.CreateDelegate<IntMethod>();
                        returnRegister.UInteger = func();
                        break;
                    }
                }
                break;
            case TypeKind.Float:
            {
                if (returnedType.Size == 4)
                {
                    var func = dynamicMethod.CreateDelegate<FloatMethod>();
                    returnRegister.Double = func();
                }
                else
                {
                    var func = dynamicMethod.CreateDelegate<DoubleMethod>();
                    returnRegister.Double = func();
                }
                break;
            }
            default:
            {
                var func = dynamicMethod.CreateDelegate<PtrMethod>();
                returnRegister.Pointer = func();
                break;
            }
        }

        return returnRegister;
    }

    private delegate void VoidMethod();
    private delegate bool BoolMethod();
    private delegate byte ByteMethod();
    private delegate ushort ShortMethod();
    private delegate uint IntMethod();
    private delegate ulong LongMethod();
    private delegate float FloatMethod();
    private delegate double DoubleMethod();
    private delegate IntPtr PtrMethod();

    private static void WriteAssemblyInstruction(InstructionDefinition definition, RegisterDefinition register1, RegisterDefinition register2, List<byte> code, ulong? value1 = null, ulong? value2 = null)
    {
        var codeIndex = code.Count;

        // Handle instructions with prefixes
        if (definition.Prefix != 0)
        {
            code.Add(definition.Prefix);
        }

        // Handle rex addressing
        var rex = definition.Rex;
        if (definition.RMFirst)
        {
            if (register1 != null && register1.Rex)
            {
                rex |= 0x41; // Set REX.B
            }
            if (register2 != null && register2.Rex)
            {
                rex |= 0x44; // Set REX.R
            }
        }
        else
        {
            if (register1 != null && register1.Rex)
            {
                rex |= 0x44; // Set REX.R
            }
            if (register2 != null && register2.Rex)
            {
                rex |= 0x41; // Set REX.B
            }
        }

        if (rex != 0)
        {
            code.Add(rex);
        }

        // Write 0x0F if the instruction requires
        if (definition.OF)
        {
            code.Add(0x0F);
        }

        // Determine the opcode(s)
        var opcode = definition.Opcode;
        if (definition.AddRegisterToOpcode)
        {
            opcode |= register1.Offset;
        }
        code.Add(opcode);
        if (definition.Opcode2 != 0)
        {
            code.Add(definition.Opcode2);
        }

        // Write the ModR/M byte
        if (!definition.AddRegisterToOpcode)
        {
            var modrm = definition.Mod;

            if (definition.HasExtension)
            {
                modrm |= definition.Extension;
                modrm |= register1.Offset;
                code.Add(modrm);
            }
            else if (register1 != null && register2 != null)
            {
                if (definition.RMFirst)
                {
                    modrm |= register1.Offset;
                    modrm |= (byte)(register2.Offset << 3);
                }
                else
                {
                    modrm |= (byte)(register1.Offset << 3);
                    modrm |= register2.Offset;
                }
                code.Add(modrm);
            }
        }

        // Write constants if necessary
        if (value1.HasValue)
        {
            WriteConstant(code, value1.Value);
        }
        else if (value2.HasValue)
        {
            WriteConstant(code, value2.Value);
        }

        /*#if DEBUG
        for (; codeIndex < code.Count; codeIndex++)
        {
            Console.Write($"{code[codeIndex]:x} ");
        }
        Console.Write("\n");
        #endif*/
    }

    private static void WriteConstant(List<byte> code, ulong value)
    {
        for (var x = 0; x < 8; x++)
        {
            var b = value & 0xFF;
            code.Add((byte)b);
            value >>= 8;
        }
    }

    private static void ClearMemory(IntPtr pointer, int length)
    {
        Span<byte> bytes = stackalloc byte[length];
        bytes.Fill(0);
        fixed (byte* bytePointer = &bytes[0])
        {
            Buffer.MemoryCopy(bytePointer, pointer.ToPointer(), length, length);
        }
    }

    private const string Libc = "c";

    #if _LINUX
    [DllImport(Libc)]
    private static extern long syscall(long number, long a, long b, long c, long d, long e, long f);

    [DllImport(Libc)]
    private static extern IntPtr mmap(IntPtr addr, long length, int prot, int flags, int fd, long offset);

    [DllImport(Libc)]
    private static extern int munmap(IntPtr addr, long length);

    #elif _WINDOWS
    private const string Kernel32 = "kernel32";

    [DllImport(Kernel32)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, long dwSize, int flAllocationType, int flProtect);

    [DllImport(Kernel32)]
    private static extern bool VirtualFree(IntPtr lpAddress, long dwSize, int dwFreeType);
    #endif

    private static Register ConvertToRegister(object value)
    {
        var register = new Register();
        switch (value)
        {
            case Register r:
                return r;
            case bool b:
                register.Bool = b;
                break;
            case byte b:
                register.Byte = b;
                break;
            case ushort s:
                register.UShort = s;
                break;
            case uint u:
                register.UInteger = u;
                break;
            case ulong l:
                register.ULong = l;
                break;
            case float f:
                register.Float = f;
                break;
            case double d:
                register.Double = d;
                break;
            case IntPtr ptr:
                register.Pointer = ptr;
                break;
        }
        return register;
    }

    private static IntPtr GetExternFunctionPointer(FunctionAst function)
    {
        var library = function.Library == null ? function.ExternLib : function.Library.FileName == null ?
        #if _LINUX
            $"{function.Library.AbsolutePath}.so" :
        #elif _WINDOWS
            $"{function.Library.AbsolutePath}.dll" :
        #endif
            function.Library.FileName;

        var libraryPointer = IntPtr.Zero;
        if (!LibraryFunctionPointers.TryGetValue(library, out var functionPointers))
        {
            if (!LibraryPointers.TryGetValue(library, out libraryPointer))
            {
                if (!LoadLibrary(library, function.Library, out libraryPointer))
                {
                    return IntPtr.Zero;
                }
                LibraryPointers[library] = libraryPointer;
            }
            functionPointers = LibraryFunctionPointers[library] = new();
        }

        if (functionPointers.TryGetValue(function.Name, out var functionPointer))
        {
            return functionPointer;
        }

        if (libraryPointer == IntPtr.Zero)
        {
            LibraryPointers.TryGetValue(library, out libraryPointer);
        }

        if (!NativeLibrary.TryGetExport(libraryPointer, function.Name, out functionPointer))
        {
            return IntPtr.Zero;
        }

        functionPointers[function.Name] = functionPointer;
        return functionPointer;
    }

    private static bool LoadLibrary(string library, Library lib, out IntPtr pointer)
    {
        if (lib == null || lib.FileName != null)
        {
             return NativeLibrary.TryLoad(library, typeof(ProgramRunner).Assembly, null, out pointer);
        }

        return NativeLibrary.TryLoad(library, out pointer);
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

    private static IntPtr CreateFunctionPointer(FunctionIR function)
    {
        if (function.FunctionPointer != IntPtr.Zero) return function.FunctionPointer;

        var argumentTypes = new Type[function.Source.Arguments.Count];
        if (function.Source.Flags.HasFlag(FunctionFlags.Extern))
        {
            var functionAst = (FunctionAst)function.Source;
            function.FunctionPointer = GetExternFunctionPointer(functionAst);
        }
        else
        {
            var parameters = new ParameterExpression[argumentTypes.Length];
            var arguments = new Expression[argumentTypes.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = argumentTypes[i] = GetType(function.Source.Arguments[i].Type);
                parameters[i] = Expression.Parameter(type);
                arguments[i] = Expression.Convert(parameters[i], typeof(object));
            }

            var functionArguments = Expression.NewArrayInit(typeof(object), arguments);
            var call = Expression.Call(_executeFunction, Expression.Constant(function), functionArguments);

            // Compile the expression to a delegate
            var delegateType = CreateDelegateType(function.Source.Name, argumentTypes, typeof(Register));
            var functionDelegate = Expression.Lambda(delegateType, call, parameters).Compile();
            GCHandle.Alloc(functionDelegate); // Prevent the pointer from being garbage collected
            function.FunctionPointer = Marshal.GetFunctionPointerForDelegate(functionDelegate);
        }

        return function.FunctionPointer;
    }

    private static Type GetType(IType type)
    {
        switch (type?.TypeKind)
        {
            case TypeKind.Void:
                return typeof(void);
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
                return type.Size == 4 ? typeof(float) : typeof(double);
            default:
                return typeof(IntPtr);
        }
    }

    private static Register ExecuteFunctionFromDelegate(FunctionIR function, object[] arguments)
    {
        Span<Register> args = stackalloc Register[arguments.Length];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = ConvertToRegister(arguments[i]);
        }

        return ExecuteFunction(function, args);
    }

    private static MethodInfo GetMethodInfo(Delegate d) => d.Method;
    private static readonly MethodInfo _executeFunction = GetMethodInfo(ExecuteFunctionFromDelegate);

    // Borrowed from https://source.dot.net/#System.Linq.Expressions/System/Linq/Expressions/Compiler/DelegateHelpers.cs,117 so I don't have to load the function with reflection
    private static Type CreateDelegateType(string name, Type[] argumentTypes, Type returnType)
    {
        var builder = ModuleBuilder.DefineType(name, DelegateTypeAttributes, typeof(MulticastDelegate));

        builder.DefineConstructor(CtorAttributes, CallingConventions.Standard, DelegateCtorSignature).SetImplementationFlags(ImplAttributes);
        builder.DefineMethod("Invoke", InvokeAttributes, returnType, argumentTypes).SetImplementationFlags(ImplAttributes);

        return builder.CreateTypeInfo();
    }

    private const TypeAttributes DelegateTypeAttributes = TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.AutoClass;
    private const MethodAttributes CtorAttributes = MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public;
    private const MethodImplAttributes ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed;
    private const MethodAttributes InvokeAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual;
    private static readonly Type[] DelegateCtorSignature = { typeof(object), typeof(IntPtr) };
}
