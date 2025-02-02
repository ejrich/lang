// This module contains the standard library for the language

// Array support functions
ARRAY_BLOCK_SIZE := 10; #const

array_insert<T>(Array<T>* array, T value, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    // Reallocate the array if necessary
    length := array.length;
    if length % ARRAY_BLOCK_SIZE == 0 {
        new_blocks := length / ARRAY_BLOCK_SIZE + 1;
        element_size := size_of(T);

        if length {
            array.data = reallocator(array.data, length * element_size, element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
        else {
            array.data = allocator(element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
    }

    array.data[length] = value;
    array.length++;
}

array_insert<T>(Array<T>* array, int index, T value, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    length := array.length;
    element_size := size_of(T);

    if index > length {
        new_length := index + 1;
        available_padding := length % ARRAY_BLOCK_SIZE;
        diff := index - length;
        if diff >= available_padding {
            reserve_length := new_length + ARRAY_BLOCK_SIZE - new_length % ARRAY_BLOCK_SIZE;

            if length {
                array.data = reallocator(array.data, length * element_size, element_size * reserve_length);
            }
            else {
                array.data = allocator(element_size * reserve_length);
            }
        }

        array.length = new_length;
        array.data[index] = value;
        return;
    }

    if length % ARRAY_BLOCK_SIZE == 0 {
        new_blocks := length / ARRAY_BLOCK_SIZE + 1;

        if length {
            array.data = reallocator(array.data, length * element_size, element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
        else {
            array.data = allocator(element_size * new_blocks * ARRAY_BLOCK_SIZE);
        }
    }

    while length > index {
        array.data[length] = array.data[length - 1];
        length--;
    }

    array.data[index] = value;
    array.length++;
}

array_insert_range<T>(Array<T>* array, int index, Array<T> values, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    if values.length == 0 return;

    length := array.length;
    element_size := size_of(T);
    available_padding := length % ARRAY_BLOCK_SIZE;

    if index > length {
        new_length := index + values.length + 1;
        diff := new_length - length;
        if diff > available_padding {
            reserve_length := new_length + ARRAY_BLOCK_SIZE - new_length % ARRAY_BLOCK_SIZE;

            if length {
                array.data = reallocator(array.data, length * element_size, element_size * reserve_length);
            }
            else {
                array.data = allocator(element_size * reserve_length);
            }
        }

        array.length = new_length;
        each value, i in values {
            array.data[index + i] = value;
        }
        return;
    }

    new_length := length + values.length;
    diff := new_length - length;
    if diff > available_padding {
        reserve_length := new_length + ARRAY_BLOCK_SIZE - new_length % ARRAY_BLOCK_SIZE;

        if length {
            array.data = reallocator(array.data, length * element_size, element_size * reserve_length);
        }
        else {
            array.data = allocator(element_size * reserve_length);
        }
    }

    array.length = new_length;
    // Copy existing and new data
    while length > index {
        array.data[--new_length] = array.data[--length];
    }

    each value, i in values {
        array.data[index + i] = value;
    }
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

// Resize the array to the next largest array block size
array_reserve<T>(Array<T>* array, int length, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    if array.length >= length || length <= 0 return;

    reserve_length := length + ARRAY_BLOCK_SIZE - length % ARRAY_BLOCK_SIZE;
    element_size := size_of(T);

    if array.length {
        array.data = reallocator(array.data, array.length * element_size, element_size * reserve_length);
    }
    else {
        array.data = allocator(element_size * reserve_length);
    }

    array.length = length;
}

// Resize the array to the exact length specified
array_resize<T>(Array<T>* array, int length, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    if array.length >= length {
        array.length = length;
        return;
    }
    else if length <= 0 return;

    element_size := size_of(T);

    if array.length {
        array.data = reallocator(array.data, array.length * element_size, element_size * length);
    }
    else {
        array.data = allocator(element_size * length);
    }

    array.length = length;
}

bool array_contains<T>(Array<T> array, T value) {
    each element in array {
        if element == value return true;
    }

    return false;
}


// Sorting algorithms
interface int SortCompare<T>(T a, T b)

bubble_sort<T>(Array<T> array) {
    each i in 1..array.length {
        swapped := false;
        each j in 0..array.length - i - 1 {
            if array[j] > array[j + 1] {
                temp := array[j];
                array[j] = array[j + 1];
                array[j + 1] = temp;
                swapped = true;
            }
        }

        if !swapped break;
    }
}

bubble_sort<T>(Array<T> array, SortCompare<T> compare) {
    each i in 1..array.length {
        swapped := false;
        each j in 0..array.length - i - 1 {
            comparison := compare(array[j], array[j + 1]);
            if comparison == 1 {
                temp := array[j];
                array[j] = array[j + 1];
                array[j + 1] = temp;
                swapped = true;
            }
        }

        if !swapped break;
    }
}


// Assertions
#import compiler
assert(bool assertion, int exit_code = 1) #call_location #inline {
    #if build_env == BuildEnv.Debug {
        if assertion return;

        print("Assertion failed at % %:%\n", file, line, column);

        asm { int3; }
        exit_program(exit_code);
    }
}

assert(bool assertion, string message, int exit_code = 1) #call_location #inline {
    #if build_env == BuildEnv.Debug {
        if assertion return;

        print("Assertion failed: % at % %:%\n", message, file, line, column);

        asm { int3; }
        exit_program(exit_code);
    }
}


// Memory operations
void* memory_copy(void* dest, void* src, int length) {
    dest_buffer := cast(u8*, dest);
    src_buffer := cast(u8*, src);

    each i in 0..length-1 {
        dest_buffer[i] = src_buffer[i];
    }

    return dest;
}

clear_memory(void* dest, int length) {
    dest_buffer := cast(u8*, dest);

    each i in 0..length-1 {
        dest_buffer[i] = 0;
    }
}

void* allocate_memory(u64 size) {
    pointer: void*;
    #if os == OS.Linux {
        pointer = mmap(null, size, Prot.PROT_READ | Prot.PROT_WRITE, MmapFlags.MAP_PRIVATE | MmapFlags.MAP_ANONYMOUS | MmapFlags.MAP_NORESERVE, 0, 0);
    }
    #if os == OS.Windows {
        pointer = VirtualAlloc(null, size, AllocationType.MEM_COMMIT | AllocationType.MEM_RESERVE, ProtectionType.PAGE_READWRITE);
    }

    return pointer;
}

void* reallocate_memory(void* pointer, u64 old_size, u64 new_size) {
    new_pointer: void*;
    #if os == OS.Linux {
        new_pointer = mremap(pointer, old_size, new_size, MremapFlags.MREMAP_MAYMOVE);
    }
    #if os == OS.Windows {
        memory_info: MEMORY_BASIC_INFORMATION;
        VirtualQuery(pointer, &memory_info, size_of(memory_info));

        if new_size > memory_info.RegionSize {
            new_pointer = VirtualAlloc(null, new_size, AllocationType.MEM_COMMIT | AllocationType.MEM_RESERVE, ProtectionType.PAGE_READWRITE);
            memory_copy(new_pointer, pointer, old_size);
            VirtualFree(pointer, 0, FreeType.MEM_RELEASE);
        }
        else {
            new_pointer = pointer;
        }
    }

    return new_pointer;
}

free_memory(void* pointer, u64 size) {
    #if os == OS.Linux {
        munmap(pointer, size);
    }
    #if os == OS.Windows {
        VirtualFree(pointer, 0, FreeType.MEM_RELEASE);
    }
}

void* default_allocator(u64 size) {
    // The default allocator is a crude allocation method that will always request memory from the operating system
    pointer := allocate_memory(size);

    allocation: DefaultAllocation = { size = size; pointer = pointer; }

    if default_allocations.length == 0 {
        default_allocations.length = 10;
        default_allocations.data = allocate_memory(10 * size_of(DefaultAllocation));
        default_allocations[0] = allocation;
        add_exit_callback(free_default_allocations);
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
            old_length := default_allocations.length;
            old_size := old_length * size_of(DefaultAllocation);
            new_pointer: DefaultAllocation* = reallocate_memory(default_allocations.data, old_size, (old_length + 10) * size_of(DefaultAllocation));

            if new_pointer != default_allocations.data {
                // memory_copy(new_pointer, default_allocations.data, old_size);
                // free_memory(default_allocations.data, old_size);

                default_allocations.data = new_pointer;
            }

            default_allocations[old_length] = allocation;
            default_allocations.length += 10;
        }
    }

    return pointer;
}

void* default_reallocator(void* pointer, u64 old_size, u64 size) {
    each allocation in default_allocations {
        if allocation.pointer == pointer {
            // Try to reallocate the memory from the os
            new_pointer := reallocate_memory(pointer, allocation.size, size);

            if new_pointer != pointer {
                allocation.pointer = new_pointer;
            }

            allocation.size = size;
            return new_pointer;
        }
    }

    return pointer;
}

default_free(void* data) {
    each allocation, i in default_allocations {
        if allocation.pointer == data {
            free_memory(allocation.pointer, allocation.size);
            allocation.pointer = null;
            return;
        }
    }
}


// Sleep and timing related functions
sleep(int milliseconds) {
    if milliseconds <= 0 return;

    #if os == OS.Linux {
        seconds := milliseconds / 1000;
        ms := milliseconds % 1000;

        ts: Timespec = { tv_sec = seconds; tv_nsec = ms * 1000000; }
        nanosleep(&ts, &ts);
    }
    #if os == OS.Windows {
        Sleep(milliseconds);
    }
}

yield() {
    #if os == OS.Linux {
        sched_yield();
    }
    #if os == OS.Windows {
        YieldProcessor();
    }
}

u64 get_performance_frequency() {
    freq: u64;

    #if os == OS.Linux {
        // Always return to handle nanoseconds
        return 1000000000;
    }
    #if os == OS.Windows {
        QueryPerformanceFrequency(&freq);
    }

    return freq;
}

u64 get_performance_counter() {
    counter: u64;

    #if os == OS.Linux {
        now: Timespec;

        clock_gettime(ClockId.CLOCK_MONOTONIC_RAW, &now);
        counter = now.tv_sec * 1000000000 + now.tv_nsec;
    }
    #if os == OS.Windows {
        QueryPerformanceCounter(&counter);
    }

    return counter;
}

int random_integer() {
    value: int;
    #if os == OS.Linux {
        getrandom(&value, 4, RandomFlags.GRND_RANDOM);
    }
    #if os == OS.Windows {
        handle: Handle*;
        rng: CArray<u16> = ['R', 'N', 'G', 0]

        if BCryptOpenAlgorithmProvider(&handle, &rng, null, 0) == NtStatus.STATUS_SUCCESS {
            BCryptGenRandom(handle, &value, 4, 0);
            BCryptCloseAlgorithmProvider(handle, 0);
        }
    }

    return value;
}


// Functions for printing and formatting strings
print(string format, Params args) {
    if args.length == 0 {
        write_buffer_to_standard_out(format.data, format.length);
        return;
    }

    buffer: Array<u8>[STRING_BUFFER_MAX_LENGTH];
    string_buffer: StringBuffer = { buffer = buffer; flush = string_buffer_write_to_standard_out; }
    format_string_arguments(&string_buffer, format, args);
    write_buffer_to_standard_out(buffer.data, string_buffer.length);
}

string format_string(string format, Allocate allocator = default_allocator, Params args) {
    buffer: Array<u8>[STRING_BUFFER_MAX_LENGTH];
    string_buffer: StringBuffer = { buffer = buffer; }

    format_string_arguments(&string_buffer, format, args);

    value: string;
    // If the formatted string overflows the initial buffer, then allocate the necessary total length
    // for the final string and format the string arguments again to the final buffer
    if string_buffer.length > string_buffer.buffer.length {
        value = { length = string_buffer.length; data = allocator(string_buffer.length); }
        string_buffer.length = 0;
        string_buffer.buffer.length = value.length;
        string_buffer.buffer.data = value.data;
        format_string_arguments(&string_buffer, format, args);
    }
    else {
        value = { length = string_buffer.length; data = allocator(string_buffer.length); }
        memory_copy(value.data, buffer.data, string_buffer.length);
    }

    return value;
}

string allocate_string(string input, Allocate allocator = default_allocator) {
    value: string = { length = input.length; data = allocator(input.length); }
    memory_copy(value.data, input.data, input.length);
    return value;
}

string get_enum_name<T>(T value) {
    enum_type := cast(EnumTypeInfo*, type_of(T));
    assert(enum_type.type == TypeKind.Enum);

    raw_value := cast(s64, value);
    each enum_value in enum_type.values {
        if enum_value.value == raw_value {
            return enum_value.name;
        }
    }

    return empty_string;
}

bool, T get_enum_value<T>(string name) {
    enum_type := cast(EnumTypeInfo*, type_of(T));
    assert(enum_type.type == TypeKind.Enum);

    each enum_value in enum_type.values {
        if enum_value.name == name {
            return true, cast(T, enum_value.value);
        }
    }

    return false, cast(T, 0);
}

struct StringBuffer {
    length: s64;
    buffer: Array<u8>;
    flush: flush_string_buffer;
    flush_data: void*;
}

interface flush_string_buffer(void* data, u8* buffer, s64 length)

union IntFormatValue {
    signed: s64;
    unsigned: u64;
}

struct IntFormat {
    value: IntFormatValue;
    signed := true;
    base: u8 = 10;
    min_chars := 1;
}

IntFormat int_format(s64 value, u8 base = 10, int min_chars = 1) #inline {
    format: IntFormat = { signed = true; base = base; min_chars = min_chars; }
    format.value.signed = value;
    return format;
}

IntFormat uint_format(u64 value, u8 base = 10, int min_chars = 1) #inline {
    format: IntFormat = { signed = false; base = base; min_chars = min_chars; }
    format.value.unsigned = value;
    return format;
}

struct FloatFormat {
    value: float64;
    decimal_places := 4;
}

FloatFormat float_format(float64 value, int decimal_places = 4) #inline {
    format: FloatFormat = { value = value; decimal_places = decimal_places; }
    return format;
}

format_string_arguments(StringBuffer* buffer, string format, Array<Any> args) {
    arg_index := 0;

    each i in 0..format.length-1 {
        char := format[i];
        if char == '%' && arg_index < args.length {
            arg := args[arg_index++];
            write_value_to_buffer(buffer, arg.type, arg.data);
        }
        else {
            add_char_to_string_buffer(buffer, char);
        }
    }
}

write_buffer_to_standard_out(u8* buffer, s64 length) {
    #if os == OS.Linux {
        write(stdout, buffer, length);
    }
    #if os == OS.Windows {
        std_out := GetStdHandle(STD_OUTPUT_HANDLE);
        if std_out == null {
            AttachConsole(-1);
            std_out = GetStdHandle(STD_OUTPUT_HANDLE);

            // Fail out here since the handle wasn't able to be created
            if std_out == null return;
        }

        written: int;
        WriteConsoleA(std_out, buffer, length, &written, null);
    }
}

write_value_to_buffer(StringBuffer* buffer, TypeInfo* type, void* data) {
    if data == null {
        add_to_string_buffer(buffer, "null");
        return;
    }

    if type == type_of(IntFormat) {
        format := cast(IntFormat*, data);
        write_integer(buffer, *format);
        return;
    }

    if type == type_of(FloatFormat) {
        format := cast(FloatFormat*, data);
        write_float(buffer, *format);
        return;
    }

    switch type.type {
        case TypeKind.Void; add_to_string_buffer(buffer, "void");
        case TypeKind.Boolean; {
            value := *cast(bool*, data);
            if value add_to_string_buffer(buffer, "true");
            else add_to_string_buffer(buffer, "false");
        }
        case TypeKind.Integer; {
            type_info := cast(IntegerTypeInfo*, type);
            format: IntFormat = { signed = type_info.signed; }

            if type_info.signed {
                switch type_info.size {
                    case 1;  format.value.signed = *cast(s8*, data);
                    case 2;  format.value.signed = *cast(s16*, data);
                    case 4;  format.value.signed = *cast(s32*, data);
                    default; format.value.signed = *cast(s64*, data);
                }
            }
            else {
                switch type_info.size {
                    case 1;  format.value.unsigned = *cast(u8*, data);
                    case 2;  format.value.unsigned = *cast(u16*, data);
                    case 4;  format.value.unsigned = *cast(u32*, data);
                    default; format.value.unsigned = *cast(u64*, data);
                }
            }

            write_integer(buffer, format);
        }
        case TypeKind.Float; {
            format: FloatFormat;
            if type.size == 4 format.value = *cast(float*, data);
            else format.value = *cast(float64*, data);

            write_float(buffer, format);
        }
        case TypeKind.String; {
            value := *cast(string*, data);
            add_to_string_buffer(buffer, value);
        }
        case TypeKind.Pointer; {
            write_pointer_to_buffer(buffer, data);
        }
        case TypeKind.Array; {
            type_info := cast(StructTypeInfo*, type);
            pointer_field := type_info.fields[1];
            pointer_type_info := cast(PointerTypeInfo*, pointer_field.type_info);
            element_type := pointer_type_info.pointer_type;

            array := cast(Array<void*>*, data);
            write_array_to_buffer(buffer, element_type, array.data, array.length);
        }
        case TypeKind.CArray; {
            type_info := cast(CArrayTypeInfo*, type);
            element_type := type_info.element_type;

            write_array_to_buffer(buffer, element_type, data, type_info.length);
        }
        case TypeKind.Enum; {
            type_info := cast(EnumTypeInfo*, type);
            value: s64;
            switch type_info.size {
                case 1;  value = *cast(s8*, data);
                case 2;  value = *cast(s16*, data);
                case 4;  value = *cast(s32*, data);
                default; value = *cast(s64*, data);
            }

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
                    if (enum_value.value == 0 && value == 0) || (enum_value.value != 0 && (enum_value.value & value) == enum_value.value) {
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
                    add_char_to_string_buffer(buffer, '0');
                }
                else {
                    if value < 0 {
                        add_char_to_string_buffer(buffer, '-');
                        value *= -1;
                    }
                    write_integer_to_buffer(buffer, value);
                }
            }
        }
        case TypeKind.Struct; {
            type_info := cast(StructTypeInfo*, type);
            write_struct_to_buffer(buffer, type_info, data);
        }
        case TypeKind.Union; {
            type_info := cast(UnionTypeInfo*, type);
            add_to_string_buffer(buffer, "{ ");

            length := type_info.fields.length;
            each field, i in type_info.fields {
                add_to_string_buffer(buffer, field.name);
                add_to_string_buffer(buffer, ": ");
                switch field.type_info.type {
                    case TypeKind.Pointer;
                    case TypeKind.Interface; {
                        pointer_value := *cast(void**, data);
                        write_pointer_to_buffer(buffer, pointer_value);
                    }
                    default;
                        write_value_to_buffer(buffer, field.type_info, data);
                }

                if i == length - 1
                    add_to_string_buffer(buffer, " }");
                else
                    add_to_string_buffer(buffer, ", ");
            }
        }
        case TypeKind.Interface; {
            write_pointer_to_buffer(buffer, data);
        }
        case TypeKind.Type; {
            value := *cast(s32*, data);
            type_info := __type_table[value];
            add_to_string_buffer(buffer, type_info.name);
        }
        case TypeKind.Any; {
            type_info := cast(StructTypeInfo*, type);
            write_struct_to_buffer(buffer, type_info, data);
        }
        case TypeKind.Compound; {
            // @Cleanup This branch should not be hit
        }
        case TypeKind.Function; {
            type_info := cast(FunctionTypeInfo*, type);
            add_to_string_buffer(buffer, type_info.name);
        }
    }
}

add_char_to_string_buffer(StringBuffer* buffer, u8 char) {
    if buffer.length >= buffer.buffer.length {
        if buffer.flush != null {
            buffer.flush(buffer.flush_data, buffer.buffer.data, buffer.length);
            buffer.length = 0;
        }
        else {
            buffer.length++;
            return;
        }
    }

    buffer.buffer[buffer.length++] = char;
}

add_chars_to_string_buffer(StringBuffer* buffer, u8* chars, s64 length) {
    char_index := 0;
    while length {
        max_copy_length := buffer.buffer.length - buffer.length;

        if length > max_copy_length {
            memory_copy(buffer.buffer.data + buffer.length, chars + char_index, max_copy_length);

            if buffer.flush != null {
                buffer.flush(buffer.flush_data, buffer.buffer.data, buffer.length);
                buffer.length = 0;

                buffer.length += max_copy_length;
                char_index += max_copy_length;
                length -= max_copy_length;
            }
            else {
                buffer.length += length;
                length = 0;
            }
        }
        else {
            memory_copy(buffer.buffer.data + buffer.length, chars + char_index, length);
            buffer.length += length;
            length = 0;
        }
    }
}

add_to_string_buffer(StringBuffer* buffer, string value) {
    add_chars_to_string_buffer(buffer, value.data, value.length);
}

write_integer(StringBuffer* buffer, IntFormat format) {
    if format.value.unsigned == 0 {
        each i in format.min_chars {
            add_char_to_string_buffer(buffer, '0');
        }
    }
    else {
        if format.signed && format.value.signed < 0 {
            add_char_to_string_buffer(buffer, '-');
            format.value.signed *= -1;
        }
        write_integer_to_buffer(buffer, format.value.unsigned, format.base, format.min_chars);
    }
}

write_float(StringBuffer* buffer, FloatFormat format) {
    value := format.value;
    if value < 0 {
        add_char_to_string_buffer(buffer, '-');
        value *= -1;
    }

    exponent := 0;
    // For values greater than what a u64 can hold, use scientific notation
    if value > 0xFFFFFFFFFFFFFFFF {
        exponent = 19;
        value /= 10000000000000000000;

        while true {
            if value < 10     {                                break; }
            if value < 100    { value /= 10;    exponent += 1; break; }
            if value < 1000   { value /= 100;   exponent += 2; break; }
            if value < 10000  { value /= 1000;  exponent += 3; break; }
            if value < 100000 { value /= 10000; exponent += 4; break; }
            exponent += 5;
            value /= 100000;
        }
    }

    whole := cast(u64, value);
    if whole == 0 {
        add_char_to_string_buffer(buffer, '0');
    }
    else {
        write_integer_to_buffer(buffer, whole);
    }

    add_char_to_string_buffer(buffer, '.');
    value -= whole;

    each x in 0..format.decimal_places - 1 {
        value *= 10;
        digit := cast(u8, value);
        add_char_to_string_buffer(buffer, digit + '0');
        value -= digit;
    }

    if exponent {
        add_char_to_string_buffer(buffer, 'e');
        write_integer_to_buffer(buffer, exponent);
    }
}

write_integer_to_buffer(StringBuffer* buffer, u64 value, u8 base = 10, int min_chars = 1) {
    digits: Array<u8>[64];
    length := 0;
    while value > 0 {
        digit := value % base;
        if digit < 10
            digits[length++] = digit + '0';
        else
            digits[length++] = digit + '7';

        value /= base;
    }

    while length < min_chars {
        digits[length++] = '0';
    }

    reverse_integer_characters(digits, length);

    str: string = { length = length; data = digits.data; }
    add_to_string_buffer(buffer, str);
}

write_pointer_to_buffer(StringBuffer* buffer, void* data) {
    value := cast(u64, data);
    if value {
        digits: Array<u8>[16];
        each i in 0..15 {
            digit := (value >> ((15 - i) * 4)) & 0xF;
            if digit < 10
                digits[i] = digit + '0';
            else
                digits[i] = digit + '7';
        }
        str: string = { length = 16; data = digits.data; }

        add_to_string_buffer(buffer, "0x");
        add_to_string_buffer(buffer, str);
    }
    else {
        add_to_string_buffer(buffer, "null");
    }
}

reverse_integer_characters(Array<u8> buffer, int length) {
    each a in 0..length / 2 - 1 {
        b := length - a - 1;
        temp := buffer[b];
        buffer[b] = buffer[a];
        buffer[a] = temp;
    }
}

write_array_to_buffer(StringBuffer* buffer, TypeInfo* element_type, void* data, int length) {
    add_to_string_buffer(buffer, "[ ");

    each i in 0..length-1 {
        element_data := data + element_type.size * i;
        write_value_to_buffer(buffer, element_type, element_data);

        if i == length - 1
            add_to_string_buffer(buffer, " ]");
        else
            add_to_string_buffer(buffer, ", ");
    }

    if length == 0 {
        add_char_to_string_buffer(buffer, ']');
    }
}

write_struct_to_buffer(StringBuffer* buffer, StructTypeInfo* type_info, void* data) {
    add_to_string_buffer(buffer, "{ ");

    length := type_info.fields.length;
    each field, i in type_info.fields {
        add_to_string_buffer(buffer, field.name);
        add_to_string_buffer(buffer, ": ");
        element_data := data + field.offset;
        switch field.type_info.type {
            case TypeKind.Pointer;
            case TypeKind.Interface; {
                pointer_value := *cast(void**, element_data);
                write_pointer_to_buffer(buffer, pointer_value);
            }
            default;
                write_value_to_buffer(buffer, field.type_info, element_data);
        }

        if i == length - 1
            add_to_string_buffer(buffer, " }");
        else
            add_to_string_buffer(buffer, ", ");
    }

    if length == 0 {
        add_char_to_string_buffer(buffer, '}');
    }
}


// File and directory operations and types
bool file_exists(string path) {
    null_terminated_path: Array<u8>[path.length + 1];
    memory_copy(null_terminated_path.data, path.data, path.length);
    null_terminated_path[path.length] = 0;

    exists: bool;

    #if os == OS.Linux {
        stat_buf: Stat;
        exists = stat(null_terminated_path.data, &stat_buf) == 0;
    }
    #if os == OS.Windows {
        exists = PathFileExistsA(null_terminated_path.data);
    }

    return exists;
}

bool create_directory(string path) {
    null_terminated_path: Array<u8>[path.length + 1];
    memory_copy(null_terminated_path.data, path.data, path.length);
    null_terminated_path[path.length] = 0;

    created: bool;

    #if os == OS.Linux {
        created = mkdir(null_terminated_path.data, 0x1FF) == 0;
    }
    #if os == OS.Windows {
        created = CreateDirectoryA(null_terminated_path.data, null);
    }

    return created;
}

#if os == OS.Windows {
    struct File {
        handle: Handle*;
    }
}
else {
    struct File {
        handle: s64;
    }
}

enum FileFlags {
    None   = 0;
    Read   = 1;
    Write  = 2;
    Create = 4;
    Append = 8;
}

bool, File open_file(string path, FileFlags flags = FileFlags.Read) {
    file: File;
    null_terminated_path: Array<u8>[path.length + 1];
    memory_copy(null_terminated_path.data, path.data, path.length);
    null_terminated_path[path.length] = 0;

    #if os == OS.Linux {
        open_flags: OpenFlags;

        if flags & FileFlags.Read {
            open_flags |= OpenFlags.O_RDONLY;
        }
        if flags & FileFlags.Write {
            if flags & FileFlags.Read {
                open_flags = OpenFlags.O_RDWR;
            }
            else {
                open_flags |= OpenFlags.O_WRONLY;
            }
        }
        if flags & FileFlags.Create {
            if (flags & FileFlags.Write) == FileFlags.None {
                open_flags |= OpenFlags.O_WRONLY;
            }
            open_flags |= OpenFlags.O_CREAT | OpenFlags.O_TRUNC;
        }
        if flags & FileFlags.Append {
            if (flags & FileFlags.Write) == FileFlags.None {
                open_flags |= OpenFlags.O_WRONLY;
            }
            open_flags |= OpenFlags.O_CREAT | OpenFlags.O_APPEND;
        }

        file.handle = open(null_terminated_path.data, open_flags, OpenMode.S_RWALL);

        if file.handle < 0 {
            return false, file;
        }
        else if flags & FileFlags.Append {
            lseek(file.handle, 0, Whence.SEEK_END);
        }
    }
    #if os == OS.Windows {
        file_exists := PathFileExistsA(null_terminated_path.data);
        if !file_exists && flags == FileFlags.Read return false, file;

        open_type: OpenFileType;
        file_info: OFSTRUCT;

        if flags & FileFlags.Read {
            open_type |= OpenFileType.OF_READ;
        }
        if flags & FileFlags.Write {
            if flags & FileFlags.Read {
                open_type = OpenFileType.OF_READWRITE;
            }
            else {
                open_type |= OpenFileType.OF_WRITE;
            }
        }
        if flags & FileFlags.Create {
            if (flags & FileFlags.Write) == FileFlags.None {
                open_type |= OpenFileType.OF_WRITE;
            }
            open_type |= OpenFileType.OF_CREATE;
        }
        if flags & FileFlags.Append {
            if (flags & FileFlags.Write) == FileFlags.None {
                open_type |= OpenFileType.OF_WRITE;
            }

            if PathFileExistsA(null_terminated_path.data) {
                open_type |= OpenFileType.OF_WRITE;
            }
            else {
                open_type |= OpenFileType.OF_CREATE;
            }
        }

        file.handle = OpenFile(null_terminated_path.data, &file_info, open_type);

        if file.handle == null {
            return false, file;
        }
        else if flags & FileFlags.Append {
            SetFilePointer(file.handle, 0, null, MoveMethod.FILE_END);
        }
    }

    return true, file;
}

bool delete_file(string path) {
    deleted: bool;

    null_terminated_path: Array<u8>[path.length + 1];
    memory_copy(null_terminated_path.data, path.data, path.length);
    null_terminated_path[path.length] = 0;

    #if os == OS.Linux {
        success := unlink(null_terminated_path.data);
        deleted = success == 0;
    }
    #if os == OS.Windows {
        deleted = DeleteFileA(null_terminated_path.data);
    }

    return deleted;
}

struct FileEntry {
    type: FileType;
    name: string;
}

enum FileType {
    Unknown;
    File;
    Directory;
}

bool, Array<FileEntry> get_files_in_directory(string path, Allocate allocator = default_allocator, Reallocate reallocator = default_reallocator) {
    files: Array<FileEntry>;

    #if os == OS.Linux {
        null_terminated_path: Array<u8>[path.length + 1];
        memory_copy(null_terminated_path.data, path.data, path.length);
        null_terminated_path[path.length] = 0;

        open_flags := OpenFlags.O_RDONLY | OpenFlags.O_NONBLOCK | OpenFlags.O_DIRECTORY | OpenFlags.O_LARGEFILE | OpenFlags.O_CLOEXEC;
        directory := open(null_terminated_path.data, open_flags, OpenMode.S_RWALL);

        if directory < 0 {
            return false, files;
        }

        buffer: CArray<u8>[5600];
        while true {
            bytes := getdents64(directory, cast(Dirent*, &buffer), buffer.length);

            if bytes == 0 break;

            position := 0;
            while position < bytes {
                dirent := cast(Dirent*, &buffer + position);
                name := convert_c_string(&dirent.d_name);

                file_entry: FileEntry = { name = allocate_string(name, allocator); }
                if dirent.d_type == DirentType.DT_REG {
                    file_entry.type = FileType.File;
                }
                else if dirent.d_type == DirentType.DT_DIR {
                    file_entry.type = FileType.Directory;
                }

                array_insert(&files, file_entry, allocator, reallocator);
                position += dirent.d_reclen;
            }
        }

        close(directory);
    }
    #if os == OS.Windows {
        wildcard := "/*"; #const
        path_with_wildcard: Array<u8>[path.length + wildcard.length + 1];
        memory_copy(path_with_wildcard.data, path.data, path.length);
        memory_copy(path_with_wildcard.data + path.length, wildcard.data, wildcard.length);
        path_with_wildcard[path.length + wildcard.length] = 0;

        find_data: WIN32_FIND_DATAA;
        find_handle := FindFirstFileA(path_with_wildcard.data, &find_data);

        if cast(s64, find_handle) == -1 {
            return false, files;
        }

        while true {
            file_name := convert_c_string(&find_data.cFileName);
            file_entry: FileEntry = { name = allocate_string(file_name, allocator); }

            if find_data.dwFileAttributes & FileAttribute.FILE_ATTRIBUTE_DIRECTORY {
                file_entry.type = FileType.Directory;
            }
            else {
                file_entry.type = FileType.File;
            }

            array_insert(&files, file_entry, allocator, reallocator);

            if !FindNextFileA(find_handle, &find_data) break;
        }

        FindClose(find_handle);
    }

    // Sort by name
    bubble_sort(files, file_entry_compare);

    return true, files;
}

bool close_file(File file) {
    #if os == OS.Linux {
        success := close(file.handle);
        return success == 0;
    }
    #if os == OS.Windows {
        return CloseHandle(file.handle);
    }

    return false;
}

bool, string read_file(string file_path, Allocate allocator = default_allocator) {
    file_contents: string;

    success, file := open_file(file_path);
    if success {
        #if os == OS.Linux {
            size := lseek(file.handle, 0, Whence.SEEK_END);
            lseek(file.handle, 0, Whence.SEEK_SET);

            file_contents = { length = size; data = allocator(size); }

            read(file.handle, file_contents.data, size);
        }
        #if os == OS.Windows {
            size := SetFilePointer(file.handle, 0, null, MoveMethod.FILE_END);
            SetFilePointer(file.handle, 0, null, MoveMethod.FILE_BEGIN);

            file_contents = { length = size; data = allocator(size); }

            read: int;
            ReadFile(file.handle, file_contents.data, size, &read, null);
        }

        close_file(file);
    }

    return success, file_contents;
}

write_to_file(File file, string format, Params args) {
    if args.length == 0 {
        write_buffer_to_file(file, format.data, format.length);
        return;
    }

    buffer: Array<u8>[STRING_BUFFER_MAX_LENGTH];
    string_buffer: StringBuffer = { buffer = buffer; flush = string_buffer_write_to_file; flush_data = &file; }
    format_string_arguments(&string_buffer, format, args);
    write_buffer_to_file(file, buffer.data, string_buffer.length);
}

write_to_file(File file, u8 char) {
    write_buffer_to_file(file, &char, 1);
}

write_buffer_to_file(File file, void* buffer, s64 length) {
    #if os == OS.Linux {
        write(file.handle, buffer, length);
    }
    #if os == OS.Windows {
        written: int;
        WriteFile(file.handle, buffer, length, &written, null);
    }
}

u64 file_get_last_modified(string file) {
    time := u64;
    #if os == OS.Linux {
        null_terminated_path: Array<u8>[file.length + 1];
        memory_copy(null_terminated_path.data, file.data, file.length);
        null_terminated_path[file.length] = 0;

        stat_buf: Stat;
        stat(null_terminated_path.data, &stat_buf);
        time = stat_buf.st_mtim.tv_sec;
    }
    #if os == OS.Windows {
        found, file_handle := open_file(file);
        if found {
            file_time: FILETIME;
            GetFileTime(file_handle.handle, null, null, &file_time);
            time = cast(u64, file_time.dwHighDateTime) << 32 | file_time.dwLowDateTime;
            close_file(file_handle);
        }
    }

    return time;
}


// System information
int get_processors() {
    result: int;
    #if os == OS.Linux {
        result = get_nprocs();
    }
    #if os == OS.Windows {
        info: SYSTEM_INFO;
        GetSystemInfo(&info);
        result = info.dwNumberOfProcessors;
    }

    return result;
}

string get_environment_variable(string name, Allocate allocator = default_allocator) {
    result: string;
    #if os == OS.Linux {
        variable := getenv(name);
        if variable {
            variable_string := convert_c_string(variable);
            result = { length = variable_string.length; data = allocator(variable_string.length); }
            memory_copy(result.data, variable_string.data, result.length);
        }
    }
    #if os == OS.Windows {
        result.length = GetEnvironmentVariableA(name, null, 0);
        if result.length {
            result.data = allocator(result.length);
            result.length = GetEnvironmentVariableA(name, result.data, result.length);
        }
    }

    return result;
}

int execute_command(string command, bool silent = false, bool print = false) {
    if print print("%\n", command);

    status: int;
    #if os == OS.Windows {
        sa: SECURITY_ATTRIBUTES = { nLength = size_of(SECURITY_ATTRIBUTES); bInheritHandle = true; }
        stdOutRd, stdOutWr: Handle*;

        if !CreatePipe(&stdOutRd, &stdOutWr, &sa, 0) {
            return -1;
        }
        SetHandleInformation(stdOutRd, HandleFlags.HANDLE_FLAG_INHERIT, HandleFlags.None);

        si: STARTUPINFOA = { cb = size_of(STARTUPINFOA); dwFlags = 0x100; hStdInput = GetStdHandle(STD_INPUT_HANDLE); hStdOutput = stdOutWr; }
        pi: PROCESS_INFORMATION;

        // Null terminate the command in case it is not
        null_terminated_data: Array<u8>[command.length + 1];
        null_terminated_data[command.length] = 0;
        memory_copy(null_terminated_data.data, command.data, command.length);
        command.data = null_terminated_data.data;

        if !CreateProcessA(null, command, null, null, true, 0, null, null, &si, &pi) {
            CloseHandle(stdOutRd);
            CloseHandle(stdOutWr);
            return -1;
        }

        CloseHandle(si.hStdInput);
        CloseHandle(stdOutWr);

        if silent {
            buf: CArray<u8>[1000];
            parentStdOut := GetStdHandle(STD_OUTPUT_HANDLE);
            while true {
                read: int;
                success := ReadFile(stdOutRd, &buf, 1000, &read, null);

                if !success || read == 0 break;
            }
        }

        GetExitCodeProcess(pi.hProcess, &status);

        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        CloseHandle(stdOutRd);
    }
    else {
        if silent {
            silent_command := " > /dev/null"; #const
            length := command.length + silent_command.length;

            string_data: Array<u8>[length + 1];
            string_data[length] = 0;
            silenced_command: string = { length = length; data = string_data.data; }

            memory_copy(silenced_command.data, command.data, command.length);
            memory_copy(silenced_command.data + command.length, silent_command.data, silent_command.length);

            command = silenced_command;
        }
        else {
            // Null terminate the command in case it is not
            null_terminated_data: Array<u8>[command.length + 1];
            null_terminated_data[command.length] = 0;
            memory_copy(null_terminated_data.data, command.data, command.length);
            command.data = null_terminated_data.data;
        }

        status = system(command);
    }

    return status;
}

add_exit_callback(ExitCallback callback) {
    exit_callbacks := get_exit_callbacks();
    array_insert(exit_callbacks, callback);
}

#private

int file_entry_compare(FileEntry a, FileEntry b) {
    return string_compare(a.name, b.name);
}

STRING_BUFFER_MAX_LENGTH := 1024; #const

string_buffer_write_to_standard_out(void* data, u8* buffer, s64 length) {
    write_buffer_to_standard_out(buffer, length);
}

string_buffer_write_to_file(void* data, u8* buffer, s64 length) {
    file := *cast(File*, data);
    write_buffer_to_file(file, buffer, length);
}

struct DefaultAllocation {
    size: u64;
    pointer: void*;
}

default_allocations: Array<DefaultAllocation>;

free_default_allocations() {
    each allocation in default_allocations {
        if allocation.pointer != null {
            free_memory(allocation.pointer, allocation.size);
        }
    }

    free_memory(default_allocations.data, default_allocations.length * size_of(DefaultAllocation));
}
