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

    public static bool Intercepting;
    public static bool Completed;

    private static readonly Semaphore _messageWaitMutex = new(0, 1);
    private static readonly Semaphore _messageReceiveMutex = new(0, 1);

    public static void Submit(MessageType type)
    {
        var message = new CompilerMessage { Type = type, Value = new() { Ast = IntPtr.Zero } };
        _messages.Add(message);

        if (Intercepting)
        {

        }
    }

    public static bool GetNextMessage(IntPtr messagePointer)
    {
        // Read the head
        // If null
        //   Completed, return false
        //   else, use the wait mutex to wait for the next message
        // If not null, return true and set the value from the pointer
        // Remove the head and set the next to the head

        var head = _messages.Head;

        if (head == null)
        {
            if (Completed) return false;

            // TODO
            return true;
        }
        else
        {
            Marshal.StructureToPtr(head.Data, messagePointer, false);
        }

        Interlocked.CompareExchange(ref _messages.Head, head.Next, head);
        if (_messages.End == head)
        {
            Interlocked.CompareExchange(ref _messages.Head, head.Next, head);
        }

        return true;
    }
}
