main() {
    operator_overloading();
}

operator_overloading() {

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
