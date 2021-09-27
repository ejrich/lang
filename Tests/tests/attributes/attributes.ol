main() {
    a := foo();
}

[something]
int foo() {
    return 8;
}

[multiple, attributes]
int bar() {
    return 20;
}

#run main();
