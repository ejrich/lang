// Runtime library with types and main function

// Runtime structs
struct List<T> {
    int length;
    T* data;
}

struct string {
    int length;
    u8* data;
}

operator == string(string a, string b) {
    if (a.length != b.length) then return false;

    each i in 0..a.length-1 {
        if a[i] != b[i] then return false;
    }

    return true;
}

operator != string(string a, string b) {
    return !(a == b);
}

string convert_c_string(u8* string_pointer) {
    length := 0;
    while string_pointer[length] then length++;
    str: string = { length = length; data = string_pointer; }
    return str;
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
    u32 offset;
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

TypeInfo* type_of(Type type) {
    return __type_table[type];
}

u32 size_of(Type type) {
    return __type_table[type].size;
}


// Basic functions
printf(string format, ... args) #extern "libc"
exit(int exit_code) #extern "libc"


// Runtime functions
int __start(int argc, u8** argv) {
    exit_code := 0;
    args: List<string>[argc-1];

    each i in 1..argc-1 then args[i-1] = convert_c_string(argv[i]);

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

assert(bool assertion, int exit_code = 1) {
    if assertion then return;

    printf("Assertion failed\n");
    exit(exit_code);
}

assert(bool assertion, string message, int exit_code = 1) {
    if assertion then return;

    if message.length == 0 then printf("Assertion failed\n");
    else then printf("Assertion failed: %s\n", message);
    exit(exit_code);
}
