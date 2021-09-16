int Main(List<string> args) {
    // Return positive exit code
    /*
        Multi line comment
    */
    var hello = "This is an \"escaped\" string literal";
    var a = 4.2;
    var baz = foo() + bar(3 + 1, "Hello", 1.2);
    var b = 6 * (4 - 1);
    return 0;
}

int foo() {
    var a = 0;
    a++;
    ++a;
    var b = --a + 1;
    while a < 10 {
        a = a + 1;
    }
    return a;
}

int bar(int a, string b, float c) {
    if a == 4 then return a;
    else if a == 5 {
        return 9;
    }
    return a + 3;
}

/*int baz() {
    var a = 1;
    each i in 1..10 {
        a = a + i;
    }
    return a;
}*/

int fib(int n) {
    if n == 1 then return 1;
    return n * fib(n - 1);
}
