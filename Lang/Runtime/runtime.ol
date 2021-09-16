// Runtime library with types and main function

// Runtime structs
struct List<T> {
    int length;
    T* data;
}

// @Future Update strings to use this struct
struct string {
    int length;
    u8* data;
}


// Basic IO functions
printf(string format, ... args) #extern "libc"


// Runtime functions
int __start(int argc, string* argv) {
    args: List<string>[argc-1];

    each i in 1..argc-1 then args[i-1] = *(argv + i);

    //return __main(args);
    return main();

    /* @Future Add compile time execution to write the correct return
    #if function(__main).return == Type.Void {
        main(args);
        return 0;
    }
    else {
        #if function(main).arguments.length == 0 then return main();
        else then return main(args);
    }*/
}
