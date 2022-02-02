using System.Collections.Generic;

namespace ol;

public static class Assembly
{
    public static readonly Dictionary<string, RegisterType> Registers = new()
    {
        {"rax",   RegisterType.General},
        {"rcx",   RegisterType.General},
        {"rdx",   RegisterType.General},
        {"rbx",   RegisterType.General},
        {"rsi",   RegisterType.General},
        {"rdi",   RegisterType.General},
        {"rsp",   RegisterType.General},
        {"rbp",   RegisterType.General},
        {"r8",    RegisterType.General},
        {"r9",    RegisterType.General},
        {"r10",   RegisterType.General},
        {"r11",   RegisterType.General},
        {"r12",   RegisterType.General},
        {"r13",   RegisterType.General},
        {"r14",   RegisterType.General},
        {"r15",   RegisterType.General},
        {"mm0",   RegisterType.MMX},
        {"mm1",   RegisterType.MMX},
        {"mm2",   RegisterType.MMX},
        {"mm3",   RegisterType.MMX},
        {"mm4",   RegisterType.MMX},
        {"mm5",   RegisterType.MMX},
        {"mm6",   RegisterType.MMX},
        {"mm7",   RegisterType.MMX},
        {"xmm0",  RegisterType.SSE},
        {"xmm1",  RegisterType.SSE},
        {"xmm2",  RegisterType.SSE},
        {"xmm3",  RegisterType.SSE},
        {"xmm4",  RegisterType.SSE},
        {"xmm5",  RegisterType.SSE},
        {"xmm6",  RegisterType.SSE},
        {"xmm7",  RegisterType.SSE},
        {"xmm8",  RegisterType.SSE},
        {"xmm9",  RegisterType.SSE},
        {"xmm10", RegisterType.SSE},
        {"xmm11", RegisterType.SSE},
        {"xmm12", RegisterType.SSE},
        {"xmm13", RegisterType.SSE},
        {"xmm14", RegisterType.SSE},
        {"xmm15", RegisterType.SSE},
        {"ymm0",  RegisterType.AVX},
        {"ymm1",  RegisterType.AVX},
        {"ymm2",  RegisterType.AVX},
        {"ymm3",  RegisterType.AVX},
        {"ymm4",  RegisterType.AVX},
        {"ymm5",  RegisterType.AVX},
        {"ymm6",  RegisterType.AVX},
        {"ymm7",  RegisterType.AVX},
        {"ymm8",  RegisterType.AVX},
        {"ymm9",  RegisterType.AVX},
        {"ymm10", RegisterType.AVX},
        {"ymm11", RegisterType.AVX},
        {"ymm12", RegisterType.AVX},
        {"ymm13", RegisterType.AVX},
        {"ymm14", RegisterType.AVX},
        {"ymm15", RegisterType.AVX}
    };

    public static readonly Dictionary<string, InstructionDefinition[]> Instructions = new() {
        // TODO Add more
        {"fcos",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFF} }},
        {"fld",    new InstructionDefinition[]{ new() {Opcode = 0xDD, Value1 = new(true)} }},
        {"fptan",  new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xF2} }},
        {"fsin",   new InstructionDefinition[]{ new() {Opcode = 0xD9, Opcode2 = 0xFE} }},
        {"fst",    new InstructionDefinition[]{ new() {Opcode = 0xDD, Value1 = new(true)} }},
        {"fstp",   new InstructionDefinition[]{ new() {Opcode = 0xDD, Value1 = new(true)} }},
        {"sqrtsd", new InstructionDefinition[]{ new() {Prefix = 0xF2, OF = true, Opcode = 0x51, Value1 = new(type: RegisterType.SSE), Value2 = new(type: RegisterType.SSE)} }},
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
    public RegisterType Type;
    public byte Offset;
}

public struct InstructionDefinition
{
    public byte Prefix;
    public bool OF;
    public byte Opcode;
    public byte Opcode2;
    public byte Extension;
    public InstructionArgument Value1;
    public InstructionArgument Value2;
}

public class InstructionArgument
{
    public InstructionArgument(bool memory = false, bool constant = false, RegisterType type = RegisterType.General)
    {
        Memory = memory;
        Type = type;
    }

    public bool Memory;
    public bool Constant;
    public RegisterType Type;
}
