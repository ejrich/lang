#import standard
#import compiler

main() {
    print("Hello world\n");

    i := 0;
    #insert return macro_with_code("print(\"i = %\\n\", i++)");

    bar();
}

#run {
    add_code("message := \"Hello from codegen\\n\";\nfoo() {\n    print(message);\n}");

    main();

    if intercept_compiler_messages() {
        message: CompilerMessage;

        while get_next_compiler_message(&message) {
            // print("Hello world %\n", message.value);
            sleep(10);
        }
    }

    main_function := get_function("main");
    insert_code(main_function, format_string("print(\"Executing function: %\\n\");\nfoo();", main_function.name));

    main();
}

bar() #print_ir {
    message: CompilerMessage;
    print("Hello world %\n", message.value);
}

string macro_with_code(string code) {
    return format_string("each j in 0..4 {\n    %;\n}", code);
}
