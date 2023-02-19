using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ol;

public class MemoryBlock
{
    public IntPtr Pointer;
    public int Cursor;
    public int Size;
}

public static class Allocator
{
    private const int BlockSize = 50000;

    private static readonly List<MemoryBlock> _memoryBlocks = new();
    private static readonly List<IntPtr> _openPointers = new();

    private static readonly List<int> _stackRequestingThreadIds = new();
    private static readonly List<List<MemoryBlock>> _stackBlocks = new();

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
        var threadId = Environment.CurrentManagedThreadId;
        for (var i = 0; i < _stackRequestingThreadIds.Count; i++)
        {
            if (_stackRequestingThreadIds[i] == threadId)
            {
                return StackAllocate(size, _stackBlocks[i]);
            }
        }

        var threadStackBlocks = new List<MemoryBlock>();
        _stackRequestingThreadIds.Add(threadId);
        _stackBlocks.Add(threadStackBlocks);

        return StackAllocate(size, threadStackBlocks);
    }

    private static (IntPtr, int, MemoryBlock) StackAllocate(int size, List<MemoryBlock> blocks)
    {
        // Allocate separate blocks if above the block size
        if (size > BlockSize)
        {
            var pointer = Marshal.AllocHGlobal(size);
            _openPointers.Add(pointer);

            var block = new MemoryBlock {Pointer = pointer, Cursor = size, Size = size};
            blocks.Add(block);

            return (pointer, 0, block);
        }

        return FindMemoryBlock(size, blocks);
    }

    private static (IntPtr, int, MemoryBlock) FindMemoryBlock(int size, List<MemoryBlock> blocks)
    {
        // Search for a memory block with open space
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            while (true)
            {
                var cursor = block.Cursor;
                if (size > block.Size - cursor) break;

                if (Interlocked.CompareExchange(ref block.Cursor, cursor + size, cursor) == cursor)
                {
                    var pointer = block.Pointer + cursor;
                    return (pointer, cursor, block);
                }
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

    public static IntPtr MakeString(ConstantString value, bool useRawString)
    {
        if (useRawString)
        {
            if (value.RawPointer != IntPtr.Zero)
            {
                return value.RawPointer;
            }
            return value.RawPointer = AllocateString(value.Value);
        }

        if (value.Pointer != IntPtr.Zero)
        {
            return value.Pointer;
        }

        if (value.RawPointer == IntPtr.Zero)
        {
            value.RawPointer = AllocateString(value.Value);
        }

        var stringPointer = Allocate(StringLength);
        var stringStruct = new String {Length = value.Value.Length, Data = value.RawPointer};
        Marshal.StructureToPtr(stringStruct, stringPointer, false);

        return value.Pointer = stringPointer;
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

        if (value != string.Empty)
        {
            var stringBytes = Encoding.ASCII.GetBytes(value);
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
public struct String
{
    [FieldOffset(0)] public long Length;
    [FieldOffset(8)] public IntPtr Data;
}
