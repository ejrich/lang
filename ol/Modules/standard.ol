// This module contains the standard library for the language

#if os == OS.Linux {
    #import linux
}


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

    print("Assertion failed\n");

    exit_program(exit_code);
}

assert(bool assertion, string message, int exit_code = 1) {
    if assertion return;

    if message.length == 0 print("Assertion failed\n");
    else print("Assertion failed: %\n", message);

    exit_program(exit_code);
}

exit_program(int exit_code) {
    run_exit_callbacks();

    #if os == OS.Linux {
        exit_group(exit_code);
    }
}


// Memory operations
void* allocate_memory(int size) {
    pointer: void*;
    size = 4096;
    #if os == OS.Linux {
        pointer = mmap(null, size, Prot.PROT_NONE, MmapFlags.MAP_PRIVATE | MmapFlags.MAP_ANONYMOUS | MmapFlags.MAP_NORESERVE, 0, 0);
    }
    print("%\n", size);
    print("% - %\n", pointer, size);

    return pointer;
}

void* default_allocator(int size) {
    // The default allocator is a crude allocation method that will always request memory from the operating system
    pointer := allocate_memory(size);

    allocation: DefaultAllocation = { size = size; pointer = pointer; }

    if default_allocations.length == 0 {
        default_allocations.length = 10;
        default_allocations.data = allocate_memory(10 * size_of(DefaultAllocation));
        default_allocations[0] = allocation;
    }
    else {
        resize := true;
        each alloc, i in default_allocations {
            if alloc.pointer == null {
                default_allocations[i] = allocation;
                resize = false;
                break;
            }
        }
        if resize {
            new_pointer: DefaultAllocation*;
            old_size := default_allocations.length * size_of(DefaultAllocation);
            #if os == OS.Linux {
                new_pointer = mremap(default_allocations.data, old_size, (default_allocations.length + 10) * size_of(DefaultAllocation), MremapFlags.MREMAP_MAYMOVE);
            }

            if new_pointer != default_allocations.data {
                memory_copy(new_pointer, default_allocations.data, old_size);
                #if os == OS.Linux {
                    munmap(default_allocations.data, old_size);
                }

                default_allocations.data = new_pointer;
            }

            default_allocations.length += 10;
        }
    }

    return pointer;
}

void* default_reallocator(void* pointer, int size) {
    each allocation in default_allocations {
        if allocation.pointer == pointer {
            // Try to reallocate the memory from the os
            new_pointer: void*;
            #if os == OS.Linux {
                new_pointer = mremap(pointer, allocation.size, size, MremapFlags.MREMAP_MAYMOVE);
            }

            // If reallocation returns a new pointer:
            // - Copy the data from the old pointer to the new pointer
            // - and free the old pointer and clear out the old allocation
            if new_pointer != pointer {
                memory_copy(new_pointer, pointer, allocation.size);
                #if os == OS.Linux {
                    munmap(pointer, allocation.size);
                }

                allocation.pointer = new_pointer;
            }

            allocation.size = size;
            return new_pointer;
        }
    }

    // If the pointer in not in the list of allocations, exit
    assert(false, "Pointer not found in list of allocations\n");
    return pointer;
}

default_free(void* data) {
    each allocation in default_allocations {
        if allocation.pointer == data {
            #if os == OS.Linux {
                munmap(allocation.pointer, allocation.size);
            }

            allocation.pointer = null;
        }
    }
}

struct DefaultAllocation {
    size: u64;
    pointer: void*;
}

default_allocations: Array<DefaultAllocation>;

void* memory_copy(void* dest, void* src, int length) {
    dest_buffer := cast(u8*, dest);
    src_buffer := cast(u8*, src);

    each i in 0..length-1 {
        dest_buffer[i] = src_buffer[i];
    }

    return dest;
}


// Sleep and timing related functions
sleep(int milliseconds) {
    if milliseconds <= 0 return;

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
    if args.length == 0 {
        write_buffer_to_standard_out(format.data, format.length);
        return;
    }

    buffer: StringBuffer;
    format_string_arguments(&buffer, format, args);
    write_buffer_to_standard_out(&buffer.buffer, buffer.length);
}

string format_string(string format, Allocate allocator = default_allocator, Params args) {
    buffer: StringBuffer;
    format_string_arguments(&buffer, format, args);

    value: string = { length = buffer.length; data = allocator(buffer.length + 1); }
    memory_copy(value.data, &buffer.buffer, buffer.length);
    value[buffer.length] = 0; // @Cleanup Make this just length
    return value;
}

format_string_arguments(StringBuffer* buffer, string format, Array<Any> args) {
    arg_index := 0;

    each i in 0..format.length-1 {
        char := format[i];
        if char == '%' {
            if arg_index < args.length {
                arg := args[arg_index++];
                write_value_to_buffer(buffer, arg.type, arg.data);
            }
        }
        else {
            buffer.buffer[buffer.length++] = char;
        }
    }
}

string_buffer_max_length := 1024; #const

struct StringBuffer {
    length: s64;
    buffer: CArray<u8>[string_buffer_max_length];
}

write_buffer_to_standard_out(u8* buffer, s64 length) {
    #if os == OS.Linux {
        write(stdout, buffer, length);
    }
}

write_value_to_buffer(StringBuffer* buffer, TypeInfo* type, void* data) {
    if data == null {
        add_to_string_buffer(buffer, "null");
        return;
    }

    type_kind := type.type;

    if type_kind == TypeKind.Void {
        add_to_string_buffer(buffer, "void");
    }
    else if type_kind == TypeKind.Boolean {
        value := *cast(bool*, data);
        if value add_to_string_buffer(buffer, "true");
        else add_to_string_buffer(buffer, "false");
    }
    else if type_kind == TypeKind.Integer {
        type_info := cast(IntegerTypeInfo*, type);
        size := type_info.size;
        if type_info.signed {
            if size == 1 write_integer<s8>(buffer, data);
            else if size == 2 write_integer<s16>(buffer, data);
            else if size == 4 write_integer<s32>(buffer, data);
            else write_integer<s64>(buffer, data);
        }
        else {
            if size == 1 write_integer<u8>(buffer, data);
            else if size == 2 write_integer<u16>(buffer, data);
            else if size == 4 write_integer<u32>(buffer, data);
            else write_integer<u64>(buffer, data);
        }
    }
    else if type_kind == TypeKind.Float {
        if type.size == 4 write_float<float>(buffer, data);
        else write_float<float64>(buffer, data);
    }
    else if type_kind == TypeKind.String {
        value := *cast(string*, data);
        add_to_string_buffer(buffer, value);
    }
    else if type_kind == TypeKind.Pointer {
        write_pointer_to_buffer(buffer, data);
    }
    else if type_kind == TypeKind.Array {
        type_info := cast(StructTypeInfo*, type);
        pointer_field := type_info.fields[1];
        pointer_type_info := cast(PointerTypeInfo*, pointer_field.type_info);
        element_type := pointer_type_info.pointer_type;

        array := cast(Array<void*>*, data);
        write_array_to_buffer(buffer, element_type, array.data, array.length);
    }
    else if type_kind == TypeKind.CArray {
        type_info := cast(CArrayTypeInfo*, type);
        element_type := type_info.element_type;

        write_array_to_buffer(buffer, element_type, data, type_info.length);
    }
    else if type_kind == TypeKind.Enum {
        type_info := cast(EnumTypeInfo*, type);
        value: s32;
        if type_info.size == 1 value = *cast(s8*, data);
        else if type_info.size == 2 value = *cast(s16*, data);
        else if type_info.size == 4 value = *cast(s32*, data);
        else value = *cast(s64*, data);

        flags := false;
        each attribute in type_info.attributes {
            if attribute == "flags" {
                flags = true;
                break;
            }
        }

        found := false;
        if flags {
            each enum_value in type_info.values {
                if enum_value.value & value {
                    if found add_to_string_buffer(buffer, " | ");
                    else found = true;

                    add_to_string_buffer(buffer, enum_value.name);
                }
            }
        }
        else {
            each enum_value in type_info.values {
                if enum_value.value == value {
                    add_to_string_buffer(buffer, enum_value.name);
                    found = true;
                    break;
                }
            }
        }

        if !found {
            if value == 0 {
                buffer.buffer[buffer.length++] = '0';
            }
            else {
                if value < 0 {
                    buffer.buffer[buffer.length++] = '-';
                    value *= -1;
                }
                write_integer_to_buffer(buffer, value);
            }
        }
    }
    else if type_kind == TypeKind.Struct {
        type_info := cast(StructTypeInfo*, type);
        write_struct_to_buffer(buffer, type_info, data);
    }
    else if type_kind == TypeKind.Union {
        type_info := cast(UnionTypeInfo*, type);
        add_to_string_buffer(buffer, "{ ");

        each field in type_info.fields {
            add_to_string_buffer(buffer, field.name);
            add_to_string_buffer(buffer, ": ");
            write_value_to_buffer(buffer, field.type_info, data);
            add_to_string_buffer(buffer, ", ");
        }

        buffer.buffer[buffer.length-2] = ' ';
        buffer.buffer[buffer.length-1] = '}';
    }
    else if type_kind == TypeKind.Interface {
        write_pointer_to_buffer(buffer, data);
    }
    else if type_kind == TypeKind.Type {
        value := *cast(s32*, data);
        type_info := __type_table[value];
        add_to_string_buffer(buffer, type_info.name);
    }
    else if type_kind == TypeKind.Any {
        type_info := cast(StructTypeInfo*, type);
        write_struct_to_buffer(buffer, type_info, data);
    }
    else if type_kind == TypeKind.Compound {
        // @Cleanup This branch should not be hit
    }
    else if type_kind == TypeKind.Function {
        type_info := cast(FunctionTypeInfo*, type);
        add_to_string_buffer(buffer, type_info.name);
    }
}

add_to_string_buffer(StringBuffer* buffer, string value) {
    each i in 0..value.length-1 {
        buffer.buffer[buffer.length++] = value[i];
    }
}

write_integer<T>(StringBuffer* buffer, void* data) {
    value := *cast(T*, data);
    if value == 0 {
        buffer.buffer[buffer.length++] = '0';
    }
    else {
        if value < 0 {
            buffer.buffer[buffer.length++] = '-';
            value *= -1;
        }
        write_integer_to_buffer(buffer, value);
    }
}

write_float<T>(StringBuffer* buffer, void* data) {
    value := *cast(T*, data);
    whole := cast(s64, value);
    if whole == 0 {
        buffer.buffer[buffer.length++] = '0';
    }
    else {
        if value < 0 {
            buffer.buffer[buffer.length++] = '-';
            value *= -1;
        }
        write_integer_to_buffer(buffer, whole);
    }
    buffer.buffer[buffer.length++] = '.';
    value -= whole;

    each x in 0..3 {
        value *= 10;
        digit := cast(u8, value);
        buffer.buffer[buffer.length++] = digit + '0';
        value -= digit;
    }
}

write_integer_to_buffer(StringBuffer* buffer, u64 value) {
    buffer_length := buffer.length;
    length := 0;
    while value > 0 {
        digit := value % 10;
        buffer.buffer[buffer.length++] = digit + '0';
        value /= 10;
        length++;
    }

    reverse_integer_characters(buffer, length, buffer_length);
}

write_pointer_to_buffer(StringBuffer* buffer, void* data) {
    value := cast(u64, data);
    if value {
        add_to_string_buffer(buffer, "0x");
        buffer_length := buffer.length;
        length := 0;
        while value > 0 {
            digit := value & 0xF;
            if digit < 10 {
                buffer.buffer[buffer.length++] = digit + '0';
            }
            else {
                buffer.buffer[buffer.length++] = digit + '7';
            }
            value >>= 4;
            value &= 0x0FFFFFFFFFFFFFFF;
            length++;
        }

        reverse_integer_characters(buffer, length, buffer_length);
    }
    else {
        add_to_string_buffer(buffer, "null");
    }
}

reverse_integer_characters(StringBuffer* buffer, int length, int original_length) {
    // Reverse the characters in the number
    // h e l l o 1 2 3
    //           5 6 7   a = o_len + i, b = len - i - 1
    //           7 6 5
    each i in 0..length / 2 - 1 {
        a := original_length + i;
        b := buffer.length - i - 1;
        temp := buffer.buffer[b];
        buffer.buffer[b] = buffer.buffer[a];
        buffer.buffer[a] = temp;
    }
}

write_array_to_buffer(StringBuffer* buffer, TypeInfo* element_type, void* data, int length) {
    add_to_string_buffer(buffer, "[ ");

    each i in 0..length-1 {
        element_data := data + element_type.size * i;
        write_value_to_buffer(buffer, element_type, element_data);
        add_to_string_buffer(buffer, ", ");
    }

    if length > 0 {
        buffer.buffer[buffer.length-2] = ' ';
        buffer.buffer[buffer.length-1] = ']';
    }
    else {
        buffer.buffer[buffer.length++] = ']';
    }
}

write_struct_to_buffer(StringBuffer* buffer, StructTypeInfo* type_info, void* data) {
    add_to_string_buffer(buffer, "{ ");

    each field in type_info.fields {
        add_to_string_buffer(buffer, field.name);
        add_to_string_buffer(buffer, ": ");
        element_data := data + field.offset;
        write_value_to_buffer(buffer, field.type_info, element_data);
        add_to_string_buffer(buffer, ", ");
    }

    if type_info.fields.length > 0 {
        buffer.buffer[buffer.length-2] = ' ';
        buffer.buffer[buffer.length-1] = '}';
    }
    else {
        buffer.buffer[buffer.length++] = '}';
    }
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
