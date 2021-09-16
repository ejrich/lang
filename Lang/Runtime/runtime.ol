// Runtime library with types and main function

// Runtime structs
struct List {
    int length;
    int* data; // TODO Make this polymorphic
}

/* @Future Update strings to use this struct
struct string {
    int length;
    u8* data;
}
*/


// Basic IO functions
printf(string format, ... args) #extern
