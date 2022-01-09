// This module contains the standard library for the language

#if os == OS.Linux
    #import linux


// Assertions
assert(bool assertion, int exit_code = 1) {
    if assertion return;

    printf("Assertion failed\n");
    exit(exit_code);
}

assert(bool assertion, string message, int exit_code = 1) {
    if assertion return;

    if message.length == 0 printf("Assertion failed\n");
    else printf("Assertion failed: %s\n", message);
    exit(exit_code);
}


// Memory operations
void* mem_copy(void* dest, void* src, int length) #print_ir {
    dest_buffer := cast(u8*, dest);
    src_buffer := cast(u8*, src);

    each i in 0..length-1 {
        dest_buffer[i] = src_buffer[i];
    }

    return dest;
}
