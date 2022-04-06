using System;
using System.Runtime.InteropServices;

namespace ol;

public unsafe struct String
{
    public int Length;
    public char* Pointer;

    public String Substring(int start)
    {
        return new String {Length = Length - start, Pointer = Pointer + start};
    }

    public char this[int index] => Pointer[index];

    public static implicit operator string(String str) => new(str.Pointer, 0, str.Length);
    public static implicit operator String(string str) => str.AsSpan();
    public static implicit operator String(ReadOnlySpan<char> span)
    {
        fixed (char* ptr = &MemoryMarshal.GetReference(span))
        {
            return new String {Length = span.Length, Pointer = ptr};
        }
    }

    public override string ToString()
    {
        return this;
    }
}
