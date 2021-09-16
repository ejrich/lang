// Runtime library with types and main function

// Runtime structs
struct List<T> {
    int length;
    T* data;
}

// @Future Update strings to use this struct
struct string {
    int length;
    u8* data;
}


// Runtime type information data
struct TypeInfo {
    string name;
    TypeKind type;
    u32 size;
    List<TypeField> fields;
    List<EnumValue> enum_values;
    TypeInfo* return_type;
    List<ArgumentType> arguments;
}

enum TypeKind {
    Void;
    Boolean;
    Integer;
    Float;
    String;
    Pointer;
    List;
    Enum;
    Struct;
    Function;
}

struct TypeField {
    string name;
    TypeInfo* type_info;
}

struct EnumValue {
    string name;
    int value;
}

struct ArgumentType {
    string name;
    TypeInfo* type_info;
}

__type_table: List<TypeInfo*>;

TypeInfo type_of(Type type) {
    return *__type_table[type];
}

u32 size_of(Type type) {
    return __type_table[type].size;
}


// Basic IO functions
printf(string format, ... args) #extern "libc"


// Runtime functions
int __start(int argc, string* argv) {
    exit_code := 0;
    args: List<string>[argc-1];

    each i in 1..argc-1 then args[i-1] = *(argv + i);

    #assert type_of(main).type == TypeKind.Function;
    #if type_of(main).return_type.type == TypeKind.Void {
        #if type_of(main).arguments.length == 0 then main();
        else then main(args);
    }
    else {
        #if type_of(main).arguments.length == 0 then exit_code = main();
        else then exit_code = main(args);
    }

    return exit_code;
}
