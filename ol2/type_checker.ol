#import math
#import "ir_builder.ol"
#import "polymorph.ol"
#import "type_table.ol"

init_types() {
}

init_necessary_types() {
}

verify_compiler_directives() {
    // Verify compiler directives, collecting run directives to be handled afterwards

    // Evaluate run directives on separate thread to handle metaprogramming
}

check_types() {
    // Process the ast queue, emitting a message on success/failure

    // If the metaprogram takes control, let the message queue flow until it has been cleared
    // - Wait for sent messages to be processed before moving to next flow
    // - Once fully typechecked, generate ir
    // - Send message that code is ready to be generated, wait for processing
    // - Send message that exe is ready to be linked, wait for processing
    // - Send message that exe has been linked, wait for processing and send null
}

bool add_global_variable(DeclarationAst* declaration) {
    return true;
}

add_function(FunctionAst* function) {
}

bool add_polymorphic_struct(StructAst* struct_ast) {
    return true;
}

bool add_struct(StructAst* struct_ast) {
    return true;
}

add_enum(EnumAst* enum_ast) {
}

add_union(UnionAst* union_ast) {
}

add_library(CompilerDirectiveAst* directive) {
}

add_system_library(CompilerDirectiveAst* directive) {
}

add_overload(OperatorOverloadAst* overload) {
}

add_interface(InterfaceAst* interface_ast) {
}

bool add_type(string name, TypeAst* type) {
    return add_type(name, type, type.file_index);
}

bool add_type(string name, TypeAst* type, int file_index) {
    return true;
}

TypeAst* get_type(string name, int file_index) {
    private_scope := private_scopes[file_index];

    if private_scope == null {
        // TODO Get from global scope
    }

    // TODO Get from private scope
    // TODO Get from global scope
    return null;
}

StructAst* get_polymorphic_struct(string name, int file_index) {
    return null;
}

verify_struct(StructAst* struct_ast) {

}

verify_union(UnionAst* union_ast) {

}

verify_interface(InterfaceAst* interface_ast) {

}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope) {
    _: bool;
    return verify_expression(ast, function, scope, &_, &_);
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope, bool* is_constant) {
    _: bool;
    return verify_expression(ast, function, scope, is_constant, &_);
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope, bool* is_constant, u32* array_length) {
    _: bool;
    return verify_expression(ast, function, scope, is_constant, &_, true, array_length);
}

TypeAst* verify_expression(Ast* ast, Function* function, Scope* scope, bool* is_constant, bool* is_type, bool get_array_length = false, u32* array_length = null) {
    return null;
}

TypeAst* verify_type(TypeDefinition* type, Scope* scope, int depth = 0) {
    _: bool;
    return verify_type(type, scope, &_, &_, &_, depth);
}

TypeAst* verify_type(TypeDefinition* type, Scope* scope, bool* is_generic, int depth = 0) {
    _: bool;
    return verify_type(type, scope, is_generic, &_, &_, depth);
}

TypeAst* verify_type(TypeDefinition* type, Scope* scope, bool* is_generic, bool* is_varargs, bool* is_params, int depth = 0, bool allow_params = false, int* initial_array_length = null) {
    if type == null return null;
    if type.baked_type return type.baked_type;

    if type.is_generic {
        if type.generics.length
            report_error("Generic type cannot have additional generic types", type);

        *is_generic = true;
        return null;
    }

    if type.compound {
        compound_type_name := print_compound_type(type);
        compound_type := get_type(compound_type_name, type.file_index);
        if compound_type return compound_type;

        types: Array<TypeAst*>[type.generics.length];
        size: u32;
        private_type := false;
        each generic, i in type.generics {
            has_generic := false;
            sub_type := verify_type(generic, scope, &has_generic);
            if sub_type == null {
                return null;
            }
            else {
                size += sub_type.size;
                types[i] = sub_type;
                if sub_type.private {
                    private_type = true;
                }
            }
        }

        return create_compound_type(types, compound_type_name, size, private_type, type.file_index);
    }

    if type.name == "Array" {
        if type.generics.length != 1 {
            report_error("Type 'Array' should have 1 generic type, but got %", type, type.generics.length);
            return null;
        }

        return verify_array(type, scope, depth, is_generic);
    }
    if type.name == "CArray" {
        if type.generics.length != 1 {
            report_error("Type 'CArray' should have 1 generic type, but got %", type, type.generics.length);
            return null;
        }

        element_type := verify_type(type.generics[0], scope, depth + 1);
        if element_type == null return null;

        array_length: u32;
        if initial_array_length {
            array_length = *initial_array_length;
        }
        else {
            is_constant := false;
            count_type := verify_expression(type.count, null, scope, &is_constant, &array_length);
            if count_type == null || count_type.type_kind != TypeKind.Integer || !is_constant || array_length < 0 {
                report_error("Expected size of C array to be a constant, positive integer", type);
                return null;
            }
        }

        // Format the name for the type with [array_length] at the end
        length := integer_length(array_length);
        padding := length + 2;
        name := print_type_definition(type, padding);
        name[name.length - padding] = '[';
        array_length_temp := array_length;
        each i in 2..length + 1 {
            digit := array_length_temp % 10;
            name[name.length - i] = digit + '0';
            array_length_temp /= 10;
        }
        name[name.length - 1] = ']';

        array_type := cast(ArrayType*, get_type(name, type.file_index));
        if array_type == null {
            array_type = create_ast<ArrayType>(null, AstType.Array);
            array_type.name = allocate_string(name);
            array_type.size = element_type.size * array_length;
            array_type.alignment = element_type.alignment;
            array_type.private = element_type.private;
            array_type.length = array_length;
            array_type.element_type = element_type;

            add_type(name, array_type);
            create_type_info(array_type);
        }
        return array_type;
    }

    if type.count != null {
        report_error("Type '%' cannot have a count", type, print_type_definition(type));
        return null;
    }

    has_generics := type.generics.length > 0;

    if type.name == "bool" {
        if has_generics
            report_error("Type 'bool' cannot have generics", type);

        return &bool_type;
    }
    if type.name == "string" {
        if has_generics
            report_error("Type 'string' cannot have generics", type);

        return string_type;
    }
    if type.name == "void" {
        if has_generics
            report_error("Type 'void' cannot have generics", type);

        return &void_type;
    }
    if type.name == "*" {
        if type.generics.length != 1 {
            report_error("Pointer type should have reference to 1 type, but got %", type, type.generics.length);
            return null;
        }

        pointer_type_name := print_type_definition(type);
        pointer_type := get_type(pointer_type_name, type.file_index);
        if pointer_type return pointer_type;

        pointed_to_type := verify_type(type.generics[0], scope, is_generic, depth + 1);
        if pointed_to_type == null return null;

        // There are some cases where the pointed to type is a struct that contains a field for the pointer type
        // To account for this, the type table needs to be checked for again for the type
        pointer_type = get_type(pointer_type_name, type.file_index);
        if pointer_type == null
            pointer_type = create_pointer_type(pointer_type_name, pointed_to_type);

        return pointer_type;
    }
    if type.name == "..." {
        if has_generics
            report_error("Type 'varargs' cannot have generics", type);

        *is_varargs = true;
        return null;
    }
    if type.name == "Params" {
        if (!allow_params) return null;
        if depth != 0 {
            report_error("Params can only be declared as a top level type, such as 'Params<int>'", type);
            return null;
        }

        switch type.generics.length {
            case 0; {
                *is_params = true;
                array_any := "Array<Any>"; #const
                found, array_type := table_get(global_scope.types, array_any);
                if found return array_type;

                return create_array_struct(array_any, any_type);
            }
            case 1; {
                *is_params = true;
                return verify_array(type, scope, depth, is_generic);
            }
        }

        report_error("Type 'Params' should have 1 generic type, but got %", type, type.generics.length);
        return null;
    }
    if type.name == "Type" {
        if has_generics
            report_error("Type 'Type' cannot have generics", type);

        return &type_type;
    }
    if type.name == "Any" {
        if has_generics
            report_error("Type 'Any' cannot have generics", type);

        return any_type;
    }
    // if type.name == "Code" {
    //     if has_generics || depth > 0 || !allow_params
    //         report_error("Type 'Code' must be a standalone type used as an argument", type);
    //
    //     return &code_type;
    // }

    if has_generics {
        generic_name := print_type_definition(type);
        struct_type := get_type(generic_name, type.file_index);
        if struct_type return struct_type;

        struct_def := get_polymorphic_struct(type.name, type.file_index);
        if struct_def == null {
            report_error("No polymorphic structs of type '%'", type, type.name);
            return null;
        }

        if struct_def.generics.length != type.generics.length {
            report_error("Expected type '%' to have % generic(s), but got %", type, type.name, struct_def.generics.length, type.generics.length);
            return null;
        }

        generic_types: Array<TypeAst*>[type.generics.length];
        private_generic_types := false;

        each generic, i in type.generics {
            has_generic: bool;
            generic_type := verify_type(generic, scope, &has_generic, depth + 1);
            if generic_type == null && !has_generic {
                return null;
            }
            else if has_generic {
                *is_generic = true;
            }
            else if generic_type.private {
                private_generic_types = true;
            }

            generic_types[i] = generic_type;
        }

        if is_generic return null;

        file_index := struct_def.file_index;
        if private_generic_types && !struct_def.private
            file_index = type.file_index;

        poly_struct := create_polymorphed_struct(struct_def, generic_name, TypeKind.Struct, private_generic_types, generic_types);
        add_type(generic_name, poly_struct, file_index);
        verify_struct(poly_struct);
        return poly_struct;
    }

    type_value := get_type(type.name, type.file_index);
    if type_value {
        switch type_value.ast_type {
            case AstType.Struct; {
                struct_ast := cast(StructAst*, type_value);
                if !struct_ast.verifying verify_struct(struct_ast);
            }
            case AstType.Union; {
                union_ast := cast(UnionAst*, type_value);
                if !union_ast.verifying verify_union(union_ast);
            }
            case AstType.Interface; {
                interface_ast := cast(InterfaceAst*, type_value);
                if !interface_ast.verifying verify_interface(interface_ast);
            }
        }
    }

    return type_value;
}

TypeAst* verify_array(TypeDefinition* type, Scope* scope, int depth, bool* is_generic) {
    element_type_def := type.generics[0];
    element_type := verify_type(element_type_def, scope, is_generic, depth + 1);
    if element_type == null return null;

    // Create a temporary string for the name of the array type
    name_data: Array<u8>[element_type.name.length + "Array<>".length];
    name: string = { length = name_data.length; data = name_data.data; }
    array_prefix := "Array<"; #const
    memory_copy(name.data, array_prefix.data, array_prefix.length);
    memory_copy(name.data + array_prefix.length, element_type.name.data, element_type.name.length);
    name[name.length - 1] = '>';

    array_type := get_type(name, type.file_index);
    if array_type return array_type;

    return create_array_struct(allocate_string(name), element_type, element_type_def);
}

TypeAst* create_array_struct(string name, TypeAst* element_type, TypeDefinition* element_type_def = null) {
    if base_array_type == null return null;

    array_struct := create_polymorphed_struct(base_array_type, name, TypeKind.Array, element_type.private, element_type);
    add_type(name, array_struct, element_type.file_index);
    verify_struct(array_struct);
    return array_struct;
}

TypeAst* create_pointer_type(string name, TypeAst* pointed_to_type) {
    pointer_type: PointerType = { file_index = pointed_to_type.file_index; name = allocate_string(name); private = pointed_to_type.private; pointed_type = pointed_to_type; }

    type_pointer := new<PointerType>();
    *type_pointer = pointer_type;

    add_type(name, type_pointer);
    create_type_info(type_pointer);
    return type_pointer;
}

TypeAst* create_compound_type(Array<TypeAst*> types, string name, u32 size, bool private_type, int file_index) {
    compound_type: CompoundType = { ast_type = AstType.Compound; file_index = file_index; name = allocate_string(name); size = size; private = private_type; types = allocate_array(types); }

    type_pointer := new<CompoundType>();
    *type_pointer = compound_type;

    add_type(name, type_pointer);
    create_type_info(type_pointer);
    return type_pointer;
}

string print_compound_type(TypeDefinition* type) {
    length := -2; // Start at -2 to offset the last type not ending with ", "
    each sub_type in type.generics {
        length += determine_type_definition_length(sub_type) + 2; // Add 2 for the ", " after each sub-type
    }

    result_data: Array<u8>[length];
    result: string = { length = length; data = result_data.data; }
    add_generics_to_string(type, result, 0);

    return result;
}

string print_type_definition(TypeDefinition* type, int padding = 0) #inline {
    _: bool;
    return print_type_definition(type, &_, padding);
}

string print_type_definition(TypeDefinition* type, bool* allocated, int padding = 0) #inline {
    if type == null return "";

    if padding == 0 {
        if type.baked_type {
            *allocated = true;
            return type.baked_type.name;
        }
        if type.generics.length == 0 {
            *allocated = true;
            return type.name;
        }
    }

    // Determine how much space to reserve
    length := determine_type_definition_length(type) + padding;
    result_data: Array<u8>[length];
    result: string = { length = length; data = result_data.data; }
    add_type_to_string(type, result, 0);

    return result;
}

base_array_type: StructAst*;
global_scope: GlobalScope;
private_scopes: Array<GlobalScope*>;

#private

string allocate_string(string input) {
    result: string = { length = input.length; data = allocate(input.length); }
    memory_copy(result.data, input.data, input.length);

    return result;
}

Array<T> allocate_array<T>(Array<T> input) {
    result: Array<T>;
    array_resize(&result, input.length, allocate, reallocate);

    each value, i in input {
        result[i] = value;
    }

    return result;
}

int determine_type_definition_length(TypeDefinition* type) {
    if type.baked_type return type.baked_type.name.length;
    if type.name == "*" return determine_type_definition_length(type.generics[0]) + 1;

    length := type.name.length;
    each generic in type.generics {
        length += determine_type_definition_length(generic) + 2; // Add 2 for the ", " after each generic
    }

    return length;
}

int add_type_to_string(TypeDefinition* type, string result, int index) {
    if type.baked_type {
        memory_copy(result.data + index, type.baked_type.name.data, type.baked_type.name.length);
        index += type.baked_type.name.length;
    }
    else if type.name == "*" {
        index = add_type_to_string(type.generics[0], result, index);
        result[index++] = '*';
    }
    else {
        memory_copy(result.data + index, type.name.data, type.name.length);
        index += type.name.length;
        if type.generics.length {
            result[index++] = '<';
            index = add_generics_to_string(type, result, index);
            result[index++] = '>';
        }
    }

    return index;
}

int add_generics_to_string(TypeDefinition* type, string result, int index) {
    i := 0;
    while i < type.generics.length - 1 {
        index = add_type_to_string(type.generics[i++], result, index);
        result[index++] = ',';
        result[index++] = ' ';
    }
    return add_type_to_string(type.generics[i], result, index);
}
