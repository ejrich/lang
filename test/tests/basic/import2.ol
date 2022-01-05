foobar_2() {
    printf("Printing from yet another file\n");
    foobaz(1000);

    test: Test = { foo = 12; bar = 3.14; }
    print_type_info(Test);
    poly: PolyStruct2<int, int>;
    array: Array<Test>[5];

    using_test(test);
}

#private

foobaz(int a = 100) {
    printf("Hello world 2 - %d\n", a);
}

struct Test {
    foo := 0;
    bar := 1.2;
}

struct PolyStruct2<T, U> {
    field1: T;
    field2: U;
}

using_test<T>(T foo) {
    foo.bar = 8.5;
    printf("foo.bar = %.2f\n", foo.bar);
}
