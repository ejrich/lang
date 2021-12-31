foobar_2() {
    printf("Printing from yet another file\n");
    foobaz(1000);
}

#private

foobaz(int a = 100) {
    printf("Hello world 2 - %d\n", a);
}
