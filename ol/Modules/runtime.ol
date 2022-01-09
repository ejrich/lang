// ----------------- Runtime library with types and main function -----------------
// --------------------------------------------------------------------------------
// -- This module is included automatically with the build,
// -- so it does not need to be imported.

// Runtime structs
struct Array<T> {
    length: s64;
    data: T*;
}

ARRAY_BLOCK_SIZE := 10; #const

array_insert<T>(Array<T>* array, T value, Allocate allocator = null, Reallocate reallocator = null) {
    // Reallocate the array if necessary
    length := array.length;
    if length % ARRAY_BLOCK_SIZE == 0 {
        // @Future add custom allocators
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

interface void* Allocate(int size)
interface void* Reallocate(void* data, int size)
interface void* Free(void* data)

enum OS : u8 {
    None;
    Linux;
    Windows; // Not supported
    Mac;     // Not supported
}

os: OS; #const

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
void* memcpy(void* dest, void* src, int length) #extern "c"


// Runtime functions
int __start(int argc, u8** argv) {
    each i in 1..argc-1 {
        argument := convert_c_string(argv[i]);
        array_insert(&command_line_arguments, argument);
    }

    #if true main();

    return exit_code;
}

command_line_arguments: Array<string>;
exit_code := 0;
