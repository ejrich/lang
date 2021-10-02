main() {
    basics();

    string_compare();

    string_indexing();
}

basics() {
    a := "Hello sailor!";
    b := "Hello world!"; #const

    printf("a: length = %d, value = %s\n", a.length, a);
    printf("b: length = %d, value = %s\n", b.length, b.data);
}

string_compare() {
    a := "Hello world";
    b := "Hello world!";

    assert(!(a == b));
    assert(a != b);

    assert("123456" == "123456", "Numbers are not equal");
    assert("123456" != "123457", "Numbers are equal");

    printf("Assertions passed\n");
}

string_indexing() {
    a := "Hello world!";
    a[2] = 'e';

    printf("%s\n", a);
    assert(a == "Heelo world!");

    b: StringStruct = {foo = 12; bar = "Hey what's up";}
    b.bar[2] = 'e';

    printf("%s\n", b.bar);
    assert(b.bar == "Hee what's up");
}

struct StringStruct {
    foo: int;
    bar: string;
}

#run main();
