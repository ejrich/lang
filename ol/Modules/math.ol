// General math module

float64 square_root(float64 value) {
    asm {
        in xmm0, value;
        sqrtsd xmm0, xmm0;
        out value, xmm0;
    }

    return value;
}

float64 sine(float64 value) {
    asm {
        in rax, &value;
        fld [rax];
        fsin;
        fstp [rax];
    }

    return value;
}

float64 cosine(float64 value) {
    asm {
        in rax, &value;
        fld [rax];
        fcos;
        fstp [rax];
    }

    return value;
}

float64 tangent(float64 value) {
    asm {
        in rax, &value;
        fld [rax];
        fptan;
        fstp [rax];
        fstp [rax];
    }

    return value;
}

float64 log_2(float64 value) {
    asm {
        in rax, &value;
        fld1;
        fld [rax];
        fyl2x;
        fstp[rax];
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

T floor<T>(T value) {
    #assert T == float || T == float64;

    result := cast(s64, value);
    return cast(T, result);
}
