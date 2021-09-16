main() {
    string_compare();

    string_indexing();
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

    a[2] = 0x65; // TODO Char syntax

    printf("%s\n", a);
    assert(a == "Heelo world!");

    b: StringStruct = {foo = 12; bar = "Hey what's up";}
    b.bar[2] = 0x65; // TODO Char syntax

    printf("%s\n", b.bar);
    assert(b.bar == "Hee what's up");
}

struct StringStruct {
    int foo;
    string bar;
}

#run main();
