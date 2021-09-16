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
    args: List<string>[argc];

    each i in 0..argc-1 then args[i] = *(argv + i);

    return Main(args);
}
