main() {
    operator_overloading();

    generic_overloads();

    index_overloading();
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

    mod := a % b;
    printf("Modulus: x = %.2f, y = %.2f, z = %.2f\n", mod.x, mod.y, mod.z);

    a_int: Vector3I = { x = 1; y = 1; z = 1; }
    b_int: Vector3I = { x = 2; y = 2; z = 2; }

    shift_right := a_int >> b_int;
    printf("Shift right: x = %d, y = %d, z = %d\n", shift_right.x, shift_right.y, shift_right.z);

    shift_left := a_int << b_int;
    printf("Shift left: x = %d, y = %d, z = %d\n", shift_left.x, shift_left.y, shift_left.z);

    rotate_right := a_int >>> b_int;
    printf("Rotate right: x = %d, y = %d, z = %d\n", rotate_right.x, rotate_right.y, rotate_right.z);

    rotate_left := a_int <<< b_int;
    printf("Rotate left: x = %d, y = %d, z = %d\n", rotate_left.x, rotate_left.y, rotate_left.z);

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

struct Vector3I {
    int x;
    int y;
    int z;
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

operator % Vector3(Vector3 a, Vector3 b) {
    c: Vector3 = { x = a.x % b.x; y = a.y % b.y; z = a.z % b.z; }
    return c;
}

operator >> Vector3I(Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x >> b.x; y = a.y >> b.y; z = a.z >> b.z; }
    return c;
}

operator << Vector3I(Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x << b.x; y = a.y << b.y; z = a.z << b.z; }
    return c;
}

operator >>> Vector3I(Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x >>> b.x; y = a.y >>> b.y; z = a.z >>> b.z; }
    return c;
}

operator <<< Vector3I(Vector3I a, Vector3I b) {
    c: Vector3I = { x = a.x <<< b.x; y = a.y <<< b.y; z = a.z <<< b.z; }
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
    printf("Vector values: x = %.2f, y = %.2f, z = %.2f\n", vector.x, vector.y, vector.z);
    printf("Vector value: x = %.2f\n", a[3].x);

    b: ListStruct<int>;
    b[8] = 7;
    printf("Integer value: b[8] = %d\n", *b[8]);

    c: NestedStruct;
    initial_vector: Vector3 = { x = 1.0; y = 2.0; z = 3.0; }
    c.inner_list[2] = initial_vector;
    c.inner_list[2].y = 1.5;
    c.inner_list[2].x++;
    new_vec := c.inner_list[2];
    printf("Inner list vector values: x = %.2f, y = %.2f, z = %.2f\n", c.inner_list[2].x, new_vec.y, new_vec.z);
}

struct SOAVector3 {
    List<float>[5] x;
    List<float>[5] y;
    List<float>[5] z;
}

operator [] SOAVector3(SOAVector3 a, int index) : Vector3 {
    value: Vector3 = { x = a.x[index]; y = a.y[index]; z = a.z[index]; }
    return value;
}

struct ListStruct<T> {
    int max = 10;
    List<T>[10] list;
}

operator [] ListStruct<T>(ListStruct<T> a, int index) : T* {
    if index < 0 then return &a.list[0];
    if index >= a.max then return &a.list[a.max - 1];
    return &a.list[index];
}

struct NestedStruct {
    bool foo;
    ListStruct<Vector3> inner_list;
}

#run main();
