int Main(List<string> args) {
    // Return positive exit code
    /*
        Multi line comment
    */
    //hello := "This is an \"escaped\" string literal";
    a := 4.2;
    a++;
    baz := foo() + bar(3 + 1);//, "Hello", 1.2);
    b := 6 * (4 - 1);
    c := 2;
    d := a + 1 == b + 2 && (1 + b) == 2 || b > 3 + 4 * c - 1;
    e := !d;
    f := test(3);
    g := test_each();
    h := 3 > a;
    fac6 := factorial(6);
    my_struct := create();
    prim := primitives();
    list := create_list(4);
    return prim;
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

int bar(int a) {//, string b, float c) {
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
    List<int>[6] list;
}

List<int> create_list(int count) {
    list: List<int>[count];
    each i in 1..count {
        list[i] = i;
    }
    a := list[1];
    s: ListStruct;
    s.list[0] = 8;
    return list;
}
