main() {
    variables();

    calls();

    structs();
}

interface foo(int x, int y)
interface int bar(int x, float y)
interface void* baz(s64 x, float y)

variables() {
    a: foo = null;

    assert(a == null);

    a = null;
    a = foo_impl;

    assert(a != null);

    // b := bar_impl; // Does not compile, cannot infer interface from function
    b: bar = bar_impl;

    assert(b != null);

    a(1, 2);
    b(1, 3.14);
}

foo_impl(int x, int y) {
    printf("%d %d\n", x, y);
}

int bar_impl(int x, float y) {
    printf("%d %.2f\n", x, y);
    return 9;
}

calls() {
    use_interface(foo_impl);
    use_interface(foo_impl2);
}

use_interface(foo pointer) {
    pointer(20, 23);
}

foo_impl2(int x, int y) {
    printf("This is foo_impl2 %d %d\n", x, y);
}

struct FunctionPointers {
    a: int;
    b: foo;
    c: bar;
    d: baz;
}

structs() {
    pointers: FunctionPointers;
    // TODO Implement me
}

#run main();
