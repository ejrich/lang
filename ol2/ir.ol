entry_point: FunctionIR*;
functions: HashTable<int, FunctionIR*>;

constants: Array<InstructionValue>;
global_variables: Array<GlobalVariable>;
global_variables_size: u32;

struct FunctionIR {
    stack_size: u32;
    value_count: int;
    pointer_offset: int;
    save_stack: bool;
    source: Function*;
    compound_return_allocation: InstructionValue;
    constants: Array<InstructionValue>;
    allocations: Array<Allocation>;
    pointers: Array<InstructionValue>;
    instructions: Array<Instruction>;
    basic_blocks: Array<BasicBlock>;
    function_pointer: void*;
}

struct GlobalVariable {
    name: string;
    file_index: int;
    line: u32;
    size: u32;
    array: bool;
    array_length: u32;
    type: TypeAst*;
    initial_value: InstructionValue;
    pointer: InstructionValue;
}

struct Allocation {
    index: int;
    offset: u32;
    size: u32;
    array: bool;
    array_length: u32;
    type: TypeAst*;
}

struct BasicBlock {
    index: int;
    location: int;
}

struct Instruction {
    type: InstructionType;
    value_index: int;
    source: Ast*;
    scope: Scope*;

    // Used for Call, GetPointer, GetStructPointer, and debug locations
    index: int;
    index2: int;
    flag: bool;
    string: string;
    load_type: TypeAst*;

    value1: InstructionValue;
    value2: InstructionValue;
}

struct InstructionValue {
    value_type: InstructionValueType;

    value_index: int;
    type: TypeAst*;
    global: bool;

    // For constant values
    constant_value: Constant;
    constant_string: string;
    use_raw_string: bool;

    // For calls and constant structs/arrays
    values: Array<InstructionValue>;
    array_length: u32;

    // For Jumps
    jump_block: BasicBlock;
}

union Constant {
    boolean: bool;
    integer: s64;
    unsigned_integer: u64;
    double: float64;
}

enum InstructionType {
    None;
    Jump;
    ConditionalJump;
    Return;
    ReturnVoid;
    Load;
    LoadPointer;
    Store;
    GetPointer;
    GetStructPointer;
    GetUnionPointer;
    Call;
    CallFunctionPointer;
    SystemCall;
    InlineAssembly;
    IntegerExtend;
    UnsignedIntegerToIntegerExtend;
    UnsignedIntegerExtend;
    IntegerToUnsignedIntegerExtend;
    IntegerTruncate;
    UnsignedIntegerToIntegerTruncate;
    UnsignedIntegerTruncate;
    IntegerToUnsignedIntegerTruncate;
    IntegerToFloatCast;
    UnsignedIntegerToFloatCast;
    FloatCast;
    FloatToIntegerCast;
    FloatToUnsignedIntegerCast;
    PointerCast;
    PointerToIntegerCast;
    IntegerToPointerCast;
    IntegerToEnumCast;
    AllocateArray;
    IsNull;
    IsNotNull;
    Not;
    IntegerNegate;
    FloatNegate;
    And;
    Or;
    BitwiseAnd;
    BitwiseOr;
    Xor;
    PointerEquals;
    IntegerEquals;
    FloatEquals;
    PointerNotEquals;
    IntegerNotEquals;
    FloatNotEquals;
    IntegerGreaterThan;
    UnsignedIntegerGreaterThan;
    FloatGreaterThan;
    IntegerGreaterThanOrEqual;
    UnsignedIntegerGreaterThanOrEqual;
    FloatGreaterThanOrEqual;
    IntegerLessThan;
    UnsignedIntegerLessThan;
    FloatLessThan;
    IntegerLessThanOrEqual;
    UnsignedIntegerLessThanOrEqual;
    FloatLessThanOrEqual;
    PointerAdd;
    IntegerAdd;
    FloatAdd;
    PointerSubtract;
    IntegerSubtract;
    FloatSubtract;
    IntegerMultiply;
    FloatMultiply;
    IntegerDivide;
    UnsignedIntegerDivide;
    FloatDivide;
    IntegerModulus;
    UnsignedIntegerModulus;
    ShiftRight;
    ShiftLeft;
    RotateRight;
    RotateLeft;
    DebugSetLocation;
    DebugDeclareParameter;
    DebugDeclareVariable;
}

enum InstructionValueType {
    Value;
    Allocation;
    Argument;
    Constant;
    Null;
    Type;
    TypeInfo;
    Function;
    BasicBlock;
    CallArguments;
    ConstantStruct;
    ConstantArray;
    FileName;
}
