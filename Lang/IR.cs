using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lang
{
    public class ProgramIR
    {
        public FunctionIR EntryPoint { get; set; }
        public Dictionary<string, FunctionIR> Functions { get; } = new();
        public Dictionary<string, InstructionValue> Constants { get; } = new();
        public List<GlobalVariable> GlobalVariables { get; } = new();
    }

    public class FunctionIR
    {
        public uint StackSize { get; set; }
        public List<Allocation> Allocations { get; set; }
        public List<BasicBlock> BasicBlocks { get; set; }
        public Dictionary<string, InstructionValue> Constants { get; set; }
    }

    public class GlobalVariable
    {
        public string Name { get; set; }
        public int Index { get; set; }
        public uint Size { get; set; }
        public IType Type { get; set; }
        // TODO Add initializers
    }

    public class Allocation
    {
        public int Index { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public bool Array { get; set; }
        public uint ArrayLength { get; set; }
        public IType Type { get; set; }
    }

    public class BasicBlock
    {
        public int Index { get; set; }
        public List<Instruction> Instructions { get; } = new();
    }

    public class Instruction
    {
        public InstructionType Type { get; set; }

        // Used for Load, Store, GetPointer, and GetStructPointer
        public int? Index { get; set; }
        public bool Global { get; set; }
        public bool GetFirstPointer { get; set; }
        public string CallFunction { get; set; }

        public InstructionValue Value1 { get; set; }
        public InstructionValue Value2 { get; set; }
    }

    public class InstructionValue
    {
        public InstructionValueType ValueType { get; set; }

        public int ValueIndex { get; set; }
        // public TypeDefinition TypeDefinition { get; set; } // TODO Remove
        public IType Type { get; set; }

        // For constant values
        public InstructionConstant ConstantValue { get; set; }
        public string ConstantString { get; set; }

        // For calls
        public InstructionValue[] Arguments { get; set; }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InstructionConstant
    {
        [FieldOffset(0)] public bool Boolean;
        [FieldOffset(0)] public long Integer;
        [FieldOffset(0)] public ulong UnsignedInteger;
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public double Double;
    }

    public enum InstructionType
    {
        None = 0,
        Jump,
        Return,
        Load,
        Store,
        GetPointer,
        GetStructPointer,
        Call,
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
        Value, // TODO IDK?
        Argument,
        Constant,
        Null,
        Type,
        CallArguments
    }
}
