using System;
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

    public const int Size = 24;
}

public static class Messages
{
    public enum AstType
    {
        None = 0,
        Function,
        OperatorOverload,
        Enum,
        Struct,
        Union,
        Interface
    }

    [StructLayout(LayoutKind.Explicit, Size=Size)]
    public struct Function
    {
        [FieldOffset(0)] public AstType Type;
        [FieldOffset(8)] public String File;
        [FieldOffset(24)] public uint Line;
        [FieldOffset(28)] public uint Column;
        [FieldOffset(32)] public String Name;
        [FieldOffset(48)] public IntPtr ReturnType;
        [FieldOffset(56)] public ArrayStruct Arguments;
        [FieldOffset(72)] public ArrayStruct Attributes;
        [FieldOffset(88)] public IntPtr Source;

        public const int Size = 96;
        public const int SizeWithoutSource = 88;
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
        public const int SizeWithoutSource = 88;
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
        public const int SizeWithoutSource = 80;
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
        public const int SizeWithoutSource = 64;
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
        public const int SizeWithoutSource = 72;
    }

    private static readonly SafeLinkedList<CompilerMessage> MessageQueue = new();
    private static readonly Semaphore MessageWaitMutex = new(0, int.MaxValue);

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
        if (function.MessagePointer == IntPtr.Zero)
        {
            var handle = GCHandle.Alloc(function);
            var functionMessage = new Function
            {
                Type = function is FunctionAst ? AstType.Function : AstType.OperatorOverload,
                File = Marshal.PtrToStructure<String>(BuildSettings.FileNames[function.FileIndex]), Line = function.Line, Column = function.Column,
                Name = Allocator.MakeString(function.Name), Source = GCHandle.ToIntPtr(handle)
            };

            var pointer = Allocator.Allocate(Function.Size);
            Marshal.StructureToPtr(functionMessage, pointer, false);
            function.MessagePointer = pointer;
        }

        var message = new CompilerMessage { Type = type, Value = new() { Ast = function.MessagePointer } };
        Submit(message);
    }

    private static void Submit(CompilerMessage message)
    {
        if (ErrorReporter.Errors.Any() || _completed) return;

        MessageQueue.Add(message);
        MessageWaitMutex.Release();
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

    public static bool GetNextMessage(IntPtr messagePointer)
    {
        MessageWaitMutex.WaitOne();

        var head = MessageQueue.Head;
        if (head == null)
        {
            return false;
        }

        Marshal.StructureToPtr(head.Data, messagePointer, false);

        Interlocked.CompareExchange(ref MessageQueue.Head, head.Next, head);
        if (MessageQueue.End == head)
        {
            Interlocked.CompareExchange(ref MessageQueue.Head, head.Next, head);
        }

        return true;
    }
}
