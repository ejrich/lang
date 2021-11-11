main() {
    variables();

    calls();

    structs();
}

interface foo(int x, int y)
interface int bar(int x, float y)
interface Foo foobar(s64 x, float y)
interface Foo* baz(s64 x, float y)

struct Foo {
    bar: int;
    baz: float;
}

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
    d: foobar;
    e: baz;
}

structs() #print_ir {
    pointers: FunctionPointers = { b = foo_impl; c = bar_impl; }

    pointers.b(10, 20);
    pointers.c(10, 31.4);

    a := pointers.d();
    b := pointers.d().bar;
    c := pointers.e().baz;
}

#run main();
