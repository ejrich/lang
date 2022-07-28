void_type: PrimitiveAst = { name = "void"; type_kind = TypeKind.Void;    size = 1; alignment = 1; }
bool_type: PrimitiveAst = { name = "bool"; type_kind = TypeKind.Boolean; size = 1; alignment = 1; }
s8_type:   PrimitiveAst = { name = "s8";   type_kind = TypeKind.Integer; size = 1; alignment = 1; }
u8_type:   PrimitiveAst = { name = "u8";   type_kind = TypeKind.Integer; size = 1; alignment = 1; }
s16_type:  PrimitiveAst = { name = "s16";  type_kind = TypeKind.Integer; size = 2; alignment = 2; }
u16_type:  PrimitiveAst = { name = "u16";  type_kind = TypeKind.Integer; size = 2; alignment = 2; }
s32_type:  PrimitiveAst = { name = "s32";  type_kind = TypeKind.Integer; size = 4; alignment = 4; }
u32_type:  PrimitiveAst = { name = "u32";  type_kind = TypeKind.Integer; size = 4; alignment = 4; }
s64_type:  PrimitiveAst = { name = "s64";  type_kind = TypeKind.Integer; size = 8; alignment = 8; }
u64_type:  PrimitiveAst = { name = "s64";  type_kind = TypeKind.Integer; size = 8; alignment = 8; }

float_type:   PrimitiveAst = { name = "float";   type_kind = TypeKind.Float; size = 4; alignment = 4; }
float64_type: PrimitiveAst = { name = "float64"; type_kind = TypeKind.Float; size = 8; alignment = 8; }

type_type: PrimitiveAst = { name = "Type"; type_kind = TypeKind.Type; size = 4; alignment = 4; }

string_type: StructAst*;
raw_string_type: TypeAst*;

any_type: StructAst*;
type_info_pointer_type: TypeAst*;
void_pointer_type: TypeAst*;


add_to_type_table(TypeAst* type) {

}

create_type_info(TypeAst* type) {

}
