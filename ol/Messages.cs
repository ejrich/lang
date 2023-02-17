using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ol;

[StructLayout(LayoutKind.Explicit, Size=Size)]
public struct Function
{
    [FieldOffset(0)] public String File;
    [FieldOffset(16)] public uint Line;
    [FieldOffset(20)] public uint Column;
    [FieldOffset(24)] public String Name;
    [FieldOffset(40)] public IntPtr Source;

    public const int Size = 48;
}

public enum MessageType
{
    ReadyToBeTypeChecked = 0,
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
    private static readonly SafeLinkedList<CompilerMessage> _messages = new();
    private static readonly Semaphore _messageWaitMutex = new(0, int.MaxValue);
    private static readonly Semaphore _messageReceiveMutex = new(0, 1);

    public static bool _completed;
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

    private static void Submit(CompilerMessage message)
    {
        _messages.Add(message);

        if (Intercepting)
        {
            _messageWaitMutex.Release();
            _messageReceiveMutex.WaitOne();
        }
    }

    public static void CompleteAndWait()
    {
        Completed();
        while (Messages.Intercepting);
    }

    public static void Completed()
    {
        _messageWaitMutex.Release();
        _completed = true;
    }

    public static bool GetNextMessage(IntPtr messagePointer)
    {
        var head = _messages.Head;

        if (head == null)
        {
            if (_completed) return false;

            _messageWaitMutex.WaitOne();

            head = _messages.Head;
            if (_completed || head == null) return false;

            _messageReceiveMutex.Release();
        }
        else
        {
            Marshal.StructureToPtr(head.Data, messagePointer, false);
        }

        var message = head.Data;
        Marshal.StructureToPtr(message, messagePointer, false);

        Interlocked.CompareExchange(ref _messages.Head, head.Next, head);
        if (_messages.End == head)
        {
            Interlocked.CompareExchange(ref _messages.Head, head.Next, head);
        }

        return true;
    }
}
