#import "ir_builder.ol"
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

TypeAst* verify_type(TypeDefinition* type_def, Scope* scope) {
    return null;
}

string print_type_definition(TypeDefinition* type) {
    if type.baked_type return type.baked_type.name;
    if type.generics.length == 0 return type.name;

    // Determine how much space to reserve
    length := determine_type_definition_length(type);

    result: string = { length = length; data = allocate(length); }
    add_type_to_string(type, result, 0);

    return result;
}

base_array_type: StructAst*;
global_scope: GlobalScope;
private_scopes: Array<GlobalScope*>;

#private

int determine_type_definition_length(TypeDefinition* type) {
    if type.baked_type return type.baked_type.name.length;

    length := type.name.length;
    if type.name == "*" {
        length += determine_type_definition_length(type.generics[0]);
    }
    else {
        each generic in type.generics {
            length += determine_type_definition_length(generic) + 2; // Add 2 for the ", " after each generic
        }
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
            i := 0;
            while i < type.generics.length - 1 {
                index = add_type_to_string(type.generics[i++], result, index);
                result[index++] = ',';
                result[index++] = ' ';
            }
            index = add_type_to_string(type.generics[i], result, index);
            result[index++] = '>';
        }
    }

    return index;
}
