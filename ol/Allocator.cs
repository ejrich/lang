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
    private const int BlockSize = 50000;

    private static readonly List<MemoryBlock> _memoryBlocks = new();
    private static readonly List<MemoryBlock> _stackBlocks = new();
    private static readonly List<IntPtr> _openPointers = new();

    public static IntPtr Allocate(uint size)
    {
        if (size == 0) return IntPtr.Zero;
        return Allocate((int)size);
    }

    public static IntPtr Allocate(int size)
    {
        if (size == 0) return IntPtr.Zero;
        Debug.Assert(size > 0, "Allocation size must be positive");

        // Allocate separate blocks if above the block size
        if (size > BlockSize)
        {
            var pointer = Marshal.AllocHGlobal(size);
            _openPointers.Add(pointer);
            return pointer;
        }

        var (blockPointer, _, _) = FindMemoryBlock(size, _memoryBlocks);

        return blockPointer;
    }

    public static (IntPtr, int, MemoryBlock) StackAllocate(int size)
    {
        // Allocate separate blocks if above the block size
        if (size > BlockSize)
        {
            var pointer = Marshal.AllocHGlobal(size);
            _openPointers.Add(pointer);

            var block = new MemoryBlock {Pointer = pointer, Cursor = size, Size = size};
            _stackBlocks.Add(block);

            return (pointer, 0, block);
        }

        return FindMemoryBlock(size, _stackBlocks);
    }

    private static (IntPtr, int, MemoryBlock) FindMemoryBlock(int size, List<MemoryBlock> blocks)
    {
        // Search for a memory block with open space
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (size <= block.Size - block.Cursor)
            {
                var pointer = block.Pointer + block.Cursor;
                var cursor = block.Cursor;
                block.Cursor += size;
                return (pointer, cursor, block);
            }
        }

        // Allocate a new block if necessary
        var blockPointer = Marshal.AllocHGlobal(BlockSize);
        _openPointers.Add(blockPointer);

        var memoryBlock = new MemoryBlock {Pointer = blockPointer, Cursor = size, Size = BlockSize};
        blocks.Add(memoryBlock);

        return (blockPointer, 0, memoryBlock);
    }


    public static void Free()
    {
        /* #if DEBUG
        Console.WriteLine($"{_memoryBlocks.Count} memory blocks, {_stackBlocks.Count} stack blocks, {_openPointers.Count} open pointers");
        #endif */

        foreach (var pointer in _openPointers)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public const int StringLength = 16;

    public static IntPtr MakeString(ReadOnlySpan<char> value, bool useRawString)
    {
        var s = AllocateString(value);

        if (useRawString)
        {
            return s;
        }

        var stringPointer = Allocate(StringLength);
        var stringStruct = new LanguageString {Length = value.Length, Data = s};
        Marshal.StructureToPtr(stringStruct, stringPointer, false);

        return stringPointer;
    }

    public static unsafe IntPtr AllocateString(ReadOnlySpan<char> value)
    {
        var pointer = Allocate(value.Length + 1);
        var bytePointer = (byte*)pointer;

        if (value.Length > 0)
        {
            var stringBytes = Encoding.ASCII.GetBytes(value.ToArray());
            fixed (byte* p = &stringBytes[0])
            {
                Buffer.MemoryCopy(p, bytePointer, value.Length, value.Length);
            }
        }
        bytePointer[value.Length] = 0;

        return pointer;
    }
}

[StructLayout(LayoutKind.Explicit, Size=Allocator.StringLength)]
public struct LanguageString
{
    [FieldOffset(0)] public long Length;
    [FieldOffset(8)] public IntPtr Data;
}
