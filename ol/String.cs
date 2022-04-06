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

    public bool Compare(string str)
    {
        if (Length != str.Length) return false;

        for (var i = 0; i < Length; i++)
        {
            if (Pointer[i] != str[i]) return false;
        }

        return true;
    }

    public char this[int index] => Pointer[index];

    public bool Equals(String other) => this == other;
    public override bool Equals(object obj) => obj is String other && Equals(other);
    public override int GetHashCode()// => ((string)this).GetHashCode();
    {
        var sum = 0;
        for (var i = 0; i < Length; i++)
        {
            sum += Pointer[i];
        }
        return sum;
    }

    public static bool operator ==(String a, String b)
    {
        if (a.Length != b.Length) return false;
        if (a.Pointer == b.Pointer) return true;

        for (var i = 0; i < a.Length; i++)
        {
            if (a.Pointer[i] != b.Pointer[i]) return false;
        }

        return true;
    }
    public static bool operator !=(String a, String b) => !(a == b);

    public static implicit operator string(String str) => new(str.Pointer, 0, str.Length);
    public static implicit operator LanguageString(String str) => new() {Length = str.Length, Data = (IntPtr)str.Pointer};
    public static implicit operator ReadOnlySpan<char>(String str) => new(str.Pointer, str.Length);
    public static implicit operator String(string str) => str.AsSpan();
    public static implicit operator String(ReadOnlySpan<char> span)
    {
        fixed (char* ptr = &MemoryMarshal.GetReference(span))
        {
            return new String {Length = span.Length, Pointer = ptr};
        }
    }

    public static implicit operator UIntPtr(String str) => (UIntPtr)str.Length;

    public override string ToString()
    {
        return this;
    }
}
