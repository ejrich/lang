main() {
    operator_overloading();
}

operator_overloading() {
    a: Vector3 = { x = 1.0; y = 1.0; z = 1.0; }
    b: Vector3 = { x = 2.0; y = 2.0; z = 2.0; }

    c := a + b;
    printf("x = %.2f, y = %.2f, z = %.2f\n", c.x, c.y, c.z);
}

struct Vector3 {
    float x;
    float y;
    float z;
}

operator + Vector3(Vector3 a, Vector3 b) {
    a.x += b.x;
    a.y += b.y;
    a.z += b.z;
    return a;
}

#run main();
