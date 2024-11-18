using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace ol;

public enum MessageType
{
    ReadyToBeTypeChecked = 0,
    TypeCheckSuccessful,
    TypeCheckFailed,
    IRGenerated,
    ReadyForCodeGeneration,
    CodeGenerationFailed,
    CodeGenerated,
    ExecutableLinked
}

[StructLayout(LayoutKind.Explicit, Size=Size)]
public struct MessageValue
{
    [FieldOffset(0)] public IntPtr Ast;
    [FieldOffset(0)] public String Name;

    public const int Size = 16;
}

[StructLayout(LayoutKind.Explicit, Size=Size)]
public struct CompilerMessage
{
    [FieldOffset(0)] public MessageType Type;
    [FieldOffset(8)] public MessageValue Value;
    [FieldOffset(24)] public bool WaitForNext;

    public const int Size = 32;
    public const int PublicSize = 24;
}

public static unsafe class Messages
{
    public enum AstType
    {
        None = 0,
        Function,
        OperatorOverload,
        Enum,
        Struct,
        Union,
        Interface,
        GlobalVariable
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct Function
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public FunctionFlags Flags;
        [FieldOffset(56)] public IntPtr ReturnType;
        [FieldOffset(64)] public ArrayStruct Arguments;
        [FieldOffset(80)] public ArrayStruct Attributes;
        [FieldOffset(96)] public IntPtr Source;

        public const int Size = 104;
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct Enum
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public IntPtr BaseType;
        [FieldOffset(56)] public ArrayStruct Values;
        [FieldOffset(72)] public ArrayStruct Attributes;
        [FieldOffset(88)] public IntPtr Source;

        public const int Size = 96;
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct Struct
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public ArrayStruct Fields;
        [FieldOffset(64)] public ArrayStruct Attributes;
        [FieldOffset(80)] public IntPtr Source;

        public const int Size = 88;
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct Union
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public ArrayStruct Fields;
        [FieldOffset(64)] public IntPtr Source;

        public const int Size = 72;
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct Interface
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public IntPtr ReturnType;
        [FieldOffset(56)] public ArrayStruct Arguments;
        [FieldOffset(72)] public IntPtr Source;

        public const int Size = 80;
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct GlobalVariable
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public IntPtr VariableType;
        [FieldOffset(56)] public IntPtr Source;

        public const int Size = 64;
    }

    private static readonly ConcurrentQueue<CompilerMessage> MessageQueue = new();
    private static readonly Semaphore MessageWaitMutex = new(0, int.MaxValue);
    private static readonly Semaphore MessageReceiveMutex = new(0, 1);

    private static bool _completed;
    public static bool Intercepting;

    public static void Submit(MessageType type)
    {
        var message = new CompilerMessage { Type = type };
        Submit(message);
    }

    public static void Submit(MessageType type, string name)
    {
        var message = new CompilerMessage { Type = type, Value = new() { Name = Allocator.MakeString(name) } };
        Submit(message);
    }

    public static void Submit(MessageType type, IFunction function)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        IntPtr pointer;
        Function functionMessage;
        if (function.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(function);
            functionMessage = new()
            {
                Type = function is FunctionAst ? AstType.Function : AstType.OperatorOverload,
                File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[function.FileIndex]), Line = function.Line, Column = function.Column,
                Name = Allocator.MakeString(function.Name), Source = GCHandle.ToIntPtr(handle)
            };

            function.MessagePointer = pointer = Allocator.Allocate(Function.Size);
        }
        else
        {
            pointer = function.MessagePointer;
            functionMessage = Marshal.PtrToStructure<Function>(pointer);
        }

        if (function is FunctionAst functionAst)
        {
            var typeInfoPointer = TypeTable.TypeInfos[functionAst.TypeIndex];
            if (typeInfoPointer != IntPtr.Zero)
            {
                var functionTypeInfo = Marshal.PtrToStructure<TypeTable.FunctionTypeInfo>(typeInfoPointer);
                functionMessage.Flags = functionAst.Flags & FunctionFlags.PublicFlagsMask;
                functionMessage.ReturnType = functionTypeInfo.ReturnType;
                functionMessage.Arguments = functionTypeInfo.Arguments;
                functionMessage.Attributes = functionTypeInfo.Attributes;
            }
        }
        else if (type == MessageType.TypeCheckSuccessful && function is OperatorOverloadAst)
        {
            functionMessage.Flags = function.Flags & FunctionFlags.PublicFlagsMask;
            functionMessage.ReturnType = TypeTable.TypeInfos[function.ReturnType.TypeIndex];
            functionMessage.Arguments = TypeTable.MakeArguments(function.Arguments);
        }

        Marshal.StructureToPtr(functionMessage, pointer, false);
        Submit(type, pointer);
    }

    public static void Submit(MessageType type, EnumAst enumAst)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        IntPtr pointer;
        Enum enumMessage;
        if (enumAst.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(enumAst);
            enumMessage = new()
            {
                Type = AstType.Enum, File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[enumAst.FileIndex]),
                Line = enumAst.Line, Column = enumAst.Column, Name = Allocator.MakeString(enumAst.Name), Source = GCHandle.ToIntPtr(handle)
            };

            enumAst.MessagePointer = pointer = Allocator.Allocate(Enum.Size);
        }
        else
        {
            pointer = enumAst.MessagePointer;
            enumMessage = Marshal.PtrToStructure<Enum>(pointer);
        }

        var typeInfoPointer = TypeTable.TypeInfos[enumAst.TypeIndex];
        if (typeInfoPointer != IntPtr.Zero)
        {
            var enumTypeInfo = Marshal.PtrToStructure<TypeTable.EnumTypeInfo>(typeInfoPointer);
            enumMessage.BaseType = enumTypeInfo.BaseType;
            enumMessage.Values = enumTypeInfo.Values;
            enumMessage.Attributes = enumTypeInfo.Attributes;
        }

        Marshal.StructureToPtr(enumMessage, pointer, false);
        Submit(type, pointer);
    }

    public static void Submit(MessageType type, StructAst structAst)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        IntPtr pointer;
        Struct structMessage;
        if (structAst.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(structAst);
            structMessage = new()
            {
                Type = AstType.Struct, File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[structAst.FileIndex]),
                Line = structAst.Line, Column = structAst.Column, Name = Allocator.MakeString(structAst.Name), Source = GCHandle.ToIntPtr(handle)
            };

            structAst.MessagePointer = pointer = Allocator.Allocate(Struct.Size);
        }
        else
        {
            pointer = structAst.MessagePointer;
            structMessage = Marshal.PtrToStructure<Struct>(pointer);
        }

        var typeInfoPointer = TypeTable.TypeInfos[structAst.TypeIndex];
        if (typeInfoPointer != IntPtr.Zero)
        {
            var structTypeInfo = Marshal.PtrToStructure<TypeTable.StructTypeInfo>(typeInfoPointer);
            structMessage.Fields = structTypeInfo.Fields;
            structMessage.Attributes = structTypeInfo.Attributes;
        }

        Marshal.StructureToPtr(structMessage, pointer, false);
        Submit(type, pointer);
    }

    public static void Submit(MessageType type, UnionAst unionAst)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        IntPtr pointer;
        Union unionMessage;
        if (unionAst.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(unionAst);
            unionMessage = new()
            {
                Type = AstType.Union, File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[unionAst.FileIndex]),
                Line = unionAst.Line, Column = unionAst.Column, Name = Allocator.MakeString(unionAst.Name), Source = GCHandle.ToIntPtr(handle)
            };

            unionAst.MessagePointer = pointer = Allocator.Allocate(Union.Size);
        }
        else
        {
            pointer = unionAst.MessagePointer;
            unionMessage = Marshal.PtrToStructure<Union>(pointer);
        }

        var typeInfoPointer = TypeTable.TypeInfos[unionAst.TypeIndex];
        if (typeInfoPointer != IntPtr.Zero)
        {
            var unionTypeInfo = Marshal.PtrToStructure<TypeTable.UnionTypeInfo>(typeInfoPointer);
            unionMessage.Fields = unionTypeInfo.Fields;
        }

        Marshal.StructureToPtr(unionMessage, pointer, false);
        Submit(type, pointer);
    }

    public static void Submit(MessageType type, InterfaceAst interfaceAst)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        IntPtr pointer;
        Interface interfaceMessage;
        if (interfaceAst.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(interfaceAst);
            interfaceMessage = new()
            {
                Type = AstType.Interface, File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[interfaceAst.FileIndex]),
                Line = interfaceAst.Line, Column = interfaceAst.Column, Name = Allocator.MakeString(interfaceAst.Name), Source = GCHandle.ToIntPtr(handle)
            };

            interfaceAst.MessagePointer = pointer = Allocator.Allocate(Interface.Size);
        }
        else
        {
            pointer = interfaceAst.MessagePointer;
            interfaceMessage = Marshal.PtrToStructure<Interface>(pointer);
        }

        var typeInfoPointer = TypeTable.TypeInfos[interfaceAst.TypeIndex];
        if (typeInfoPointer != IntPtr.Zero)
        {
            var interfaceTypeInfo = Marshal.PtrToStructure<TypeTable.InterfaceTypeInfo>(typeInfoPointer);
            interfaceMessage.ReturnType = interfaceTypeInfo.ReturnType;
            interfaceMessage.Arguments = interfaceTypeInfo.Arguments;
        }

        Marshal.StructureToPtr(interfaceMessage, pointer, false);
        Submit(type, pointer);
    }

    public static void Submit(MessageType type, DeclarationAst variable)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        IntPtr pointer;
        GlobalVariable globalMessage;
        if (variable.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(variable);
            globalMessage = new()
            {
                Type = AstType.GlobalVariable, File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[variable.FileIndex]),
                Line = variable.Line, Column = variable.Column, Name = Allocator.MakeString(variable.Name), Source = GCHandle.ToIntPtr(handle)
            };

            variable.MessagePointer = pointer = Allocator.Allocate(GlobalVariable.Size);
        }
        else
        {
            pointer = variable.MessagePointer;
            globalMessage = Marshal.PtrToStructure<GlobalVariable>(pointer);
        }

        if (variable.Type != null)
        {
            globalMessage.VariableType = TypeTable.TypeInfos[variable.Type.TypeIndex];
        }

        Marshal.StructureToPtr(globalMessage, pointer, false);
        Submit(type, pointer);
    }

    private static void Submit(MessageType type, IntPtr ast)
    {
        var message = new CompilerMessage { Type = type, Value = new() { Ast = ast } };
        Submit(message);
    }

    private static void Submit(CompilerMessage message)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        message.WaitForNext = Intercepting && ThreadPool.RunThreadId != Environment.CurrentManagedThreadId;

        MessageQueue.Enqueue(message);
        MessageWaitMutex.Release();

        if (message.WaitForNext)
        {
            MessageReceiveMutex.WaitOne();
        }
    }

    public static void CompleteAndWait()
    {
        Completed();
        while (Intercepting);
    }

    public static void Completed()
    {
        MessageWaitMutex.Release();
        _completed = true;
    }

    private static bool _waitingForNext;
    public static bool GetNextMessage(IntPtr messagePointer)
    {
        if (_waitingForNext)
        {
            try {
                MessageReceiveMutex.Release();
            }
            catch (SemaphoreFullException e)
            {
                // @Robustness Fix this error
                ErrorReporter.Report("Internal compiler error running program");
                #if DEBUG
                Console.WriteLine(e);
                #endif
                Environment.Exit(ErrorCodes.InternalError);
            }
        }

        MessageWaitMutex.WaitOne();

        if (!MessageQueue.TryDequeue(out var message))
        {
            return false;
        }

        Buffer.MemoryCopy(&message, messagePointer.ToPointer(), CompilerMessage.PublicSize, CompilerMessage.PublicSize);
        _waitingForNext = message.WaitForNext;

        return true;
    }
}
