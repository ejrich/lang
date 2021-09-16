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
        public uint StackSize { get; set; }
        public List<Allocation> Allocations { get; set; }
        public List<BasicBlock> BasicBlocks { get; set; }
    }

    public class Allocation
    {
        public int Index { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public IType Type { get; set; }
    }

    public class BasicBlock
    {
        public int Index { get; set; }
        public List<Instruction> Instructions { get; } = new();
    }

    public class Instruction
    {
        // TODO Implement me
        public InstructionType Type { get; set; }

        // Used for Load and Store
        public int AllocationIndex { get; set; }

        public InstructionValue Value1 { get; set; }
        public InstructionValue Value2 { get; set; }
    }

    public class InstructionValue
    {
        public InstructionValueType Type { get; set; }

        public int ValueIndex { get; set; }
    }

    public enum InstructionType
    {
        None = 0,
        Jump,
        Return,
        Load,
        Store,
        Cast,
        Not,
        Negate,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulus,
        And,
        BitwiseAnd,
        Or,
        BitwiseOr,
        Xor,
        Equals,
        NotEquals,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        ShiftRight,
        ShiftLeft,
        RotateRight,
        RotateLeft
    }

    public enum InstructionValueType
    {
        None = 0,
        Argument,
        Constant,
        Value // TODO IDK?
    }
}
