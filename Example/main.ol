int main(List<string> args) {
    // Return positive exit code
    /*
        Multi line comment
    */
    each arg in args then printf("Arg: %s\n", arg);
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
    prim := primitives();
    list := create_list(4);
    ptr := *pointers();
    str := string_test();
    printf("%s - Hello world %d, %d, %d\n", str, 1, 2, b);
    sum_test();
    //overflow_test();

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

    sdl_video := 0x20;
    x := 0xfeABD4;
    sdl := SDL_Init(sdl_video);
    SDL_CreateWindow("Hello world", 805240832, 805240832, 400, 300, 0);
    sleep(5);

    return 0;
}

global_a := 7;
global_b: int;

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
        //printf("%d\n", a);
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

null_test(int* value_ptr) {
    if value_ptr == null then printf("Pointer is null\n");
    else then printf("Pointer value is %d\n", *value_ptr);
}

default_args(int val = 5) {
    printf("Value = %d\n", val);
}

int SDL_Init(u32 flags) #extern "SDL2"
SDL_CreateWindow(string title, int x, int y, int w, int h, u32 flags) #extern "SDL2"
u32 sleep(u32 seconds) #extern "libc"

//#run default_args(87);
#run printf("Hello world = %d\n", 87);

/*
compiler_directives() {
    #if true then
        printf("Hello world");
    else then
        printf("Hello wrong branch");
}*/
