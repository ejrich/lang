using System.Collections.Generic;

namespace Lang
{
    public class ProgramIR
    {
        public FunctionIR EntryPoint { get; set; }
        public Dictionary<string, FunctionIR> Functions { get; } = new();
    }

    public class FunctionIR
    {
        public int StackSize { get; set; }
        public List<Allocation> Allocations { get; } = new();
        public List<BasicBlock> BasicBlocks { get; } = new();
    }

    public class Allocation
    {
        public int Index { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public TypeDefinition Type { get; set; }
    }

    public class BasicBlock
    {
        public int Index { get; set; }
        public List<Instruction> Instructions { get; } = new();
    }

    public class Instruction
    {
        // TODO Implement me
    }
}
