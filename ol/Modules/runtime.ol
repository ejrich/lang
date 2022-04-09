// ----------------- Runtime library with types and main function -----------------
// --------------------------------------------------------------------------------
// -- This module is included automatically with the build,
// -- so it does not need to be imported.

#if os == OS.Linux {
    #import linux
}
#if os == OS.Windows {
    #import windows
}

// Runtime structs and supporting functions
struct Array<T> {
    length: s64;
    data: T*;
}

struct string {
    length: s64;
    data: u8*;
}

bool string_is_null(string value) {
    return value.data == null;
}

bool string_is_empty(string value) {
    if value.length == 0 return true;
    return value.data == null;
}

operator == (string a, string b) {
    if a.length != b.length return false;

    each i in 0..a.length-1 {
        if a[i] != b[i] return false;
    }

    return true;
}

operator != (string a, string b) {
    return !(a == b);
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

interface void* Allocate(int size)
interface void* Reallocate(void* data, int old_size, int size)
interface void* Free(void* data)
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
    types: Array<TypeInfo>;
}

struct InterfaceTypeInfo : TypeInfo {
    return_type: TypeInfo*;
    arguments: Array<ArgumentType>;
}

struct FunctionTypeInfo : InterfaceTypeInfo {
    attributes: Array<string>;
}

enum TypeKind {
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
    value: int;
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
    args: Array<string>[argc-1];
    each i in 1..argc-1 {
        args[i-1] = convert_c_string(argv[i]);
    }

    command_line_arguments = args;

    #if true main();

    exit_program(exit_code);
}

exit_program(int exit_code) {
    each callback in exit_callbacks {
        callback();
    }

    #if os == OS.Linux {
        exit_group(exit_code);
    }
    #if os == OS.Windows {
        ExitProcess(exit_code);
    }
}

interface ExitCallback()
exit_callbacks: Array<ExitCallback>;

command_line_arguments: Array<string>;
exit_code := 0;
