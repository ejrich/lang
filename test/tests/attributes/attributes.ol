#import standard

main() {
    a := foo();
    b := bar();

    print_function_attributes(foo);
    print_function_attributes(bar);

    print_struct_attributes(FooBar);
    print_struct_attributes(StructWithFieldAttributes);

    print_enum_attributes(EnumFlags);
}

[something]
int foo() {
    return 8;
}

[multiple, attributes]
int bar() {
    return 20;
}

print_function_attributes(Type type) {
    type_info := type_of(type);
    function_type_info := cast(FunctionTypeInfo*, type_info);

    each attribute, i in function_type_info.attributes {
        print("Function % attribute %: %\n", type_info.name, i, attribute);
    }
}

[something]
struct FooBar {
    a: int;
    b: float;
}

struct StructWithFieldAttributes {
    [something]
    a: int;
    [foo, bar]
    b: float;
}

print_struct_attributes(Type type) {
    type_info := type_of(type);
    struct_type_info := cast(StructTypeInfo*, type_info);

    each attribute, i in struct_type_info.attributes {
        print("Struct % attribute %: %\n", type_info.name, i, attribute);
    }

    each field in struct_type_info.fields {
        each attribute, i in field.attributes {
            print("Struct % field % attribute %: %\n", type_info.name, field.name, i, attribute);
        }
    }
}

[flags]
enum EnumFlags {
    Apple;
    Orange;
    Banana;
}

print_enum_attributes(Type type) {
    type_info := type_of(type);
    enum_type_info := cast(EnumTypeInfo*, type_info);

    each attribute, i in enum_type_info.attributes {
        print("Enum % attribute %: %\n", type_info.name, i, attribute);
    }
}

#run main();
