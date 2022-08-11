void_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "void"; type_kind = TypeKind.Void;    size = 1; alignment = 1; }
bool_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "bool"; type_kind = TypeKind.Boolean; size = 1; alignment = 1; }
u8_type:   TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "u8";   type_kind = TypeKind.Integer; size = 1; alignment = 1; }
u16_type:  TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "u16";  type_kind = TypeKind.Integer; size = 2; alignment = 2; }
u32_type:  TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "u32";  type_kind = TypeKind.Integer; size = 4; alignment = 4; }
u64_type:  TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "s64";  type_kind = TypeKind.Integer; size = 8; alignment = 8; }

s8_type:  TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType | AstFlags.Signed; name = "s8";  type_kind = TypeKind.Integer; size = 1; alignment = 1; }
s16_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType | AstFlags.Signed; name = "s16"; type_kind = TypeKind.Integer; size = 2; alignment = 2; }
s32_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType | AstFlags.Signed; name = "s32"; type_kind = TypeKind.Integer; size = 4; alignment = 4; }
s64_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType | AstFlags.Signed; name = "s64"; type_kind = TypeKind.Integer; size = 8; alignment = 8; }

float_type:   TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType | AstFlags.Signed; name = "float";   type_kind = TypeKind.Float; size = 4; alignment = 4; }
float64_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType | AstFlags.Signed; name = "float64"; type_kind = TypeKind.Float; size = 8; alignment = 8; }

type_type: TypeAst = { ast_type = AstType.Type; flags = AstFlags.IsType; name = "Type"; type_kind = TypeKind.Type; size = 4; alignment = 4; }

string_type: StructAst*;
raw_string_type: TypeAst*;

any_type: StructAst*;
type_info_pointer_type: TypeAst*;
void_pointer_type: TypeAst*;

int get_function_index() {
    return 0;
}


add_to_type_table(TypeAst* type) {

}

create_type_info(TypeAst* type) {
    if errors.length return;
}
