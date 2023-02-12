#import compiler
#import standard
#import "import.ol"
#import "import2.ol"

main() { //#print_ir {
    // Return positive exit code
    /*
        Multi line comment
    */
    each arg, i in command_line_arguments print("Arg %: \"%\" -- length = %\n", i, arg, arg.length);
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
    print("%! = %, d = %, %, %, %, %, %, %\n", 6, fac6, d, &hello, State.Running, -1234567, 3.14, global_array, hello);
    my_struct := create();
    print("my_struct: %\n", my_struct);
    call_field := create().something;
    print("call_field = %\n", call_field);

    prim := primitives();
    array := create_array(4);
    ptr := pointers();
    print("Pointer = %, Value = %\n", ptr, *ptr);
    str := string_test();
    print("% - Hello world %, %, %\n", str, 1, 2, b);
    sum_test();
    // overflow_test();

    print("Initial array values %, %\n", global_array.length, global_array.data);
    print("Initial PolyStruct values %, %\n", global_poly_struct.field1, global_poly_struct.field2);
    print("Initial MyStruct values %, %, {%, %}\n", global_struct.field, global_struct.something, global_struct.subValue.something, global_struct.subValue.foo);
    set_global(8);
    print("'global_a' = %\n", global_a);
    print("'global_b' = %\n", global_b);
    print("'global_c' = %\n", global_c);

    poly_test();

    state := current_state(7);
    print("Current state - %\n", state);
    if state == State.Running print("The state is Running\n");
    else if state != State.Running print("The state is not Running\n");

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
    // sleep(5000);

    z := 1;
    each i in create_array(5) {
        print("Value % = %\n", z++, i);
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

    any_args();

    multiple_return_values();

    foobar();
    foobar_2();
    // foobaz(); // This is a private function that should not compile

    switch_statement();
}

global_c := global_b;
global_struct: MyStruct;
global_poly_struct: PolyStruct<int, float64>;
global_array: Array<int> = [1, 2, 3, 5]
global_a := 7;
global_b: int = 456; #const

set_global(int a) {
    if a > 10 global_a = a * 90;
}

bool test(int a) {
    if a == 3 return true;
    else return false;
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
    if a == 4 return a;
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
    if n == 0 return 1;
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
    each i in 0..count-1 {
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
        print("%, ", i);
    }
    print("\n");
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
        print("Value % = %\n", i++, loop_node.value);
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

    print("Sum of Array  = %\n", sum(sum_array));
    print("Sum of Params = %\n", sum(4, 41, 544, 244, 42, 14, 23));
}

int sum(Array<int> args) {
    sum := 0;
    each i in args sum += i;
    return sum;
}

int sum(Params<int> args) {
    sum := 0;
    each i in args sum += i;
    return sum;
}

overflow_test() {
    each i in 1..1000000 {
        a := i * 9;
        print("%\n", a);
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
    print("%, %\n", a.field1, a.field2);
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
    print("State is %\n", state);

    if a > 5 return State.Running;
    else if a == 5 return State.Starting;
    return State.Stopped;
}

null_test(int* value_ptr) {
    if value_ptr == null print("Pointer is null\n");
    else print("Pointer value is %\n", *value_ptr);
}

default_args(int val = 5) {
    print("Value = %\n", val);
}

default_args_enum(State val = State.Running) {
    print("Enum value = %, Running = %\n", val, val == State.Running);
}

open_window() {
    #if os == OS.Linux {
        XOpenDisplay("Hello");
        print("Opening X11 window\n");
    }
}

int SDL_Init(u32 flags) #extern "SDL2"
SDL_CreateWindow(string title, int x, int y, int w, int h, u32 flags) #extern "SDL2"

#run {
    array_insert(&command_line_arguments, "Hello world");
    main();

    build();
}

compiler_directives() {
    const_value := 7; #const
    print("Constant value = %\n", const_value);
    #assert factorial(6) == 720;
    #if build_env == BuildEnv.Debug
        print("Running Debug code\n");
    #if build_env == BuildEnv.Release
        print("Running Release code\n");
}

build() {
    set_executable_name("Example");

    if os == OS.Linux {
        set_linker(LinkerType.Dynamic);
    }
}

#assert global_b > 0;

#if os == OS.Linux {
    XOpenDisplay(string name) #extern "X11"
}

print_type_info(Type type) {
    type_info := type_of(type);
    print("Type Name = %, Type Kind = %, Type Size = %\n", type_info.name, type_info.type, type_info.size);

    if type_info.type == TypeKind.Struct {
        struct_type_info := cast(StructTypeInfo*, type_info);
        each field in struct_type_info.fields {
            print("Field name = %, Field offset = %, Field type name = %\n", field.name, field.offset, field.type_info.name);
        }
    }
    else if type_info.type == TypeKind.Enum {
        enum_type_info := cast(EnumTypeInfo*, type_info);
        each enum_value in enum_type_info.values {
            print("Enum value name = %, Value = %\n", enum_value.name, enum_value.value);
        }
    }
    else if type_info.type == TypeKind.Function {
        function_type_info := cast(FunctionTypeInfo*, type_info);
        print("Return type = %\n", function_type_info.return_type.name);
        each arg in function_type_info.arguments {
            print("Argument name = %, Argument type name = %, Argument type kind = %\n", arg.name, arg.type_info.name, arg.type_info.type);
        }
    }
}

type_casts() {
    a: u64 = 0xFFFFFFFFFFFF0000;
    b := cast(s32, a);
    c := cast(float64, a);
    d := cast(u8, State.Running);
    print("a = %, b = %, c = %, d = %\n", uint_format(a, 16), b, float_format(c, 10), d);
}

break_and_continue() {
    a := 0;
    b := 0;
    while a < 100 {
        a += 10;
        b += 4;
        if b > 10 break;
    }

    each i in 1..10 {
        if i > 5 break;
        if i > 3 {
            print("Special Value = %\n", i);
            continue;
        }
        print("Value = %\n", i);
   }
}

any_args() {
    print_value("Integer value = %\n", 8);
    print_value("Float value = %\n", 3.14);

    foo := 9;
    print_value("Pointer value = %\n", &foo);

    my_struct: MyStruct;
    print_value("Other value\n", my_struct);
}

print_value(string format, Any arg) {
    if arg.type.type == TypeKind.Integer {
        value := cast(int*, arg.data);
        print(format, *value);
    }
    else if arg.type.type == TypeKind.Float {
        value := cast(float*, arg.data);
        print(format, *value);
    }
    else if arg.type.type == TypeKind.Pointer {
        print(format, arg.data);
    }
    else print(format);
}

multiple_return_values() {
    a: int;
    b: bool;

    a, b = number_is_correct(12);
    print("Number = %, Correct = %\n", a, b);

    c, d := number_is_correct(6);
    print("Number = %, Correct = %\n", c, d);

    e, f: int;
    e, f, b = hello_world();
}

int, bool number_is_correct(int a) {
    if a > 10 {
        return a, true;
    }
    return a * 10, false;
}

int, int, bool hello_world() {
    return 1, 2, true;
}

switch_statement() {
    result := foo();
    switch result {
        case 1;
        case 2;
            return;
        case 3;
            print("Value is 3\n");
        default;
            print("Foo value = %\n", result);
    }
}
