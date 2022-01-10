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
    Windows; // Not supported
    Mac;     // Not supported
}

os: OS; #const

interface void* Allocate(int size)
interface void* Reallocate(void* data, int size)
interface void* Free(void* data)


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


// Basic functions
printf(string format, ... args) #extern "c"
void* malloc(int size) #extern "c"
void* realloc(void* pointer, int size) #extern "c"
free(void* data) #extern "c"


// Runtime functions
int __start(int argc, u8** argv) {
    args: Array<string>[argc-1];
    each i in 1..argc-1 {
        args[i-1] = convert_c_string(argv[i]);
    }

    command_line_arguments = args;

    #if true main();

    return exit_code;
}

command_line_arguments: Array<string>;
exit_code := 0;
