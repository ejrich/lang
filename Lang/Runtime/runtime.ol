// Runtime library with types and main function

// Runtime structs
struct List<T> {
    int length;
    T* data;
}

/* @Future Update strings to use this struct
struct string {
    int length;
    u8* data;
}
*/


// Basic IO functions
printf(string format, ... args) #extern


// Runtime functions
int __start(int argc, string* argv) {
    args: List<string>[argc-1];

    each i in 1..argc-1 then args[i] = *(argv + i);

    return Main(args);

    /* @Future Add compile time execution to write the correct return
    #if function(Main).return == Type.Void {
        Main(args);
        return 0;
    }
    else {
        #if function(Main).arguments.length == 0 then return Main();
        else then return Main(args);
    }*/
}
