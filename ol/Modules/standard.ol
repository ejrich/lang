// This module contains the standard library for the language

#if os == OS.Linux
    #import linux


// Array functions
ARRAY_BLOCK_SIZE := 10; #const

array_insert<T>(Array<T>* array, T value, Allocate allocator = null, Reallocate reallocator = null) {
    // Reallocate the array if necessary
    length := array.length;
    if length % ARRAY_BLOCK_SIZE == 0 {
        new_blocks := length / ARRAY_BLOCK_SIZE + 1;
        element_size := size_of(T);

        if length {
            if reallocator == null reallocator = realloc;

            array.data = reallocator(array.data, element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
        else {
            if allocator == null allocator = malloc;

            array.data = allocator(element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
    }

    array.data[length] = value;
    array.length++;
}

bool array_remove<T>(Array<T>* array, int index) {
    length := array.length;
    if index < 0 || index >= length {
        return false;
    }

    if index <= length - 1 {
        each i in index..length-2 {
            array.data[i] = array.data[i + 1];
        }
    }

    array.length--;
    return true;
}

array_reserve<T>(Array<T>* array, int length, Allocate allocator = null, Reallocate reallocator = null) {
    if array.length >= length || length <= 0 return;

    reserve_length := length + ARRAY_BLOCK_SIZE - length % ARRAY_BLOCK_SIZE;
    element_size := size_of(T);

    if array.length {
        if reallocator == null reallocator = realloc;

        array.data = realloc(array.data, element_size * reserve_length);
    }
    else {
        if allocator == null allocator = malloc;

        array.data = malloc(element_size * reserve_length);
    }

    array.length = length;
}


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
