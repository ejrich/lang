#import standard
#import compiler

main() {
    print("Hello world\n");

    i := 0;

    #insert return macro_with_code("print(\"i = %\n\", i++)");
}

#run {
    // TODO Add additional code in main to print something
    // main_function := get_function("main");

    // insert_code(main_function.body, macro(main_function.name));
}

macro(string message) {
    print(message);
}

string macro_with_code(string code) {
    return format_string(
    "each i in 0..4 {
        %
    }", code);
}
