#import standard
#import compiler

main() {
    print("Hello world\n");

    foo();

    i := 0;
    #insert return macro_with_code("print(\"i = %\\n\", i++)");
}

#run {
    // TODO Add additional code in main to print something
    // main_function := get_function("main");

    add_code("message := \"Hello from codegen\\n\";\nfoo() {\n    print(message);\n}");
    // insert_code(main_function, format_string("print(\"%\\n\");", main_function.name));
}

#run main();

macro(string message) {
    print(message);
}

string macro_with_code(string code) {
    return format_string("each j in 0..4 {\n    %;\n}", code);
}
