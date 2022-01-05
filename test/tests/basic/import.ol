foobar() {
    printf("Printing from another file\n");
    foobaz(100);

    test: Test = { foo = 12; baz = 3.14; }
    print_type_info(Test);
    poly: PolyStruct2<int, int>;
    array: Array<Test>[5];

    using_test(test);
}

#private

foobaz(int a = 10) {
    printf("Hello world - %d\n", a);
}

struct Test {
    foo := 0;
    baz := 1.2;
}

struct PolyStruct2<T, U> {
    field1: T;
    field2: U;
}

using_test<T>(T foo) {
    foo.baz = 8.5;
    printf("foo.baz = %.2f\n", foo.baz);
}
