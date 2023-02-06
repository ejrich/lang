#import standard
#import compiler

main() {
    print("Hello world\n");

    i := 0;
    #insert return macro_with_code("print(\"i = %\\n\", i++)");
}

#run {
    add_code("message := \"Hello from codegen\\n\";\nfoo() {\n    print(message);\n}");

    main();

    main_function := get_function("main");
    insert_code(main_function, format_string("print(\"Executing function: %\\n\");\nfoo();", main_function.name));

    main();
}

macro(string message) {
    print(message);
}

string macro_with_code(string code) {
    return format_string("each j in 0..4 {\n    %;\n}", code);
}
