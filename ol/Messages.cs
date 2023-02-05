using System;
using System.Runtime.InteropServices;

namespace ol;

[StructLayout(LayoutKind.Explicit, Size=Function.Size)]
public struct Function
{
    [FieldOffset(0)] public String Name;
    [FieldOffset(16)] public IntPtr Source;

    public const int Size = 24;
}
