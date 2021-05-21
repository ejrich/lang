main() {
    basic();

    ptr := pointers();
    printf("Pointer = %p, Value = %d\n", ptr, *ptr);

    str := string_test();
    printf("%s - Hello world %d, %d\n", str, 1, 2);

    sum_test();

    // Don't run this at compile time
    // overflow_test();

    set_global(8);
    printf("'global_a' = %d\n", global_a);
    printf("'global_b' = %d\n", global_b);

    poly_test();

    enums();

    nulls();

    default_args(8);
    default_args();

    lists();

    compiler_directives();

    type_information();

    type_casts();
}

basic() {
    hello := "This is an \"escaped\" string literal\nWith a new line!";
    a := 4.2;
    a++;
    baz := foo() + bar(3 + 1, "Hello", 1.2);
    b := 6 * (4 - 1);
    c := 2;
    d := a + 1 == b + 2 && (1 + b) == 2 || b > 3 + 4 * c - 1;
    e := !d;
    f := test(3);
    g := test_each();
    h := 3 > a;
    fac6 := factorial(6);
    printf("%d! = %d\n", 6, fac6);
    my_struct := create();
    printf("my_struct: field = %d, something = %f, subvalue.something = %d\n", my_struct.field, my_struct.something, my_struct.subValue.something);
    call_field := create().something;
    printf("call_field = %f\n", call_field);

    prim := primitives();
}

global_a := 7;
global_b: int = 456; #const

set_global(int a) {
    if a > 10 then global_a = a * 90;
}

bool test(int a) {
    if a == 3 then return true;
    else then return false;
}

int foo() {
    a := 0;
    a++;
    ++a;
    a += 1;
    b := --a + 1;
    b = -b;
    while a < 10 {
        a = a + 1;
    }
    return a;
}

int bar(int a, string b, float c) {
    if a == 4 then return a;
    else if a == 5 {
        return 9;
    }
    return a + 3;
}

int test_each() {
    a := 1;
    each i in 1..10 {
        a = a + i;
    }
    return a;
}

int factorial(int n) {
    if n == 0 then return 1;
    return n * factorial(n - 1);
}

struct MyStruct {
    u8 field;
    float something = 4.2;
    OtherStruct subValue;
}

struct OtherStruct {
    int something = 5;
}

MyStruct create() {
    s1: MyStruct;
    s2: OtherStruct;
    s2.something += 8;
    s1.field++;
    s1.subValue.something = s2.something;
    return s1;
}

int primitives() {
    a: u64 = 2147486648;
    b: s64;
    c: u32 = 12345;
    d: s32 = 12345;
    e: u16 = 32768;
    f: s16 = 32767;
    d = f + 123;
    g: u8 = 200;
    h: s8 = -128;
    //h = 12 + 23;
    i: float64 = -1.23456;
    x := 0xfeABD4;
    j := g + i;
    k := e > e;
    return d;
}

struct ListStruct {
    int something = 5;
    List<float64>[6] list;
}

List<int> create_list(int count) {
    list: List<int>[count];
    each i in 0..count-1 {
        list[i] = i * 5;
    }
    a := list[count-2];
    list[0]++;
    ++list[0];

    manipulate_list_struct();
    return list;
}

manipulate_list_struct() {
    s: ListStruct;
    s.list[0] = 8.9;
    ++s.list[0];
    printf("%.2f\n", s.list[0]);
}

struct Node {
    int value;
    Node* next;
}

int* pointers() {
    node: Node = { value = 9; }
    next: Node = { value = 8; }
    node.next = &next;

    loop_node := &node;
    i := 1;
    while loop_node {
        printf("Value %d = %d\n", i++, loop_node.value);
        loop_node = loop_node.next;
    }

    list: List<int>[8];
    list_ptr := &list[4];
    a := 6;
    b := &a;
    d := &a + 1; // Value will be garbage
    c := *b;
    return b;
}

string string_test() {
    return "something";
}

sum_test() {
    sum_list: List<int>[7];
    sum_list[0] = 4;
    sum_list[1] = 41;
    sum_list[2] = 544;
    sum_list[3] = 244;
    sum_list[4] = 42;
    sum_list[5] = 14;
    sum_list[6] = 23;

    printf("Sum of List   = %d\n", sum(sum_list));
    printf("Sum of Params = %d\n", sum2(4, 41, 544, 244, 42, 14, 23));
}

int sum(List<int> args) {
    sum := 0;
    each i in args then sum += i;
    return sum;
}

int sum2(Params<int> args) {
    sum := 0;
    each i in args then sum += i;
    return sum;
}

overflow_test() {
    each i in 1..1000000 {
        a := i * 9;
        printf("%d\n", a);
    }
    a := 4;
}

struct PolyStruct<T, U> {
    T field1;
    U field2;
}

poly_test() {
    a: PolyStruct<int, float>;
    a.field1 = 87;
    a.field2 = 3.14159;
    printf("%d, %f\n", a.field1, a.field2);
}

enums() {
    state := current_state(7);
    printf("Current state - %d\n", state);
    if state == State.Running then printf("The state is Running\n");
    else if state != State.Running then printf("The state is not Running\n");
}

enum State {
    Stopped;
    Stopping;
    Starting = 5;
    Running;
}

struct StateStruct {
    State state = State.Running;
    int something;
    bool flag = true;
}

State current_state(int a) {
    state: StateStruct;
    printf("State is %d\n", state.state);

    if a > 5 then return State.Running;
    else if a == 5 then return State.Starting;
    return State.Stopped;
}

nulls() {
    null_val: int* = null;
    null_val = null;
    test_int := 7;
    null_test(&test_int);
    null_test(null);
}

null_test(int* value_ptr) {
    if value_ptr == null then printf("Pointer is null\n");
    else then printf("Pointer value is %d\n", *value_ptr);
}

default_args(int val = 5) {
    printf("Value = %d\n", val);
}

lists() {
    list := create_list(4);

    z := 1;
    each i in create_list(5) {
        printf("Value %d = %d\n", z++, i);
    }
}

open_window() {
    #if os == OS.Linux {
        XOpenDisplay("Hello");
        printf("Opening X11 window\n");
    }
}

compiler_directives() {
    const_value := 7; #const
    printf("Constant value = %d\n", const_value);
    #assert factorial(6) == 720;
    #if build_env == BuildEnv.Debug then
        printf("Running Debug code\n");
    #if build_env == BuildEnv.Release then
        printf("Running Release code\n");

    open_window();
}

#run {
    build();
    main();
}

build() {
    if os == OS.Linux {
        add_dependency("X11");
    }
}

#assert global_b > 0;

#if os == OS.Linux {
    XOpenDisplay(string name) #extern "X11"
}

type_information() {
    print_type_info(MyStruct);
    print_type_info(u8);
    print_type_info(List<string>);
    print_type_info(PolyStruct<int*, float>);
    print_type_info(PolyStruct<List<int>, float>);
    print_type_info(s32*);
    print_type_info(State);

    my_struct := create();
    print_type_info(my_struct.subValue);
    print_type_info(bar);
}

print_type_info(Type type) {
    type_info := type_of(type);
    printf("Type Name = %s, Type Kind = %d, Type Size = %d, Field Count = %d\n", type_info.name, type_info.type, type_info.size, type_info.fields.length);
    each field in type_info.fields then
        printf("Field name = %s, Field type name = %s\n", field.name, field.type_info.name);
    each enum_value in type_info.enum_values then
        printf("Enum value name = %s, Value = %d\n", enum_value.name, enum_value.value);

    if type_info.type == TypeKind.Function {
        printf("Return type = %s\n", type_info.return_type.name);
        each arg in type_info.arguments then
            printf("Argument name = %s, Argument type name = %s, Argument type kind = %d\n", arg.name, arg.type_info.name, arg.type_info.type);
    }
}

type_casts() {
    a: u64 = 0xFFFFFFFFFFFF0000;
    b := cast(s32, a);
    c := cast(float64, a);
    d := cast(u8, State.Running);
    printf("a = %llu, b = %d, c = %f, d = %d\n", a, b, c, d);
}

// #run main();
