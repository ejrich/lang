foobar_2() {
    printf("Printing from yet another file\n");
    foobaz(1000);

    test: Test = { foo = 12; bar = 3.14; }
    poly: PolyStruct2<int, int>;
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
