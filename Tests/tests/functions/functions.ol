main() {
    function_overloads();

    default_arguments();

    polymorphic_functions();
}

function_overloads() {
    sum_list: Array<int>[7];
    sum_list[0] = 4;
    sum_list[1] = 41;
    sum_list[2] = 544;
    sum_list[3] = 244;
    sum_list[4] = 42;
    sum_list[5] = 14;
    sum_list[6] = 23;
    // type_of(sum); // Does not compile due to multiple overloads of 'sum'

    printf("Sum of Array  = %d\n", sum(sum_list));
    printf("Sum of Params = %d\n", sum(4, 41, 544, 244, 42, 14, 23));
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

default_arguments() {
    // Standard behavior
    foo(3);
    foo(4, 10);
    foo(7, 9, false);

    // Specifying arguments, can also move around non-default argument
    foo(5, c = false);
    foo(6, b = 7);
    foo(b = 7, a = 7);

    // Also works with params
    foo_params(3);
    foo_params(7, 10);
    foo_params(8, 9, 3.1, 2.8, 5.6);
}

foo(int a, s16 b = 8, bool c = true) {
    printf("a = %d, b = %d, c = %d\n", a, b, c);
}

foo_params(int a, int b = 8, Params<float> args) {
    sum: float;
    each i in args sum += i;
    printf("a = %d, b = %d, sum = %.2f\n", a, b, sum);
}

polymorphic_functions() {
    float_value := add_int(3.1, 2);
    int_value := add_int(35, 10);
    int_value2 := add_int(12, 10);
    printf("float_value = %.2f, should be 5.10\n", float_value);
    printf("int_value = %d, should be 45\n", int_value);

    int_list: Array<int>[8];
    string_list: Array<string>[8];
    baz(int_list, &float_value);
    baz(int_list, &int_value);
    baz(string_list, &int_value);

    thing := create<Thing>();
    create<Thing>();
    printf("thing.a = %d, thing.b = %.2f\n", thing.a, thing.b);

    a: PolyStruct<int> = { a = 2; }
    foobar(a, 1, 2, 3);
    foobar(a, 1.0, 2.0, 3.0);
}

T add_int<T>(T a, int b) {
    value: T;
    value += a + cast(T, b);
    return value;
}

baz<T, U>(Array<T> list, U* b) {
    #assert U == float || U == s32;
    #if U == float printf("b = %.2f\n", *b);
    else {
        if T == s32 printf("T is an int\n");
        else printf("T is not an int\n");
    }
}

struct Thing {
    a := 9;
    b: float64 = 3.2;
}

T create<T>() {
    thing: T;
    return thing;
}

struct PolyStruct<I> {
    a: I;
}

foobar<T, U>(PolyStruct<T> c, Params<U> args) {
    #if T == U {
        each arg in args {
            printf("Compare without casting: c.a == arg = %d, arg = %d\n", c.a == arg, arg);
        }
    }
    else {
        each arg in args {
            printf("Compare with casting: c.a == arg = %d, arg = %.2f\n", c.a == cast(T, arg), arg);
        }
    }
}

#run main();
