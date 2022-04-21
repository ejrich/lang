#import "type_table.ol"

init_types() {
}

check_types() {
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

base_array_type: StructAst*;
global_scope: GlobalScope;
