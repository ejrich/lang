using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lang
{
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

        public static IntPtr Allocate(int size)
        {
            // TODO Implement block allocator
            return Marshal.AllocHGlobal(size);
            // TODO Track pointers to be freed
        }

        public static void Free()
        {
            foreach (var pointer in _openPointers)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public const int StringLength = 12;

        public static IntPtr GetString(string value, bool useRawString = false)
        {
            if (useRawString)
            {
                // TODO Track pointers to be freed
                return Marshal.StringToHGlobalAnsi(value);
            }

            var stringPointer = Allocate(StringLength);

            Marshal.StructureToPtr(value.Length, stringPointer, false);
            var s = Marshal.StringToHGlobalAnsi(value);
            Marshal.StructureToPtr(s, stringPointer + 4, false);

            return stringPointer;
        }
    }
}
