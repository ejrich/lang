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
    return "";
}

base_array_type: StructAst*;
global_scope: GlobalScope;
private_scopes: Array<GlobalScope*>;
