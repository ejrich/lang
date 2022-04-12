using System.Collections.Generic;

namespace ol;

public static class Assembly
{
    public static readonly Dictionary<string, RegisterDefinition> Registers = new()
    {
        {"al",    new(size: 1)},
        {"cl",    new(offset: 1, size: 1)},
        {"dl",    new(offset: 2, size: 1)},
        {"bl",    new(offset: 3, size: 1)},
        {"spl",   new(offset: 4, size: 1)},
        {"bpl",   new(offset: 5, size: 1)},
        {"sil",   new(offset: 6, size: 1)},
        {"dil",   new(offset: 7, size: 1)},
        {"r8b",   new(rex: 0x41, size: 1)},
        {"r9b",   new(offset: 1, rex: 0x41, size: 1)},
        {"r10b",  new(offset: 2, rex: 0x41, size: 1)},
        {"r11b",  new(offset: 3, rex: 0x41, size: 1)},
        {"r12b",  new(offset: 4, rex: 0x41, size: 1)},
        {"r13b",  new(offset: 5, rex: 0x41, size: 1)},
        {"r14b",  new(offset: 6, rex: 0x41, size: 1)},
        {"r15b",  new(offset: 7, rex: 0x41, size: 1)},
        {"ax",    new(size: 2)},
        {"cx",    new(offset: 1, size: 2)},
        {"dx",    new(offset: 2, size: 2)},
        {"bx",    new(offset: 3, size: 2)},
        {"sp",    new(offset: 4, size: 2)},
        {"bp",    new(offset: 5, size: 2)},
        {"si",    new(offset: 6, size: 2)},
        {"di",    new(offset: 7, size: 2)},
        {"r8w",   new(rex: 0x41, size: 2)},
        {"r9w",   new(offset: 1, rex: 0x41, size: 2)},
        {"r10w",  new(offset: 2, rex: 0x41, size: 2)},
        {"r11w",  new(offset: 3, rex: 0x41, size: 2)},
        {"r12w",  new(offset: 4, rex: 0x41, size: 2)},
        {"r13w",  new(offset: 5, rex: 0x41, size: 2)},
        {"r14w",  new(offset: 6, rex: 0x41, size: 2)},
        {"r15w",  new(offset: 7, rex: 0x41, size: 2)},
        {"eax",   new(size: 4)},
        {"ecx",   new(offset: 1, size: 4)},
        {"edx",   new(offset: 2, size: 4)},
        {"ebx",   new(offset: 3, size: 4)},
        {"esp",   new(offset: 4, size: 4)},
        {"ebp",   new(offset: 5, size: 4)},
        {"esi",   new(offset: 6, size: 4)},
        {"edi",   new(offset: 7, size: 4)},
        {"r8d",   new(rex: 0x41, size: 4)},
        {"r9d",   new(offset: 1, rex: 0x41, size: 4)},
        {"r10d",  new(offset: 2, rex: 0x41, size: 4)},
        {"r11d",  new(offset: 3, rex: 0x41, size: 4)},
        {"r12d",  new(offset: 4, rex: 0x41, size: 4)},
        {"r13d",  new(offset: 5, rex: 0x41, size: 4)},
        {"r14d",  new(offset: 6, rex: 0x41, size: 4)},
        {"r15d",  new(offset: 7, rex: 0x41, size: 4)},
        {"rax",   new()},
        {"rcx",   new(offset: 1)},
        {"rdx",   new(offset: 2)},
        {"rbx",   new(offset: 3)},
        {"rsp",   new(offset: 4)},
        {"rbp",   new(offset: 5)},
        {"rsi",   new(offset: 6)},
        {"rdi",   new(offset: 7)},
        {"r8",    new(rex: 0x41)},
        {"r9",    new(offset: 1, rex: 0x41)},
        {"r10",   new(offset: 2, rex: 0x41)},
        {"r11",   new(offset: 3, rex: 0x41)},
        {"r12",   new(offset: 4, rex: 0x41)},
        {"r13",   new(offset: 5, rex: 0x41)},
        {"r14",   new(offset: 6, rex: 0x41)},
        {"r15",   new(offset: 7, rex: 0x41)},
        {"xmm0",  new(RegisterType.SSE)},
        {"xmm1",  new(RegisterType.SSE, 1)},
        {"xmm2",  new(RegisterType.SSE, 2)},
        {"xmm3",  new(RegisterType.SSE, 3)},
        {"xmm4",  new(RegisterType.SSE, 4)},
        {"xmm5",  new(RegisterType.SSE, 5)},
        {"xmm6",  new(RegisterType.SSE, 6)},
        {"xmm7",  new(RegisterType.SSE, 7)},
        {"xmm8",  new(RegisterType.SSE, 0, 0x44)},
        {"xmm9",  new(RegisterType.SSE, 1, 0x44)},
        {"xmm10", new(RegisterType.SSE, 2, 0x44)},
        {"xmm11", new(RegisterType.SSE, 3, 0x44)},
        {"xmm12", new(RegisterType.SSE, 4, 0x44)},
        {"xmm13", new(RegisterType.SSE, 5, 0x44)},
        {"xmm14", new(RegisterType.SSE, 6, 0x44)},
        {"xmm15", new(RegisterType.SSE, 7, 0x44)},
        {"ymm0",  new(RegisterType.AVX)},
        {"ymm1",  new(RegisterType.AVX, 1)},
        {"ymm2",  new(RegisterType.AVX, 2)},
        {"ymm3",  new(RegisterType.AVX, 3)},
        {"ymm4",  new(RegisterType.AVX, 4)},
        {"ymm5",  new(RegisterType.AVX, 5)},
        {"ymm6",  new(RegisterType.AVX, 6)},
        {"ymm7",  new(RegisterType.AVX, 7)},
        {"ymm8",  new(RegisterType.AVX, 0, 0x44)},
        {"ymm9",  new(RegisterType.AVX, 1, 0x44)},
        {"ymm10", new(RegisterType.AVX, 2, 0x44)},
        {"ymm11", new(RegisterType.AVX, 3, 0x44)},
        {"ymm12", new(RegisterType.AVX, 4, 0x44)},
        {"ymm13", new(RegisterType.AVX, 5, 0x44)},
        {"ymm14", new(RegisterType.AVX, 6, 0x44)},
        {"ymm15", new(RegisterType.AVX, 7, 0x44)}
    };

    public static readonly Dictionary<string, InstructionDefinition[]> Instructions = new()
    {
        {"fcos",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFF} }},
        {"fld",    new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Value1 = new(true)} }},
        {"fld1",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xE8} }},
        {"fptan",  new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xF2} }},
        {"fsin",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFE} }},
        {"fst",    new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Extension = 0x10, Value1 = new(true)} }},
        {"fstp",   new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Extension = 0x18, Value1 = new(true)} }},
        {"fyl2x",  new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xF1} }},
        {"mov",    new InstructionDefinition[]{
            // TODO Better implement REX translation
            new() {Rex = 0x48, Opcode = 0xB8, AddRegisterToOpcode = true, Value1 = new(), Value2 = new(constant: true)},
            new() {Rex = 0x48, Opcode = 0x89, Value1 = new(true), Value2 = new()}
        }},
        {"movsd",  new InstructionDefinition[]{
            new() {Prefix = 0x0F2, OF = true, Opcode = 0x11, Value1 = new(true), Value2 = new(type: RegisterType.SSE)},
            new() {Prefix = 0x0F2, OF = true, Opcode = 0x10, Value1 = new(type: RegisterType.SSE), Value2 = new(true)},
            new() {Prefix = 0x0F2, OF = true, Opcode = 0x10, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)}
        }},
        {"movss",  new InstructionDefinition[]{
            new() {Prefix = 0x0F3, OF = true, Opcode = 0x11, Value1 = new(true), Value2 = new(type: RegisterType.SSE)},
            new() {Prefix = 0x0F3, OF = true, Opcode = 0x10, Value1 = new(type: RegisterType.SSE), Value2 = new(true)},
            new() {Prefix = 0x0F3, OF = true, Opcode = 0x10, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)}
        }},
        {"movq",   new InstructionDefinition[]{ new() {Prefix = 0x66, Rex = 0x48, OF = true, Opcode = 0x6E, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new()} }},
        {"sqrtsd", new InstructionDefinition[]{ new() {Prefix = 0xF2, OF = true, Opcode = 0x51, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)} }},
        {"xadd",   new InstructionDefinition[]{
            new() {OF = true, Opcode = 0xC0, AddressSpace = 1, Value1 = new(true), Value2 = new(size: 1)},
            new() {Prefix = 0x66, OF = true, Opcode = 0xC1, AddressSpace = 2, Value1 = new(true), Value2 = new(size: 2)},
            new() {OF = true, Opcode = 0xC1, AddressSpace = 4, Value1 = new(true), Value2 = new(size: 4)},
            new() {Rex = 0x48, OF = true, Opcode = 0xC1, Value1 = new(true), Value2 = new()}
        }}
        // TODO Add more
    };
}

public enum RegisterType : byte
{
    General,
    SSE,
    AVX
}

public class RegisterDefinition
{
    public RegisterDefinition(RegisterType type = RegisterType.General, byte offset = 0, byte rex = 0, byte size = 8)
    {
        Type = type;
        Offset = offset;
        Rex = rex;
        Size = size;
    }

    public RegisterType Type;
    public byte Offset;
    public byte Rex;
    public byte Size;
}

public class InstructionDefinition
{
    public byte Prefix;
    public byte Rex;
    public bool OF;
    public byte Opcode;
    public bool AddRegisterToOpcode;
    public byte Opcode2;
    public bool HasExtension;
    public byte Extension;
    public byte Mod;
    public byte AddressSpace = 8;
    public InstructionArgument Value1;
    public InstructionArgument Value2;
}

public class InstructionArgument
{
    public InstructionArgument(bool memory = false, bool constant = false, byte size = 8, RegisterType type = RegisterType.General)
    {
        Memory = memory;
        Constant = constant;
        Type = type;
        RegisterSize = size;
    }

    public bool Memory;
    public bool Constant;
    public byte RegisterSize;
    public RegisterType Type;
}
