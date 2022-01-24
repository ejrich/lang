#import standard
#import compiler
#import math
#import "import.ol"
#import "import2.ol"

main() {
    basic();

    ptr := pointers();
    print("Pointer = %, Value = %\n", ptr, *ptr);
    *ptr = 76;
    print("Pointer = %, Value = %\n", ptr, *ptr);

    str := string_test();
    print("% - Hello world %, %\n", str, 1, 2);

    sum_test();

    // Don't run this at compile time
    // overflow_test();

    globals();

    poly_test();

    enums();

    nulls();

    default_args(8);
    default_args();

    arrays();

    compiler_directives();

    type_information();

    type_casts();

    shifts();

    break_and_continue();

    any_args();

    multiple_return_values();

    imports();
}

basic() {
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
    i := b % 8;
    j := float_mod(a, 2.0);
    print("Modulus outputs: i = %, j = %\n", i, j);
    fac6 := factorial(6);
    print("%! = %\n", 6, fac6);
    my_struct := create();
    print("my_struct: field = %, something = %, subvalue.something = %\n", my_struct.field, my_struct.something, my_struct.subValue.something);
    call_field := create().something;
    print("call_field = %\n", call_field);

    prim := primitives();
}

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
    subValue: OtherStruct;
}

struct OtherStruct {
    something := 5;
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

struct ArrayStruct {
    something := 5;
    array: Array<float64>[6];
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
    print("%\n", s.array[0]);
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

    print("Sum of Array   = %\n", sum(sum_array));
    print("Sum of Params = %\n", sum2(4, 41, 544, 244, 42, 14, 23));
}

int sum(Array<int> args) {
    sum := 0;
    each i in args sum += i;
    return sum;
}

int sum2(Params<int> args) {
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

globals() {
    print("Initial array values %, %\n", global_array.length, global_array.data);
    print("Initial PolyStruct values %, %\n", global_poly_struct.field1, global_poly_struct.field2);
    print("Initial MyStruct values %, %, {%}\n", global_struct.field, global_struct.something, global_struct.subValue.something);

    set_global(8);
    print("'global_a' = %\n", global_a);
    print("'global_b' = %\n", global_b);
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

enums() {
    state := current_state(7);
    print("Current state - %\n", state);
    if state == State.Running print("The state is Running\n");
    else if state != State.Running print("The state is not Running\n");
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
    print("State is %\n", state.state);

    if a > 5 return State.Running;
    else if a == 5 return State.Starting;
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
    if value_ptr == null print("Pointer is null\n");
    else print("Pointer value is %\n", *value_ptr);
}

default_args(int val = 5) {
    print("Value = %\n", val);
}

arrays() {
    array := create_array(4);

    z := 1;
    each i in create_array(5) {
        print("Value % = %\n", z++, i);
    }
}

open_window() {
    #if os == OS.Linux {
        XOpenDisplay("Hello");
        print("Opening X11 window\n");
    }
}

compiler_directives() {
    const_value := 7; #const
    print("Constant value = %\n", const_value);
    #assert factorial(6) == 720;
    #if build_env == BuildEnv.Debug
        print("Running Debug code\n");
    #if build_env == BuildEnv.Release
        print("Running Release code\n");

    open_window();
}

#run {
    build();
    main();
}

build() {
    if os == OS.Linux {
        set_linker(LinkerType.Dynamic);
    }
}

#assert global_b > 0;

#if os == OS.Linux {
    XOpenDisplay(string name) #extern "X11"
}

type_information() {
    print_type_info(MyStruct);
    print_type_info(u8);
    print_type_info(Array<string>);
    print_type_info(PolyStruct<int*, float>);
    print_type_info(PolyStruct<Array<int>, float>);
    print_type_info(s32*);
    print_type_info(State);

    my_struct := create();
    print_type_info(my_struct.subValue);
    print_type_info(bar);
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
    print("a = %, b = %, c = %, d = %\n", a, b, c, d);
}

shifts() {
    // Standard shifting
    a := 34 << 3;
    b := 12345 >> 5;
    print("a = %, b = %\n", a, b);

    // Shifting negative numbers
    c := -13 << 2;
    d := -13 >> 2;
    print("c = %, d = %\n", c, d);

    // Rotating
    e: u32 = 123456789 <<< 7;
    f := 34 >>> 32; // Full rotation
    print("e = %, f = %\n", e, f);
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

imports() {
    foobar();
    foobar_2();
    // foobaz(); // This is a private function that should not compile
}

#run main();
