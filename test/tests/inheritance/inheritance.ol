#import standard

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

    default_free(a);
    default_free(b);
}

BaseStruct* returns_base(bool use_struct_a) {
    if use_struct_a {
        a: StructA;
        a_pointer := default_allocator(size_of(a));
        memory_copy(a_pointer, &a, size_of(a));
        return a_pointer;
    }
    else {
        base: BaseStruct;
        base_pointer := default_allocator(size_of(base));
        memory_copy(base_pointer, &base, size_of(base));
        return base_pointer;
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
