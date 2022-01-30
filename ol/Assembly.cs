using System.Collections.Generic;

namespace ol;

public static class Assembly
{
    public static readonly HashSet<string> Registers = new() {
        "rax", "rcx", "rdx", "rbx", "rsi", "rdi", "rsp", "rbp",
        "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15",
        "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "xmm6", "xmm7",
        "xmm8", "xmm9", "xmm10", "xmm11", "xmm12", "xmm13", "xmm14", "xmm15",
    };

    public static readonly HashSet<string> Instructions = new() {
        "fcos", "fld", "fptan", "fsin", "fst", "fstp", "sqrtsd" // TODO Add more
    };
}
