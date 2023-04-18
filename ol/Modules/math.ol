// General math module

PI: float64 = 3.14159265359; #const

float abs(float value) {
    if value < 0 return -value;
    return value;
}

float64 square_root(float64 value) #inline {
    asm {
        in xmm0, value;
        sqrtsd xmm0, xmm0;
        out value, xmm0;
    }

    return value;
}

float64 sine(float64 value) #inline {
    asm {
        in rax, &value;
        fld [rax];
        fsin;
        fstp [rax];
    }

    return value;
}

float64 cosine(float64 value) #inline {
    asm {
        in rax, &value;
        fld [rax];
        fcos;
        fstp [rax];
    }

    return value;
}

float64 tangent(float64 value) #inline {
    asm {
        in rax, &value;
        fld [rax];
        fptan;
        fstp [rax];
        fstp [rax];
    }

    return value;
}

float64 log_2(float64 value) #inline {
    asm {
        in rax, &value;
        fld1;
        fld [rax];
        fyl2x;
        fstp [rax];
    }

    return value;
}

float64 log_x(float64 value, int x) #inline {
    return log_2(value) / log_2(cast(float64, x));
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

T ceil<T>(T value) {
    #assert T == float || T == float64;

    result := cast(s64, value) + 1;
    return cast(T, result);
}

int integer_length(s64 value) {
    count := 1;
    if value < 0 {
        value *= -1;
        count++;
    }

    while true {
        if value < 10    return count;
        if value < 100   return count + 1;
        if value < 1000  return count + 2;
        if value < 10000 return count + 3;
        value /= 10000;
        count += 4;
    }

    return count;
}

int popcnt(int value) {
    result: int;
    asm {
        in eax, value;
        popcnt eax, eax;
        out result, eax;
    }

    return result;
}


// Data structures
struct Vector2 {
    x: float;
    y: float;
}

struct Vector3 {
    x: float;
    y: float;
    z: float;
}

struct Vector4 {
    x: float;
    y: float;
    z: float;
    w: float;
}

struct Matrix4 {
    a: Vector4;
    b: Vector4;
    c: Vector4;
    d: Vector4;
}

struct Quaternion {
    x: float;
    y: float;
    z: float;
    w: float;
}


operator + (Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x + b.x; y = a.y + b.y; z = a.z + b.z; }
    return c;
}

operator - (Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x - b.x; y = a.y - b.y; z = a.z - b.z; }
    return c;
}

operator == (Vector3 a, Vector3 b) {
    return !(a != b);
}

operator != (Vector3 a, Vector3 b) {
    if a.x != b.x || a.y != b.y || a.z != b.z return true;

    return false;
}

Vector3 multiply(Vector3 vec, float value) {
    vec.x *= value;
    vec.y *= value;
    vec.z *= value;
    return vec;
}

Vector3 multiply(Vector3 vec, Matrix4 mat) {
    vec = {
        x = vec.x * mat.a.x + vec.y * mat.b.y + vec.z * mat.c.z + mat.d.x;
        y = vec.x * mat.a.y + vec.y * mat.b.y + vec.z * mat.c.y + mat.d.y;
        z = vec.x * mat.a.z + vec.y * mat.b.z + vec.z * mat.c.z + mat.d.z;
    }
    return vec;
}

float length_squared(Vector3 a) #inline {
    return a.x * a.x + a.y * a.y + a.z * a.z;
}

float dot(Vector3 a, Vector3 b) #inline {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

Vector3 cross(Vector3 a, Vector3 b) {
    c: Vector3 = {
        x = a.y * b.z - a.z * b.y;
        y = a.z * b.x - a.x * b.z;
        z = a.x * b.y - a.y * b.x;
    }
    return c;
}

Vector3 cross_one(Vector3 a) {
    c: Vector3 = {
        x = a.y - a.z;
        y = a.z - a.x;
        z = a.x - a.y;
    }
    return c;
}

Vector3 inverse(Vector3 a) {
    a.x *= -1;
    a.y *= -1;
    a.z *= -1;
    return a;
}

Vector3 normalize(Vector3 a) {
    length := square_root(a.x * a.x + a.y * a.y + a.z * a.z);

    a.x /= length;
    a.y /= length;
    a.z /= length;
    return a;
}
