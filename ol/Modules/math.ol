// General math module

float64 square_root(float64 value) {
    asm {
        // Store the parameters for the assembly
        in xmm0, value;

        // Body of the assembly code
        sqrtsd xmm0, xmm0;

        // Set the registers to be captured in the output
        out value, xmm0;
    }

    return value;
}

T float_mod<T>(T x, T y) {
    #assert T == float || T == float64;

    result := x / y;
    whole := cast(int, result);
    remainder := result - whole;

    return remainder * y;
}
