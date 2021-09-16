main() {
    operator_overloading();

    generic_overloads();
}

operator_overloading() {
    a: Vector3 = { x = 1.0; y = 1.0; z = 1.0; }
    b: Vector3 = { x = 2.0; y = 2.0; z = 2.0; }

    add := a + b;
    printf("Add: x = %.2f, y = %.2f, z = %.2f\n", add.x, add.y, add.z);

    sub := a - b;
    printf("Subtract: x = %.2f, y = %.2f, z = %.2f\n", sub.x, sub.y, sub.z);

    mult := a * b;
    printf("Multiply: x = %.2f, y = %.2f, z = %.2f\n", mult.x, mult.y, mult.z);

    div := a / b;
    printf("Divide: x = %.2f, y = %.2f, z = %.2f\n", div.x, div.y, div.z);

    and := a && b;
    or := a || b;
    xor := a ^ b;
    equals := a == b;
    not_equals := a != b;
    printf("And: %d, Or: %d, Xor: %d, Equals: %d, Not Equals: %d\n", and, or, xor, equals, not_equals);

    gte := a >= b;
    lte := a <= b;
    gt := a > b;
    lt := a < b;
    printf("Greater than or equal: %d, Less than or equal: %d, Greater than: %d, Less than: %d\n", gte, lte, gt, lt);
}

struct Vector3 {
    float x;
    float y;
    float z;
}

// Numeric operator overloads, these will return the overload type
operator + Vector3(Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x + b.x; y = a.y + b.y; z = a.z + b.z; }
    return c;
}

operator - Vector3(Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x - b.x; y = a.y - b.y; z = a.z - b.z; }
    return c;
}

operator * Vector3(Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x * b.x; y = a.y * b.y; z = a.z * b.z; }
    return c;
}

operator / Vector3(Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x / b.x; y = a.y / b.y; z = a.z / b.z; }
    return c;
}

// Comparison operator overloads, these will return bool
operator && Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size != 0 && b_size != 0;
}

operator || Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size != 0 || b_size != 0;
}

operator == Vector3(Vector3 a, Vector3 b) {
    return a.x == b.x && a.y == b.y && a.z == b.z;
}

operator != Vector3(Vector3 a, Vector3 b) {
    return a.x != b.x || a.y != b.y || a.z != b.z;
}

operator >= Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size >= b_size;
}

operator <= Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size <= b_size;
}

operator > Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size > b_size;
}

operator < Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size < b_size;
}

operator ^ Vector3(Vector3 a, Vector3 b) {
    a_size := a.x + a.y + a.z;
    b_size := b.x + b.y + b.z;
    return a_size != 0 ^ b_size != 0;
}

generic_overloads() {
    a: PolyStruct<int, float> = { a = 5; b = 1.5; }
    b: PolyStruct<int, float> = { a = 5; b = 1.5; }

    add := a + b;
    printf("Polymorphic Add: a = %d, b = %.2f\n", add.a, add.b);
}

struct PolyStruct<T, U> {
    T a;
    U b;
}

operator + PolyStruct<T, U>(PolyStruct<T, U> a, PolyStruct<T, U> b) {
    #assert type_of(T).type == TypeKind.Integer && type_of(U).type == TypeKind.Float;
    c: PolyStruct<T, U>;
    return c;
}


#run main();
