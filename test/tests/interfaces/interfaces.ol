#import standard

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
    print("% %\n", x, y);
}

int bar_impl(int x, float y) {
    print("% %\n", x, y);
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
    print("This is foo_impl2 % %\n", x, y);
}

struct FunctionPointers {
    a: int;
    b: foo;
    c: bar;
    d: foobar;
    e: baz;
}

Foo foobar_impl(s64 x, float y) {
    a: Foo = { bar = x; baz = y; }
    print("foobar_impl %, %\n", x, y);
    return a;
}

Foo* baz_impl(s64 x, float y) {
    a: Foo = { bar = x; baz = y; }
    print("baz_impl %, %\n", x, y);
    return &a;
}

structs() {
    pointers: FunctionPointers = { b = foo_impl; c = bar_impl; d = foobar_impl; e = baz_impl; }

    pointers.b(10, 20);
    pointers.c(10, 31.4);

    a := pointers.d(23, 4.20);
    b := pointers.d(230, 42.0).bar;
    c := pointers.e(2300, 420.0).baz;

    print("Values % % %\n", a.bar, b, c);
}

#run main();
