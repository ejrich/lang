using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ol;

public class MemoryBlock
{
    public IntPtr Pointer { get; set; }
    public int Cursor { get; set; }
    public int Size { get; set; }
}

public static class Allocator
{
    private const int BlockSize = 20000;

    private static readonly List<MemoryBlock> _memoryBlocks = new();
    private static readonly List<IntPtr> _openPointers = new();

    public static IntPtr Allocate(uint size)
    {
        // Allocate separate blocks if above the block size
        if (size > BlockSize)
        {
            var pointer = Marshal.AllocHGlobal((int)size);
            _openPointers.Add(pointer);
            return pointer;
        }

        // Search for a memory block with open space
        foreach (var block in _memoryBlocks)
        {
            if (size <= block.Size - block.Cursor)
            {
                var pointer = block.Pointer + block.Cursor;
                block.Cursor += (int)size;
                return pointer;
            }
        }

        // Allocate a new block if necessary
        var blockPointer = Marshal.AllocHGlobal(BlockSize);
        _openPointers.Add(blockPointer);

        var memoryBlock = new MemoryBlock {Pointer = blockPointer, Cursor = (int)size, Size = BlockSize};
        _memoryBlocks.Add(memoryBlock);

        return blockPointer;
    }

    public static IntPtr Allocate(int size)
    {
        Debug.Assert(size > 0, "Allocation size must be positive");
        return Allocate((uint)size);
    }

    public static void Free()
    {
        /* #if DEBUG
        Console.WriteLine($"{_memoryBlocks.Count} memory blocks, {_openPointers.Count} open pointers");
        #endif */

        foreach (var pointer in _openPointers)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public const int StringLength = 16;

    public static IntPtr MakeString(string value, bool useRawString)
    {
        var s = AllocateString(value);

        if (useRawString)
        {
            return s;
        }

        var stringPointer = Allocate(StringLength);
        var stringStruct = new String {Length = value.Length, Data = s};
        Marshal.StructureToPtr(stringStruct, stringPointer, false);

        return stringPointer;
    }

    public static String MakeString(string value)
    {
        var s = AllocateString(value);

        var stringStruct = new String {Length = value.Length, Data = s};
        return stringStruct;
    }

    public unsafe static IntPtr AllocateString(string value)
    {
        var pointer = Allocate(value.Length + 1);
        var bytePointer = (byte*)pointer;

        var stringBytes = Encoding.ASCII.GetBytes(value);
        fixed (byte* p = &stringBytes[0])
        {
            Buffer.MemoryCopy(p, bytePointer, value.Length, value.Length);
        }
        bytePointer[value.Length] = 0;

        return pointer;
    }
}

[StructLayout(LayoutKind.Explicit, Size=Allocator.StringLength)]
public struct String
{
    [FieldOffset(0)] public long Length;
    [FieldOffset(8)] public IntPtr Data;
}
