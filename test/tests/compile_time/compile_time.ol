#import standard
#import compiler

main() {
    print("Hello world\n");

    i := 0;
    // macro_with_code(print("i = %\n", i++));
}

#run {
    // TODO Add additional code in main to print something
    // main_function := get_function("main");

    // insert_code(main_function.body, macro(main_function.name));
}

macro(string message) #macro {
    print(message);
}

macro_with_code(Code code) #macro {
    each i in 0..4 {
        #insert code;
    }
}
