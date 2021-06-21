main() {
    string_compare();
}

string_compare() {
    a := "Hello world";
    b := "Hello world!";

    assert(!(a == b));
    assert(a != b);

    printf("Assertions passed\n");
}

#run main();
