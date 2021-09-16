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
    printf("Argument count - %d\nFirst arg - %s\n", argc, *argv);
    return 0;
}
