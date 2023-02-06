using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LLVMSharp.Interop;

namespace ol;

public static class Program
{
    public static FunctionIR EntryPoint { get; set; }
    public static List<FunctionIR> Functions { get; } = new();
    public static List<InstructionValue> Constants { get; } = new();
    public static uint GlobalVariablesSize { get; set; }
    public static List<GlobalVariable> GlobalVariables { get; } = new();
}

public class FunctionIR
{
    public bool Writing { get; set; }
    public bool Written { get; set; }
    public uint StackSize { get; set; }
    public int ValueCount { get; set; }
    public int PointerOffset { get; set; }
    public bool SaveStack { get; set; }
    public InstructionValue CompoundReturnAllocation { get; set; }
    public IFunction Source { get; set; }
    public InstructionValue[] Constants { get; set; }
    public List<Allocation> Allocations { get; set; }
    public List<InstructionValue> Pointers { get; set; }
    public List<Instruction> Instructions { get; set; }
    public List<BasicBlock> BasicBlocks { get; set; }
    public IntPtr FunctionPointer { get; set; }
}

public class GlobalVariable
{
    public string Name { get; set; }
    public int? FileIndex { get; set; }
    public uint Line { get; set; }
    public uint Size { get; set; }
    public bool Array { get; set; }
    public uint ArrayLength { get; set; }
    public IType Type { get; set; }
    public InstructionValue InitialValue { get; set; }
    public InstructionValue Pointer { get; set; }
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
    public int Location { get; set; }
}

public class Instruction
{
    public InstructionType Type { get; set; }
    public int ValueIndex { get; set; }
    public IAst Source { get; set; }
    public IScope Scope { get; set; }

    // Used for Call, GetPointer, GetStructPointer, and debug locations
    public int Index { get; set; }
    public int Index2 { get; set; }
    public bool Flag { get; set; }
    public string String { get; set; }
    public IType LoadType { get; set; }

    public InstructionValue Value1 { get; set; }
    public InstructionValue Value2 { get; set; }
}

public class InstructionValue
{
    public InstructionValueType ValueType { get; set; }

    public int ValueIndex { get; set; }
    public IType Type { get; set; }
    public bool Global { get; set; }

    // For constant values
    public Constant ConstantValue { get; set; }
    public ConstantString ConstantString { get; set; }
    public bool UseRawString { get; set; }

    // For calls and constant structs/arrays
    public InstructionValue[] Values { get; set; }
    public uint ArrayLength { get; set; }

    // For Jumps
    public BasicBlock JumpBlock { get; set; }
}

public class ConstantString
{
    public string Value { get; set; }
    public IntPtr RawPointer { get; set; }
    public IntPtr Pointer { get; set; }
    public LLVMValueRef LLVMValue { get; set; }
}

[StructLayout(LayoutKind.Explicit)]
public struct Constant
{
    [FieldOffset(0)] public bool Boolean;
    [FieldOffset(0)] public long Integer;
    [FieldOffset(0)] public ulong UnsignedInteger;
    [FieldOffset(0)] public double Double;
}

public enum InstructionType
{
    None = 0,
    Jump,
    ConditionalJump,
    Return,
    ReturnVoid,
    Load,
    LoadPointer,
    Store,
    GetPointer,
    GetStructPointer,
    GetUnionPointer,
    Call,
    CallFunctionPointer,
    SystemCall,
    InlineAssembly,
    IntegerExtend,
    UnsignedIntegerToIntegerExtend,
    UnsignedIntegerExtend,
    IntegerToUnsignedIntegerExtend,
    IntegerTruncate,
    UnsignedIntegerToIntegerTruncate,
    UnsignedIntegerTruncate,
    IntegerToUnsignedIntegerTruncate,
    IntegerToFloatCast,
    UnsignedIntegerToFloatCast,
    FloatCast,
    FloatToIntegerCast,
    FloatToUnsignedIntegerCast,
    PointerCast,
    PointerToIntegerCast,
    IntegerToPointerCast,
    IntegerToEnumCast,
    AllocateArray,
    IsNull,
    IsNotNull,
    Not,
    IntegerNegate,
    FloatNegate,
    And,
    Or,
    BitwiseAnd,
    BitwiseOr,
    Xor,
    PointerEquals,
    IntegerEquals,
    FloatEquals,
    PointerNotEquals,
    IntegerNotEquals,
    FloatNotEquals,
    IntegerGreaterThan,
    UnsignedIntegerGreaterThan,
    FloatGreaterThan,
    IntegerGreaterThanOrEqual,
    UnsignedIntegerGreaterThanOrEqual,
    FloatGreaterThanOrEqual,
    IntegerLessThan,
    UnsignedIntegerLessThan,
    FloatLessThan,
    IntegerLessThanOrEqual,
    UnsignedIntegerLessThanOrEqual,
    FloatLessThanOrEqual,
    PointerAdd,
    IntegerAdd,
    FloatAdd,
    PointerSubtract,
    IntegerSubtract,
    FloatSubtract,
    IntegerMultiply,
    FloatMultiply,
    IntegerDivide,
    UnsignedIntegerDivide,
    FloatDivide,
    IntegerModulus,
    UnsignedIntegerModulus,
    ShiftRight,
    ShiftLeft,
    RotateRight,
    RotateLeft,
    DebugSetLocation,
    DebugDeclareParameter,
    DebugDeclareVariable
}

public enum InstructionValueType
{
    Value,
    Allocation,
    Argument,
    Constant,
    Null,
    Type,
    TypeInfo,
    Function,
    BasicBlock,
    CallArguments,
    ConstantStruct,
    ConstantArray,
    FileName
}
