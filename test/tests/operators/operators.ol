#import standard
#import math

main() {
    operator_overloading();

    generic_overloads();

    index_overloading();
}

operator_overloading() {
    a: Vector3 = { x = 1.0; y = 1.0; z = 1.0; }
    b: Vector3 = { x = 2.0; y = 2.0; z = 2.0; }

    add := a + b;
    print("Add: x = %, y = %, z = %\n", add.x, add.y, add.z);

    sub := a - b;
    print("Subtract: x = %, y = %, z = %\n", sub.x, sub.y, sub.z);

    mult := a * b;
    print("Multiply: x = %, y = %, z = %\n", mult.x, mult.y, mult.z);

    div := a / b;
    print("Divide: x = %, y = %, z = %\n", div.x, div.y, div.z);

    mod := a % b;
    print("Modulus: x = %, y = %, z = %\n", mod.x, mod.y, mod.z);

    a_int: Vector3I = { x = 1; y = 1; z = 1; }
    b_int: Vector3I = { x = 2; y = 2; z = 2; }

    bitwise_or := a_int | b_int;
    print("Bitwise bitwise_or: x = %, y = %, z = %\n", bitwise_or.x, bitwise_or.y, bitwise_or.z);

    bitwise_and := a_int & b_int;
    print("Bitwise and: x = %, y = %, z = %\n", bitwise_and.x, bitwise_and.y, bitwise_and.z);

    shift_right := a_int >> b_int;
    print("Shift right: x = %, y = %, z = %\n", shift_right.x, shift_right.y, shift_right.z);

    shift_left := a_int << b_int;
    print("Shift left: x = %, y = %, z = %\n", shift_left.x, shift_left.y, shift_left.z);

    rotate_right := a_int >>> b_int;
    print("Rotate right: x = %, y = %, z = %\n", rotate_right.x, rotate_right.y, rotate_right.z);

    rotate_left := a_int <<< b_int;
    print("Rotate left: x = %, y = %, z = %\n", rotate_left.x, rotate_left.y, rotate_left.z);

    and := a && b;
    assert(and, "and");
    or := a || b;
    assert(or, "or");
    xor := a ^ b;
    assert(!xor, "xor");
    equals := a == b;
    assert(!equals, "==");
    not_equals := a != b;
    assert(not_equals, "!=");
    print("And: %, Or: %, Xor: %, Equals: %, Not Equals: %\n", and, or, xor, equals, not_equals);

    gte := a >= b;
    assert(!gte, ">=");
    lte := a <= b;
    assert(lte, "<=");
    gt := a > b;
    assert(!gt, ">");
    lt := a < b;
    assert(lt, "<");
    print("Greater than or equal: %, Less than or equal: %, Greater than: %, Less than: %\n", gte, lte, gt, lt);
}

struct Vector3 {
    x: float;
    y: float;
    z: float;
}

struct Vector3I {
    x: int;
    y: int;
    z: int;
}

// Numeric operator overloads, these will return the overload type
operator + (Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x + b.x; y = a.y + b.y; z = a.z + b.z; }
    return c;
}

operator - (Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x - b.x; y = a.y - b.y; z = a.z - b.z; }
    return c;
}

operator * (Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x * b.x; y = a.y * b.y; z = a.z * b.z; }
    return c;
}

operator / (Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x / b.x; y = a.y / b.y; z = a.z / b.z; }
    return c;
}

operator % (Vector3 a, Vector3 b) {
    c: Vector3 = { x = float_mod(a.x, b.x); y = float_mod(a.y, b.y); z = float_mod(a.z, b.z); }
    return c;
}

operator | (Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x | b.x; y = a.y | b.y; z = a.z | b.z; }
    return c;
}

operator & (Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x & b.x; y = a.y & b.y; z = a.z & b.z; }
    return c;
}

operator >> (Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x >> b.x; y = a.y >> b.y; z = a.z >> b.z; }
    return c;
}

operator << (Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x << b.x; y = a.y << b.y; z = a.z << b.z; }
    return c;
}

operator >>> (Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x >>> b.x; y = a.y >>> b.y; z = a.z >>> b.z; }
    return c;
}

operator <<< (Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x <<< b.x; y = a.y <<< b.y; z = a.z <<< b.z; }
    return c;
}

// Comparison operator overloads, these will return bool
operator && (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size != 0 && b_size != 0;
}

operator || (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size != 0 || b_size != 0;
}

operator == (Vector3 a, Vector3 b) {
    return a.x == b.x && a.y == b.y && a.z == b.z;
}

operator != (Vector3 a, Vector3 b) {
    return a.x != b.x || a.y != b.y || a.z != b.z;
}

operator >= (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size >= b_size;
}

operator <= (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size <= b_size;
}

operator > (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size > b_size;
}

operator < (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size < b_size;
}

operator ^ (Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size != 0 ^ b_size != 0;
}

generic_overloads() {
    a: PolyStruct<int, float> = { a = 5; b = 1.5; }
    b: PolyStruct<int, float> = { a = 5; b = 1.5; }

    add := a + b;
    print("Polymorphic Add: a = %, b = %\n", add.a, add.b);
}

struct PolyStruct<T, U> {
    a: T;
    b: U;
}

operator + <T, U>(PolyStruct<T, U> a, PolyStruct<T, U> b) {
    #assert type_of(T).type == TypeKind.Integer && type_of(U).type == TypeKind.Float;
    c: PolyStruct<T, U> = { a = a.a + b.a; b = a.b + b.b; }
    return c;
}

index_overloading() {
    a: SOAVector3;
    a.x[2] = 9.0;
    a.y[2] = 8.0;
    a.z[2] = 7.0;
    a.x[3] = 5.0;

    vector := a[2];
    print("Vector values: x = %, y = %, z = %\n", vector.x, vector.y, vector.z);
    print("Vector value: x = %\n", a[3].x);

    b: ArrayStruct<int>;
    b[8] = 7;
    b[8] += 7;
    b[5] = 5;
    b[5]--;
    // j := &b[7]; // Does not compile, pointer unknown
    print("Integer values: b[5] = %, b[8] = %\n", *b[5], *b[8]);

    c: NestedStruct<Vector3>;
    initial_vector: Vector3 = { x = 1.0; y = 2.0; z = 3.0; }
    c.inner_list[2] = initial_vector;
    c.inner_list[2].y = 1.5;
    c.inner_list[2].x++;
    new_vec := c.inner_list[2];
    print("Inner list vector values: x = %, y = %, z = %\n", c.inner_list[2].x, new_vec.y, new_vec.z);

    d: NestedStruct<float>;
    d.inner_list[1] = 3.7;
    d.inner_list[1]++;
    d.inner_list[1] -= 2.0;
    print("Inner list float value: d.inner_list[1] = %\n", *d.inner_list[1]);
}

struct SOAVector3 {
    x: Array<float>[5];
    y: Array<float>[5];
    z: Array<float>[5];
}

operator [] (SOAVector3 a, int index) : Vector3 {
    value: Vector3 = { x = a.x[index]; y = a.y[index]; z = a.z[index]; }
    return value;
}

struct ArrayStruct<T> {
    max := 10;
    list: Array<T>[10];
}

operator [] <T>(ArrayStruct<T> a, int index) : T* {
    if index < 0 return &a.list[0];
    if index >= a.max return &a.list[a.max - 1];
    return &a.list[index];
}

struct NestedStruct<T> {
    foo: bool;
    inner_list: ArrayStruct<T>;
}

#run main();
