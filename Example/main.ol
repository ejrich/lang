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
    return 8;
}

int bar(int a, string b, float c) {
    if a == 4 then return a;
    else if a == 5 {
        return 9;
    }
    return a + 3;
}
