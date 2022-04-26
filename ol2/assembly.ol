register_definitions: HashTable<string, RegisterDefinition>;
// {
//     {"al",    new(size: 1)},
//     {"cl",    new(offset: 1, size: 1)},
//     {"dl",    new(offset: 2, size: 1)},
//     {"bl",    new(offset: 3, size: 1)},
//     {"spl",   new(offset: 4, size: 1)},
//     {"bpl",   new(offset: 5, size: 1)},
//     {"sil",   new(offset: 6, size: 1)},
//     {"dil",   new(offset: 7, size: 1)},
//     {"r8b",   new(rex: true, size: 1)},
//     {"r9b",   new(offset: 1, rex: true, size: 1)},
//     {"r10b",  new(offset: 2, rex: true, size: 1)},
//     {"r11b",  new(offset: 3, rex: true, size: 1)},
//     {"r12b",  new(offset: 4, rex: true, size: 1)},
//     {"r13b",  new(offset: 5, rex: true, size: 1)},
//     {"r14b",  new(offset: 6, rex: true, size: 1)},
//     {"r15b",  new(offset: 7, rex: true, size: 1)},
//     {"ax",    new(size: 2)},
//     {"cx",    new(offset: 1, size: 2)},
//     {"dx",    new(offset: 2, size: 2)},
//     {"bx",    new(offset: 3, size: 2)},
//     {"sp",    new(offset: 4, size: 2)},
//     {"bp",    new(offset: 5, size: 2)},
//     {"si",    new(offset: 6, size: 2)},
//     {"di",    new(offset: 7, size: 2)},
//     {"r8w",   new(rex: true, size: 2)},
//     {"r9w",   new(offset: 1, rex: true, size: 2)},
//     {"r10w",  new(offset: 2, rex: true, size: 2)},
//     {"r11w",  new(offset: 3, rex: true, size: 2)},
//     {"r12w",  new(offset: 4, rex: true, size: 2)},
//     {"r13w",  new(offset: 5, rex: true, size: 2)},
//     {"r14w",  new(offset: 6, rex: true, size: 2)},
//     {"r15w",  new(offset: 7, rex: true, size: 2)},
//     {"eax",   new(size: 4)},
//     {"ecx",   new(offset: 1, size: 4)},
//     {"edx",   new(offset: 2, size: 4)},
//     {"ebx",   new(offset: 3, size: 4)},
//     {"esp",   new(offset: 4, size: 4)},
//     {"ebp",   new(offset: 5, size: 4)},
//     {"esi",   new(offset: 6, size: 4)},
//     {"edi",   new(offset: 7, size: 4)},
//     {"r8d",   new(rex: true, size: 4)},
//     {"r9d",   new(offset: 1, rex: true, size: 4)},
//     {"r10d",  new(offset: 2, rex: true, size: 4)},
//     {"r11d",  new(offset: 3, rex: true, size: 4)},
//     {"r12d",  new(offset: 4, rex: true, size: 4)},
//     {"r13d",  new(offset: 5, rex: true, size: 4)},
//     {"r14d",  new(offset: 6, rex: true, size: 4)},
//     {"r15d",  new(offset: 7, rex: true, size: 4)},
//     {"rax",   new()},
//     {"rcx",   new(offset: 1)},
//     {"rdx",   new(offset: 2)},
//     {"rbx",   new(offset: 3)},
//     {"rsp",   new(offset: 4)},
//     {"rbp",   new(offset: 5)},
//     {"rsi",   new(offset: 6)},
//     {"rdi",   new(offset: 7)},
//     {"r8",    new(rex: true)},
//     {"r9",    new(offset: 1, rex: true)},
//     {"r10",   new(offset: 2, rex: true)},
//     {"r11",   new(offset: 3, rex: true)},
//     {"r12",   new(offset: 4, rex: true)},
//     {"r13",   new(offset: 5, rex: true)},
//     {"r14",   new(offset: 6, rex: true)},
//     {"r15",   new(offset: 7, rex: true)},
//     {"xmm0",  new(RegisterType.SSE)},
//     {"xmm1",  new(RegisterType.SSE, 1)},
//     {"xmm2",  new(RegisterType.SSE, 2)},
//     {"xmm3",  new(RegisterType.SSE, 3)},
//     {"xmm4",  new(RegisterType.SSE, 4)},
//     {"xmm5",  new(RegisterType.SSE, 5)},
//     {"xmm6",  new(RegisterType.SSE, 6)},
//     {"xmm7",  new(RegisterType.SSE, 7)},
//     {"xmm8",  new(RegisterType.SSE, 0, true)},
//     {"xmm9",  new(RegisterType.SSE, 1, true)},
//     {"xmm10", new(RegisterType.SSE, 2, true)},
//     {"xmm11", new(RegisterType.SSE, 3, true)},
//     {"xmm12", new(RegisterType.SSE, 4, true)},
//     {"xmm13", new(RegisterType.SSE, 5, true)},
//     {"xmm14", new(RegisterType.SSE, 6, true)},
//     {"xmm15", new(RegisterType.SSE, 7, true)},
//     {"ymm0",  new(RegisterType.AVX)},
//     {"ymm1",  new(RegisterType.AVX, 1)},
//     {"ymm2",  new(RegisterType.AVX, 2)},
//     {"ymm3",  new(RegisterType.AVX, 3)},
//     {"ymm4",  new(RegisterType.AVX, 4)},
//     {"ymm5",  new(RegisterType.AVX, 5)},
//     {"ymm6",  new(RegisterType.AVX, 6)},
//     {"ymm7",  new(RegisterType.AVX, 7)},
//     {"ymm8",  new(RegisterType.AVX, 0, true)},
//     {"ymm9",  new(RegisterType.AVX, 1, true)},
//     {"ymm10", new(RegisterType.AVX, 2, true)},
//     {"ymm11", new(RegisterType.AVX, 3, true)},
//     {"ymm12", new(RegisterType.AVX, 4, true)},
//     {"ymm13", new(RegisterType.AVX, 5, true)},
//     {"ymm14", new(RegisterType.AVX, 6, true)},
//     {"ymm15", new(RegisterType.AVX, 7, true)}
// };

instruction_definitions: HashTable<string, Array<InstructionDefinition>>;
// {
//     {"fcos",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFF} }},
//     {"fld",    new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Value1 = new(true)} }},
//     {"fld1",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xE8} }},
//     {"fptan",  new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xF2} }},
//     {"fsin",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFE} }},
//     {"fst",    new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Extension = 0x10, Value1 = new(true)} }},
//     {"fstp",   new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Extension = 0x18, Value1 = new(true)} }},
//     {"fyl2x",  new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xF1} }},
//     {"lock",   new InstructionDefinition[]{ new() {Opcode = 0xF0} }},
//     {"mov",    new InstructionDefinition[]{
//         // TODO Better implement REX translation
//         new() {Rex = 0x48, Opcode = 0xB8, AddRegisterToOpcode = true, RMFirst = true, Value1 = new(), Value2 = new(constant: true)},
//         new() {Rex = 0x48, Opcode = 0x89, RMFirst = true, Value1 = new(true), Value2 = new()}
//     }},
//     {"movsd",  new InstructionDefinition[]{
//         new() {Prefix = 0xF2, OF = true, Opcode = 0x11, RMFirst = true, Value1 = new(true), Value2 = new(type: RegisterType.SSE)},
//         new() {Prefix = 0xF2, OF = true, Opcode = 0x10, Value1 = new(type: RegisterType.SSE), Value2 = new(true)},
//         new() {Prefix = 0xF2, OF = true, Opcode = 0x10, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)}
//     }},
//     {"movss",  new InstructionDefinition[]{
//         new() {Prefix = 0xF3, OF = true, Opcode = 0x11, RMFirst = true, Value1 = new(true), Value2 = new(type: RegisterType.SSE)},
//         new() {Prefix = 0xF3, OF = true, Opcode = 0x10, Value1 = new(type: RegisterType.SSE), Value2 = new(true)},
//         new() {Prefix = 0xF3, OF = true, Opcode = 0x10, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)}
//     }},
//     {"movq",   new InstructionDefinition[]{ new() {Prefix = 0x66, Rex = 0x48, OF = true, Opcode = 0x6E, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new()} }},
//     {"sqrtsd", new InstructionDefinition[]{ new() {Prefix = 0xF2, OF = true, Opcode = 0x51, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)} }},
//     {"xadd",   new InstructionDefinition[]{
//         new() {OF = true, Opcode = 0xC0, AddressSpace = 1, RMFirst = true, Value1 = new(true), Value2 = new(size: 1)},
//         new() {Prefix = 0x66, OF = true, Opcode = 0xC1, AddressSpace = 2, RMFirst = true, Value1 = new(true), Value2 = new(size: 2)},
//         new() {OF = true, Opcode = 0xC1, AddressSpace = 4, RMFirst = true, Value1 = new(true), Value2 = new(size: 4)},
//         new() {Rex = 0x48, OF = true, Opcode = 0xC1, RMFirst = true, Value1 = new(true), Value2 = new()}
//     }},
//     {"cmpxchg", new InstructionDefinition[]{
//         new() {OF = true, Opcode = 0xB0, AddressSpace = 1, RMFirst = true, Value1 = new(true), Value2 = new(size: 1)},
//         new() {Prefix = 0x66, OF = true, Opcode = 0xB1, AddressSpace = 2, RMFirst = true, Value1 = new(true), Value2 = new(size: 2)},
//         new() {OF = true, Opcode = 0xB1, AddressSpace = 4, RMFirst = true, Value1 = new(true), Value2 = new(size: 4)},
//         new() {Rex = 0x48, OF = true, Opcode = 0xB1, RMFirst = true, Value1 = new(true), Value2 = new()}
//     }}
//     // TODO Add more
// };

enum RegisterType : u8 {
    General;
    SSE;
    AVX;
}

struct RegisterDefinition {
    type: RegisterType;
    offset: u8;
    rex: bool;
    size:u8 = 8;
}

[flags]
enum InstructionFlags : u8 {
    None                = 0x0;
    OF                  = 0x1;
    AddRegisterToOpcode = 0x2;
    HasExtension        = 0x4;
    RMFirst             = 0x8;
}

struct InstructionDefinition {
    prefix: u8;
    rex: u8;
    opcode: u8;
    opcode2: u8;
    extension: u8;
    mod: u8;
    address_space: u8 = 8;
    flags: InstructionFlags;
    value1: InstructionArgument;
    value2: InstructionArgument;
}

struct InstructionArgument {
    memory: bool;
    constant: bool;
    register_size: u8;
    type: RegisterType;
}
