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

    if intercept_compiler_messages() {
        message: CompilerMessage;

        while get_next_compiler_message(&message) {
            switch message.type {
                case CompilerMessageType.ReadyToBeTypeChecked; {
                    function := cast(FunctionAst*, message.value.ast);
                    print("Ast is ready to be type checked: % - %\n", function.type, function.name);
                }
                case CompilerMessageType.TypeCheckSuccessful; {
                    function := cast(FunctionAst*, message.value.ast);
                    print("Ast was successfully type checked: % - %\n", function.type, function.name);
                }
                case CompilerMessageType.TypeCheckFailed;
                    print("Type checking failed\n");
                case CompilerMessageType.IRGenerated; {
                    function := cast(FunctionAst*, message.value.ast);
                    print("IR generated for function: %\n", function.name);
                }
                case CompilerMessageType.ReadyForCodeGeneration;
                    print("Ready for code generation\n");
                case CompilerMessageType.CodeGenerationFailed;
                    print("Code generation failed\n");
                case CompilerMessageType.CodeGenerated;
                    print("Successfully generated code: %\n", message.value.name);
                case CompilerMessageType.ExecutableLinked;
                    print("Successfully linked: %\n", message.value.name);
            }
        }
    }

    main();
}

string macro_with_code(string code) {
    return format_string("each j in 0..4 {\n    %;\n}", code);
}
