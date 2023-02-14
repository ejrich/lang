using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ol;

[StructLayout(LayoutKind.Explicit, Size=Function.Size)]
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

[StructLayout(LayoutKind.Explicit, Size=MessageValue.Size)]
public struct MessageValue
{
    [FieldOffset(0)] public IntPtr Ast;
    [FieldOffset(0)] public String Name;

    public const int Size = 16;
}

[StructLayout(LayoutKind.Explicit, Size=CompilerMessage.Size)]
public struct CompilerMessage
{
    [FieldOffset(0)] public MessageType Type;
    [FieldOffset(8)] public MessageValue Value;

    public const int Size = 24;
}

public static class Messages
{
    private static bool _intercepting;

    public static List<IntPtr> Functions { get; } = new();

    public static void AddFunction(IFunction function)
    {
    }
}
