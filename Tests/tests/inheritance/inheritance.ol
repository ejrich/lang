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

}

set_array_elements() {

}

#run main();
