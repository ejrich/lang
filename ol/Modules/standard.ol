// This module contains the standard library for the language

#if os == OS.Linux
    #import linux


// Assertions
assert(bool assertion, int exit_code = 1) {
    if assertion return;

    printf("Assertion failed\n");

    #if os == OS.Linux {
        exit_group(exit_code);
    }
}

assert(bool assertion, string message, int exit_code = 1) {
    if assertion return;

    if message.length == 0 printf("Assertion failed\n");
    else printf("Assertion failed: %s\n", message);

    #if os == OS.Linux {
        exit_group(exit_code);
    }
}


// Memory operations
void* mem_copy(void* dest, void* src, int length) {
    dest_buffer := cast(u8*, dest);
    src_buffer := cast(u8*, src);

    each i in 0..length-1 {
        dest_buffer[i] = src_buffer[i];
    }

    return dest;
}


// Sleep and timing related functions
sleep(int milliseconds) {
    if milliseconds < 0 return;

    seconds := milliseconds / 1000;
    ms := milliseconds % 1000;

    #if os == OS.Linux {
        ts: Timespec = { tv_sec = seconds; tv_nsec = ms * 1000000; }
        nanosleep(&ts, &ts);
    }
}

yield() {
    #if os == OS.Linux {
        sched_yield();
    }
}


// Functions for printing and formatting strings
print(string format, Params args) {
    // TODO Implement me
}
