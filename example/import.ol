foobar() {
    printf("Printing from another file\n");
    foobaz(100);
}

#private

foobaz(int a = 10) {
    printf("Hello world - %d\n", a);
}
