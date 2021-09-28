main() {
    a := foo();
    b := bar();

    print_function_attributes(foo);
    print_function_attributes(bar);
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
        printf("Function %s attribute %d: %s\n", type_info.name, i, attribute);
    }
}

print_struct_attributes(Type type) {
    type_info := type_of(type);
    struct_type_info := cast(StructTypeInfo*, type_info);

    each attribute, i in struct_type_info.attributes {
        printf("Struct %s attribute %d: %s\n", type_info.name, i, attribute);
    }
}

print_enum_attributes(Type type) {
    type_info := type_of(type);
    enum_type_info := cast(EnumTypeInfo*, type_info);

    each attribute, i in enum_type_info.attributes {
        printf("Function %s attribute %d: %s\n", type_info.name, i, attribute);
    }
}

#run main();
