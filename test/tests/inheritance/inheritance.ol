main() {
    basic_inheritance();

    function_returns();

    set_values();

    set_array_elements();
}

struct BaseStruct {
    foo := 9;
    bar := 4.2;
}

struct StructA : BaseStruct {
    baz := 100;
}

basic_inheritance() {
    base: BaseStruct;
    a: StructA;

    assert(base.foo == a.foo);
    assert(base.bar == a.bar);
    assert(a.baz == 100);
}

function_returns() {
    a := returns_base(true);
    b := returns_base(false);

    assert(a.foo == b.foo);
    assert(a.bar == b.bar);
}

BaseStruct* returns_base(bool use_struct_a) {
    if use_struct_a {
        a: StructA;
        return &a;
    }
    else {
        base: BaseStruct;
        return &base;
    }
}

set_values() {
    a: StructA = {foo = 88;}

    s: StructWithBase = {b = &a;}

    assert(s.b == &a);
    assert(s.b.foo == a.foo);
}

struct StructWithBase {
    foo: int;
    b: BaseStruct*;
}

set_array_elements() {
    a: StructA;
    b: BaseStruct;

    array: Array<BaseStruct*>[1];
    array[0] = &a;
    assert(array[0] == &a);

    array[0] = &b;
    assert(array[0] == &b);

    carray: CArray<BaseStruct*>[1];
    carray[0] = &a;
    assert(carray[0] == &a);

    carray[0] = &b;
    assert(carray[0] == &b);
}

#run main();
