main() { //#print_ir {
    // Return positive exit code
    /*
        Multi line comment
    */
    // each arg, i in args then printf("Arg %d: \"%s\" -- length = %d\n", i, arg, arg.length);
    hello := "This is an \"escaped\" string literal\nWith a new line!";
    a := 4.2;
    a++;
    baz := foo() + bar(3 + 1, "Hello", 1.2);
    b := 6 * (4 - 1);
    c := 2;
    d := a + 1 == b + 2 && 1 + b == 2 || b > 3 + 4 * c - 1;
    e := !d;
    f := test(3);
    g := test_each();
    h := 3 > a;
    fac6 := factorial(6);
    printf("%d! = %d\n", 6, fac6);
    my_struct := create();
    printf("my_struct: field = %d, something = %f, subvalue.something = %d, subvalue.foo = %d\n", my_struct.field, my_struct.something, my_struct.subValue.something, my_struct.subValue.foo);
    call_field := create().something;
    printf("call_field = %f\n", call_field);

    prim := primitives();
    array := create_array(4);
    ptr := pointers();
    printf("Pointer = %p, Value = %d\n", ptr, *ptr);
    str := string_test();
    printf("%s - Hello world %d, %d, %d\n", str, 1, 2, b);
    sum_test();
    // overflow_test();

    printf("Initial array values %d, %p\n", global_array.length, global_array.data);
    printf("Initial PolyStruct values %d, %.2f\n", global_poly_struct.field1, global_poly_struct.field2);
    printf("Initial MyStruct values %d, %.2f, {%d, %d}\n", global_struct.field, global_struct.something, global_struct.subValue.something, global_struct.subValue.foo);
    set_global(8);
    printf("'global_a' = %d\n", global_a);
    printf("'global_b' = %d\n", global_b);

    poly_test();

    state := current_state(7);
    printf("Current state - %d\n", state);
    if state == State.Running then printf("The state is Running\n");
    else if state != State.Running then printf("The state is not Running\n");

    null_val: int* = null;
    null_val = null;
    test_int := 7;
    null_test(&test_int);
    null_test(null);

    default_args(8);
    default_args();
    default_args_enum(State.Stopping);
    default_args_enum();

    sdl_video := 0x20;
    x := 0xfeABD4;
    // sdl := SDL_Init(sdl_video);
    // SDL_CreateWindow("Hello world", 805240832, 805240832, 400, 300, 0);
    // sleep(5);

    z := 1;
    each i in create_array(5) {
        printf("Value %d = %d\n", z++, i);
    }

    compiler_directives();
    open_window();

    print_type_info(MyStruct);
    print_type_info(u8);
    print_type_info(Array<string>);
    print_type_info(PolyStruct<int*, float>);
    print_type_info(PolyStruct<Array<int>, float>);
    print_type_info(s32*);
    print_type_info(State);
    print_type_info(my_struct.subValue);
    print_type_info(string);
    print_type_info(bar);

    type_casts();

    break_and_continue();
}

// TODO Make these work
// global_c := global_b;
global_struct: MyStruct;
global_poly_struct: PolyStruct<int, float64>;
global_array: Array<int> = [1, 2, 3, 5]
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
    field: u8;
    something := 4.2;
    subValue: OtherStruct = { foo = 8; }
}

struct OtherStruct {
    something := 5;
    foo: int;
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
    j := g + i;
    k := e > e;
    return d;
}

struct ArrayStruct {
    something := 5;
    array: Array<float64> = [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]
}

Array<int> create_array(int count) {
    array: Array<int>[count];
    each i in 0..count {
        array[i] = i * 5;
    }
    a := array[count-2];
    array[0]++;
    ++array[0];

    manipulate_array_struct();
    return array;
}

manipulate_array_struct() {
    s: ArrayStruct;
    s.array[0] = 8.9;
    ++s.array[0];
    each i in s.array {
        printf("%.2f, ", i);
    }
    printf("\n");
}

struct Node {
    value: int;
    next: Node*;
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

    array: Array<int>[8];
    array_ptr := &array[4];
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
    sum_array: Array<int> = [4, 41, 544, 244, 42, 14, 23]
    // print_type_info(sum); // Does not compile

    printf("Sum of Array  = %d\n", sum(sum_array));
    printf("Sum of Params = %d\n", sum(4, 41, 544, 244, 42, 14, 23));
}

int sum(Array<int> args) {
    sum := 0;
    each i in args then sum += i;
    return sum;
}

int sum(Params<int> args) {
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
    field1: T;
    field2: U;
}

poly_test() {
    a: PolyStruct<int, float>;
    a.field1 = 87;
    a.field2 = 3.14159;
    printf("%d, %f\n", a.field1, a.field2);
}

enum State {
    Stopped;
    Stopping;
    Starting = 5;
    Running;
}

struct StateStruct {
    state := State.Running;
    something: int;
    flag := true;
}

State current_state(int a) {
    state: StateStruct;
    printf("State is %d\n", state.state);

    if a > 5 then return State.Running;
    else if a == 5 then return State.Starting;
    return State.Stopped;
}

null_test(int* value_ptr) {
    if value_ptr == null then printf("Pointer is null\n");
    else then printf("Pointer value is %d\n", *value_ptr);
}

default_args(int val = 5) {
    printf("Value = %d\n", val);
}

default_args_enum(State val = State.Running) {
    printf("Enum value = %d, Running = %d\n", val, val == State.Running);
}

open_window() {
    #if os == OS.Linux {
        XOpenDisplay("Hello");
        printf("Opening X11 window\n");
    }
}

int SDL_Init(u32 flags) #extern "SDL2"
SDL_CreateWindow(string title, int x, int y, int w, int h, u32 flags) #extern "SDL2"
u32 sleep(u32 seconds) #extern "libc"

#run {
    args: Array<string> = ["Hello world"]
    main();

    build();
}

compiler_directives() {
    const_value := 7; #const
    printf("Constant value = %d\n", const_value);
    #assert factorial(6) == 720;
    #if build_env == BuildEnv.Debug then
        printf("Running Debug code\n");
    #if build_env == BuildEnv.Release then
        printf("Running Release code\n");
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

print_type_info(Type type) {
    type_info := type_of(type);
    printf("Type Name = %s, Type Kind = %d, Type Size = %d, Field Count = %d\n", type_info.name, type_info.type, type_info.size, type_info.fields.length);
    each field in type_info.fields then
        printf("Field name = %s, Field offset = %d, Field type name = %s\n", field.name, field.offset, field.type_info.name);
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

break_and_continue() {
    a := 0;
    b := 0;
    while a < 100 {
        a += 10;
        b += 4;
        if (b > 10) then break;
    }

    each i in 1..10 {
        if (i > 5) then break;
        if (i > 3) {
            printf("Special Value = %d\n", i);
            continue;
        }
        printf("Value = %d\n", i);
    }
}
