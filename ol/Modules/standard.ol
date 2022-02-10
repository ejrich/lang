// This module contains the standard library for the language

#if os == OS.Linux {
    #import linux
}
#if os == OS.Windows {
    #import windows
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
    #if os == OS.Windows {
        ExitProcess(exit_code);
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
        memory_info: MemoryBasicInformation;
        VirtualQuery(pointer, &memory_info, size_of(memory_info));

        if new_size > memory_info.RegionSize {
            VirtualFree(pointer, 0, FreeType.MEM_RELEASE);
            new_pointer = VirtualAlloc(null, new_size, AllocationType.MEM_COMMIT | AllocationType.MEM_RESERVE, ProtectionType.PAGE_READWRITE);
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

void* default_allocator(int size) {
    // The default allocator is a crude allocation method that will always request memory from the operating system
    pointer := allocate_memory(size);

    allocation: DefaultAllocation = { size = size; pointer = pointer; }

    if default_allocations.length == 0 {
        default_allocations.length = 10;
        default_allocations.data = allocate_memory(10 * size_of(DefaultAllocation));
        default_allocations[0] = allocation;
        array_insert(&exit_callbacks, free_default_allocations);
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

void* default_reallocator(void* pointer, int size) {
    each allocation in default_allocations {
        if allocation.pointer == pointer {
            // Try to reallocate the memory from the os
            new_pointer := reallocate_memory(pointer, allocation.size, size);

            // If reallocation returns a new pointer:
            // - Copy the data from the old pointer to the new pointer
            // - and free the old pointer and clear out the old allocation
            if new_pointer != pointer {
                // memory_copy(new_pointer, pointer, allocation.size);
                // free_memory(pointer, allocation.size);
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
    each allocation, i in default_allocations {
        if allocation.pointer == data {
            free_memory(allocation.pointer, allocation.size);
            allocation.pointer = null;
            return;
        }
    }

    assert(false, "Pointer not found in list of allocations\n");
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

u64 get_performance_counter() {
    counter: u64;

    #if os == OS.Linux {
        now: Timespec;

        clock_gettime(ClockId.CLOCK_MONOTONIC_RAW, &now);
        counter += now.tv_sec * 1000000000 + now.tv_nsec;
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
        if char == '%' && arg_index < args.length {
            arg := args[arg_index++];
            write_value_to_buffer(buffer, arg.type, arg.data);
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
    #if os == OS.Windows {
        stdOut := GetStdHandle(STD_OUTPUT_HANDLE);
        if (stdOut != null)
        {
            written: int;
            WriteConsoleA(stdOut, buffer, length, &written, null);
        }
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

    if value < 0 {
        buffer.buffer[buffer.length++] = '-';
        value *= -1;
        whole *= -1;
    }

    if whole == 0 {
        buffer.buffer[buffer.length++] = '0';
    }
    else {
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


// File and directory operations and types
struct File {
    handle: u64;
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

        file.handle = open(path.data, open_flags, OpenMode.S_RWALL);

        if file.handle < 0 {
            return false, file;
        }
        else if flags & FileFlags.Append {
            lseek(file.handle, 0, Whence.SEEK_END);
        }
    }
    #if os == OS.Windows {
        open_type: OpenFileType;
        file_info: OfStruct;

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
            test_handle := OpenFile(path, &file_info, OpenFileType.OF_EXIST);

            if test_handle {
                open_type |= OpenFileType.OF_WRITE;
            }
            else {
                open_type |= OpenFileType.OF_CREATE;
            }
        }

        file_handle := OpenFile(path, &file_info, open_type);

        if file_handle == null {
            return false, file;
        }
        else if flags & FileFlags.Append {
            SetFilePointer(file_handle, 0, null, MoveMethod.FILE_END);
        }

        file.handle = cast(u64, file_handle);
    }

    return true, file;
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
        open_flags := OpenFlags.O_RDONLY | OpenFlags.O_NONBLOCK | OpenFlags.O_DIRECTORY | OpenFlags.O_LARGEFILE | OpenFlags.O_CLOEXEC;
        directory := open(path.data, open_flags, OpenMode.S_RWALL);

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

                file_entry: FileEntry = { name = convert_c_string(&dirent.d_name); }
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
        find_data: Win32FindData;
        find_handle := FindFirstFileA(path, &find_data);

        if cast(s64, find_handle) == -1 {
            return false, files;
        }

        while true {
            file_entry: FileEntry = { name = convert_c_string(&find_data.cFileName); }
            if find_data.dwFileAttributes == FileAttribute.FILE_ATTRIBUTE_NORMAL {
                file_entry.type = FileType.File;
            }
            else if find_data.dwFileAttributes == FileAttribute.FILE_ATTRIBUTE_DIRECTORY {
                file_entry.type = FileType.Directory;
            }

            array_insert(&files, file_entry, allocator, reallocator);

            if !FindNextFileA(find_handle, &find_data) break;
        }

        FindClose(find_handle);
    }

    return true, files;
}

bool close_file(File file) {
    #if os == OS.Linux {
        success := close(file.handle);
        return success == 0;
    }
    #if os == OS.Windows {
        handle := cast(Handle*, file.handle);
        return CloseHandle(handle);
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
            handle := cast(Handle*, file.handle);
            size := SetFilePointer(handle, 0, null, MoveMethod.FILE_END);
            SetFilePointer(handle, 0, null, MoveMethod.FILE_BEGIN);

            file_contents = { length = size; data = allocator(size); }

            read: int;
            ReadFile(handle, file_contents.data, size, &read, null);
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

    buffer: StringBuffer;
    format_string_arguments(&buffer, format, args);
    write_buffer_to_file(file, &buffer.buffer, buffer.length);
}

write_to_file(File file, u8 char) {
    write_buffer_to_file(file, &char, 1);
}

write_buffer_to_file(File file, u8* buffer, s64 length) {
    #if os == OS.Linux {
        write(file.handle, buffer, length);
    }
    #if os == OS.Windows {
        handle := cast(Handle*, file.handle);
        written: int;
        WriteFile(handle, buffer, length, &written, null);
    }
}
