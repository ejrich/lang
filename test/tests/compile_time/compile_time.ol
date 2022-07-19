#import standard
#import compiler

main() {
    print("Hello world\n");
}

#run {
    // TODO Add additional code in main to print something
    main_function := get_function("main");
}

macro(string message) #macro {
    print(message);
}
