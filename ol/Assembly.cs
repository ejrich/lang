using System.Collections.Generic;

namespace ol;

public static class Assembly
{
    public static readonly Dictionary<string, RegisterDefinition> Registers = new()
    {
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
        {"mm0",   new(RegisterType.MMX)},
        {"mm1",   new(RegisterType.MMX, 1)},
        {"mm2",   new(RegisterType.MMX, 2)},
        {"mm3",   new(RegisterType.MMX, 3)},
        {"mm4",   new(RegisterType.MMX, 4)},
        {"mm5",   new(RegisterType.MMX, 5)},
        {"mm6",   new(RegisterType.MMX, 6)},
        {"mm7",   new(RegisterType.MMX, 7)},
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
        {"fptan",  new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xF2} }},
        {"fsin",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFE} }},
        {"fst",    new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Extension = 0x10, Value1 = new(true)} }},
        {"fstp",   new InstructionDefinition[]{ new() {Opcode = 0xDD, HasExtension = true, Extension = 0x18, Value1 = new(true)} }},
        {"mov",    new InstructionDefinition[]{ new() {Rex = 0x48, Opcode = 0xB8, AddRegisterToOpcode = true, Value1 = new(), Value2 = new(constant: true)} }},
        {"movsd",    new InstructionDefinition[]{
            new() {Prefix = 0x0F2, OF = true, Opcode = 0x10, Value1 = new(type: RegisterType.SSE), Value2 = new(true)},
            new() {Prefix = 0x0F2, OF = true, Opcode = 0x11, Value1 = new(true), Value2 = new(type: RegisterType.SSE)}
        }},
        {"sqrtsd", new InstructionDefinition[]{ new() {Prefix = 0xF2, OF = true, Opcode = 0x51, Mod = 0xC0, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)} }},
        // TODO Add more
    };
}

public enum RegisterType : byte
{
    General,
    MMX,
    SSE,
    AVX
}

public class RegisterDefinition
{
    public RegisterDefinition(RegisterType type = RegisterType.General, byte offset = 0, byte rex = 0)
    {
        Type = type;
        Offset = offset;
        Rex = rex;
    }

    public RegisterType Type;
    public byte Offset;
    public byte Rex;
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
    public InstructionArgument Value1;
    public InstructionArgument Value2;
}

public class InstructionArgument
{
    public InstructionArgument(bool memory = false, bool constant = false, RegisterType type = RegisterType.General)
    {
        Memory = memory;
        Constant = constant;
        Type = type;
    }

    public bool Memory;
    public bool Constant;
    public RegisterType Type;
}
