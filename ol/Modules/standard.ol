// This module contains the standard library for the language

#if os == OS.Linux
    #import linux


// Array support functions
ARRAY_BLOCK_SIZE := 10; #const

array_insert<T>(Array<T>* array, T value, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    // Reallocate the array if necessary
    length := array.length;
    if length % ARRAY_BLOCK_SIZE == 0 {
        new_blocks := length / ARRAY_BLOCK_SIZE + 1;
        element_size := size_of(T);

        if length {
            array.data = reallocator(array.data, element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
        else {
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

array_reserve<T>(Array<T>* array, int length, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    if array.length >= length || length <= 0 return;

    reserve_length := length + ARRAY_BLOCK_SIZE - length % ARRAY_BLOCK_SIZE;
    element_size := size_of(T);

    if array.length {
        array.data = reallocator(array.data, element_size * reserve_length);
    }
    else {
        array.data = allocator(element_size * reserve_length);
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
void* default_allocator(int size) {
    return malloc(size);
}

void* default_reallocator(void* pointer, int size) {
    return realloc(pointer, size);
}

default_free(void* data) {
    free(data);
}
// TODO Remove these functions once the above functions have been implemented
void* malloc(int size) #extern "c"
void* realloc(void* pointer, int size) #extern "c"
free(void* data) #extern "c"

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

u64 get_performance_counter() {
    counter: u64;

    #if os == OS.Linux {
        now: Timespec;

        clock_gettime(ClockId.CLOCK_MONOTONIC_RAW, &now);
        counter += now.tv_sec * 1000000000 + now.tv_nsec;
    }

    return counter;
}


// Functions for printing and formatting strings
print(string format, Params args) {
    // TODO Implement me
}


// File and IO functions and types
struct FILE {}
struct DIR {}

D_NAME_LENGTH := 256; #const

struct dirent {
    d_ino: u64;
    d_off: u64;
    d_reclen: u16;
    d_type: FileType;
    d_name: CArray<u8>[D_NAME_LENGTH];
}

enum FileType : u8 {
    DT_UNKNOWN = 0;
    DT_FIFO = 1;
    DT_CHR = 2;
    DT_DIR = 4;
    DT_BLK = 6;
    DT_REG = 8;
    DT_LNK = 10;
    DT_SOCK = 12;
    DT_WHT = 14;
}

// Directory operations
DIR* opendir(string dirname) #extern "c"
int closedir(DIR* dir) #extern "c"
dirent* readdir(DIR* dir) #extern "c"

// File operations
FILE* fopen(string file, string type) #extern "c"
int fseek(FILE* file, s64 offset, int origin) #extern "c"
s64 ftell(FILE* file) #extern "c"
int fread(void* buffer, u32 size, u32 length, FILE* file) #extern "c"
int fclose(FILE* file) #extern "c"

bool, string read_file(string file_path, Allocate allocator = default_allocator) {
    file_contents: string;
    found: bool;

    file := fopen(file_path, "r");
    if file {
        found = true;
        fseek(file, 0, 2);
        size := ftell(file);
        fseek(file, 0, 0);

        file_contents = { length = size; data = allocator(size); }

        fread(file_contents.data, 1, size, file);
        fclose(file);
    }

    return found, file_contents;
}
