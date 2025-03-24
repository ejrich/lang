// ----------------- Runtime library with types and main function -----------------
// --------------------------------------------------------------------------------
// -- This module is included automatically with the build,
// -- so it does not need to be imported.

// Runtime structs and supporting functions
struct Array<T> {
    length: s64;
    data: T*;
}

struct string {
    length: s64;
    data: u8*;
}

empty_string := ""; #const

bool string_is_null(string value) {
    return value.data == null;
}

bool string_is_empty(string value) {
    if value.length == 0 return true;
    return value.data == null;
}

int string_compare(string a, string b) {
    min_len := a.length;
    if a.length > b.length min_len = b.length;

    each i in min_len {
        if a[i] > b[i] {
            return 1;
        }
        if a[i] < b[i] {
            return -1;
        }
    }

    if a.length > b.length return 1;
    if a.length < b.length return -1;
    return 0;
}

operator == (string a, string b) {
    if a.length != b.length return false;

    each i in a.length {
        if a[i] != b[i] return false;
    }

    return true;
}

operator != (string a, string b) {
    return !(a == b);
}

operator > (string a, string b) {
    min_len := a.length;
    if a.length > b.length min_len = b.length;

    each i in min_len {
        if a[i] > b[i] {
            return true;
        }
        if a[i] < b[i] {
            return false;
        }
    }

    return a.length > b.length;
}

operator < (string a, string b) {
    if a > b return false;
    return a != b;
}

string convert_c_string(u8* string_pointer) {
    length := 0;
    while string_pointer[length] length++;
    str: string = { length = length; data = string_pointer; }
    return str;
}

enum OS : u8 {
    None;
    Linux;
    Windows;
    Mac;     // Not supported
}

os: OS; #const

interface void* Allocate(u64 size)
interface void* Reallocate(void* data, u64 old_size, u64 size)
interface Free(void* data)
interface void* ThreadProcedure(void* arg)


// Runtime type information data
struct TypeInfo {
    name: string;
    type: TypeKind;
    size: u32;
}

struct IntegerTypeInfo : TypeInfo {
    signed: bool;
}

struct PointerTypeInfo : TypeInfo {
    pointer_type: TypeInfo*;
}

struct CArrayTypeInfo : TypeInfo {
    length: u32;
    element_type: TypeInfo*;
}

struct EnumTypeInfo : TypeInfo {
    base_type: TypeInfo*;
    values: Array<EnumValue>;
    attributes: Array<string>;
}

struct StructTypeInfo : TypeInfo {
    fields: Array<TypeField>;
    attributes: Array<string>;
}

struct UnionTypeInfo : TypeInfo {
    fields: Array<UnionField>;
}

struct CompoundTypeInfo : TypeInfo {
    types: Array<TypeInfo*>;
}

struct InterfaceTypeInfo : TypeInfo {
    return_type: TypeInfo*;
    arguments: Array<ArgumentType>;
}

struct FunctionTypeInfo : InterfaceTypeInfo {
    attributes: Array<string>;
}

enum TypeKind {
    None;
    Void;
    Boolean;
    Integer;
    Float;
    String;
    Pointer;
    Array;
    CArray;
    Enum;
    Struct;
    Union;
    Interface;
    Type;
    Any;
    Compound;
    Function;
}

struct TypeField {
    name: string;
    offset: u32;
    type_info: TypeInfo*;
    attributes: Array<string>;
}

struct UnionField {
    name: string;
    type_info: TypeInfo*;
}

struct EnumValue {
    name: string;
    value: s64;
}

struct ArgumentType {
    name: string;
    type_info: TypeInfo*;
}

__type_table: Array<TypeInfo*>;

TypeInfo* type_of(Type type) {
    return __type_table[type];
}

u32 size_of(Type type) {
    return __type_table[type].size;
}

struct Any {
    type: TypeInfo*;
    data: void*;
}


// Runtime functions
void __start(int argc, u8** argv) {
    #if os == OS.Windows {
        command_line := GetCommandLineA();
        arg_string := convert_c_string(command_line);
        argc = 0;
        index := 0;
        while index < arg_string.length {
            // If arg starts with ", move to the next non-escaped quote
            if arg_string[index] == '"' {
                index++;
                while index < arg_string.length {
                    if arg_string[index++] == '"' && arg_string[index - 2] != '\\' {
                        break;
                    }
                }
            }
            // Otherwise move to the next space
            else {
                while index < arg_string.length {
                    if arg_string[index++] == ' ' {
                        break;
                    }
                }
            }

            argc++;

            // Move to the next character
            while index < arg_string.length {
                if arg_string[index++] != ' ' {
                    break;
                }
            }
        }
    }

    args: Array<string>[argc-1];

    #if os == OS.Windows {
        index = 0;
        arg_index := 0;
        while index < arg_string.length {
            arg_start_index := index;
            // If arg starts with ", move to the next non-escaped quote
            if arg_string[index] == '"' {
                index++;
                while index < arg_string.length {
                    if arg_string[index++] == '"' && arg_string[index - 2] != '\\' {
                        break;
                    }
                }
            }
            // Otherwise move to the next space
            else {
                while index < arg_string.length {
                    if arg_string[index++] == ' ' {
                        break;
                    }
                }
            }

            if arg_index > 0 {
                args[arg_index - 1] = { length = index - arg_start_index; data = arg_string.data + arg_start_index; }
            }
            arg_index++;

            // Move to the next character
            while index < arg_string.length {
                if arg_string[index] != ' ' {
                    break;
                }
                index++;
            }
        }
    }
    else {
        each i in 1..argc-1 {
            args[i-1] = convert_c_string(argv[i]);
        }
    }

    __command_line_arguments = args;

    main();

    exit_program(__exit_code);
}

Array<string> get_command_line_arguments() {
    return __command_line_arguments;
}

set_exit_code(int code) {
    __exit_code = code;
}

exit_program(int exit_code) {
    each callback in __exit_callbacks {
        if callback != null callback();
    }

    #if os == OS.Linux {
        exit_group(exit_code);
    }
    #if os == OS.Windows {
        ExitProcess(exit_code);
    }
}

interface ExitCallback()

Array<ExitCallback>* get_exit_callbacks() {
    return &__exit_callbacks;
}

#private

__exit_code := 0;
__exit_callbacks: Array<ExitCallback>;
__command_line_arguments: Array<string>;
