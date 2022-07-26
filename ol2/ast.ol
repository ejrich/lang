#import "assembly.ol"
#import "hash_table.ol"

enum AstType {
    None;
    Function;
    OperatorOverload;
    Interface;
    Scope;
    Struct;
    StructField;
    StructFieldRef;
    Enum;
    EnumValue;
    Union;
    UnionField;
    Primitive;
    Pointer;
    Array;
    Return;
    Constant;
    Null;
    Identifier;
    Expression;
    CompoundExpression;
    ChangeByOne;
    Unary;
    Call;
    Declaration;
    CompoundDeclaration;
    Assignment;
    Conditional;
    While;
    Each;
    Variable;
    Index;
    CompilerDirective;
    Cast;
    Assembly;
    AssemblyInput;
    AssemblyInstruction;
    AssemblyValue;
    Switch;
    TypeDefinition;
    Break;
    Continue;
    Defer;
}

struct Ast {
    ast_type: AstType;
    file_index: s32;
    line: u32;
    column: u32;
}

struct TypeAst : Ast {
    name: string;
    type_index: int;
    type_kind: TypeKind;
    size: u32;
    alignment: u32;
    used: bool;
    private: bool;
}

struct Interface : TypeAst {
    return_type: TypeAst*;
    return_type_definition: TypeDefinition*;
    arguments: Array<DeclarationAst*>;
    argument_count: int;
}

struct Function : Interface {
    constant_count: int;
    function_index: int;
    flags: FunctionFlags;
    generics: Array<string>;
    body: ScopeAst*;
}

[flags]
enum FunctionFlags {
    Extern                = 0x1;
    Compiler              = 0x2;
    Syscall               = 0x4;
    Varargs               = 0x8;
    Params                = 0x10;
    DefinitionVerified    = 0x20;
    Verified              = 0x40;
    HasDirectives         = 0x80;
    CallsCompiler         = 0x100;
    ReturnVoidAtEnd       = 0x200;
    ReturnTypeHasGenerics = 0x400;
    PrintIR               = 0x800;
    ExternInitted         = 0x1000;
    Queued                = 0x2000;
    PassCallLocation      = 0x4000;
    Inline                = 0x8000;
}

struct Values : Ast {
    value: Ast*;
    assignments: HashTable<string, AssignmentAst*>*;
    array_values: Array<Ast*>;
}

struct Declaration : Values {
    name: string;
    type_definition: TypeDefinition*;
    type: TypeAst*;
    array_element_type: TypeAst*;
    has_generics: bool;
}

struct Scope : Ast {
    parent: Scope*;
    identifiers: HashTable<string, Ast*>;
}

struct GlobalScope : Scope {
    functions: HashTable<string, Array<FunctionAst*>>;
    types: HashTable<string, TypeAst*>;
    polymorphic_structs: HashTable<string, StructAst*>;
    polymorphic_functions: HashTable<string, Array<FunctionAst*>>;
}

struct ScopeAst : Scope {
    returns: bool;
    defer_count: int;
    children: Array<Ast*>;
    deferred_asts: Array<ScopeAst*>;
}

struct FunctionAst : Function {
    // TypeKind TypeKind; = TypeKind.Function;
    extern_lib: string;
    library_name: string;
    library: Library*;
    syscall: int;
    params_element_type: TypeAst*;
    attributes: Array<string>;
}

struct StructAst : TypeAst {
    attributes: Array<string>;
    base_struct_name: string;
    base_type_definition: TypeDefinition*;
    base_struct: StructAst*;
    verified: bool;
    verifying: bool;
    generics: Array<string>;
    generic_types: Array<TypeAst*>;
    fields: Array<StructFieldAst*>;
}

struct StructFieldAst : Declaration {
    offset: u32;
    attributes: Array<string>;
}

struct StructFieldRefAst : Ast {
    is_enum: bool;
    is_constant: bool;
    global_constant: bool;
    constant_string_length: bool;
    raw_constant_string: bool;
    constant_index: int;
    constant_value: int;
    pointers: Array<bool>;
    types: Array<TypeAst*>;
    value_indices: Array<int>;
    children: Array<Ast*>;
}

struct EnumAst : TypeAst {
    // TypeKind TypeKind; = TypeKind.Enum;
    // uint Size; = 4;
    // uint Alignment; = 4;
    attributes: Array<string>;
    base_type_definition: TypeDefinition*;
    base_type: PrimitiveAst*;
    values: HashTable<string, EnumValueAst*>;
}

struct EnumValueAst : Ast {
    index: int;
    value: int;
    name: string;
    defined: bool;
}

struct PrimitiveAst : TypeAst {
    signed: bool;
}

struct PointerType : TypeAst {
    // TypeKind TypeKind; = TypeKind.Pointer;
    // uint Size; = 8;
    // uint Alignment; = 8;
    pointed_type: TypeAst*;
}

struct ArrayType : TypeAst {
    // TypeKind TypeKind; = TypeKind.CArray;
    length: u32;
    element_type: TypeAst*;
}

struct UnionAst : TypeAst {
    // TypeKind TypeKind; = TypeKind.Union;
    verified: bool;
    verifying: bool;
    fields: Array<UnionFieldAst*>;
}

struct UnionFieldAst : Ast {
    name: string;
    type_definition: TypeDefinition*;
    type: TypeAst*;
}

// @Note Since compound types cannot be set as struct types, the alignment doesn't matter
struct CompoundType : TypeAst {
    // TypeKind TypeKind; = TypeKind.Compound;
    types: Array<TypeAst*>;
}

struct ReturnAst : Ast {
    value: Ast*;
}

struct ConstantAst : Ast {
    type: TypeAst*;
    value: Constant;
    string: string;
}

struct NullAst : Ast {
    target_type: TypeAst*;
}

struct IdentifierAst : Ast {
    name: string;
    baked_type: TypeAst*;
    type_index := -1;
    function_type_index := -1;
}

struct ExpressionAst : Ast {
    type: TypeAst*;
    op: Operator;
    resulting_type: TypeAst*;
    overload: OperatorOverloadAst*;
    l_value: Ast*;
    r_value: Ast*;

    // operators: Array<Operator>;
    // resulting_types: Array<TypeAst*>;
    // operator_overloads: HashTable<int, OperatorOverloadAst>;
    // children: Array<Ast*>;
}

struct CompoundExpressionAst : Ast {
    children: Array<Ast*>;
}

struct ChangeByOneAst : Ast {
    prefix: bool;
    positive: bool;
    value: Ast*;
    type: TypeAst*;
}

struct UnaryAst : Ast {
    op: UnaryOperator;
    value: Ast*;
    type: TypeAst*;
}

struct CallAst : Ast {
    name: string;
    function: FunctionAst*;
    function_pointer: Interface*;
    generics: Array<TypeDefinition*>;
    specified_arguments: HashTable<string, Ast*>;
    arguments: Array<Ast*>;
    type_info: TypeAst*;
    inline: bool;
    pass_array_to_params: bool;
}

struct DeclarationAst : Declaration {
    global: bool;
    private: bool;
    constant: bool;
    constant_index: int;
    pointer_index: int;
}

struct CompoundDeclarationAst : Declaration {
    variables: Array<VariableAst*>;
}

struct AssignmentAst : Values {
    reference: Ast*;
    op: Operator;
}

struct ConditionalAst : Ast {
    condition: Ast*;
    if_block: ScopeAst*;
    else_block: ScopeAst*;
}

struct WhileAst : Ast {
    condition: Ast*;
    body: ScopeAst*;
}

struct EachAst : Ast {
    iteration_variable: VariableAst*;
    index_variable: VariableAst*;
    iteration: Ast*;
    range_begin: Ast*;
    range_end: Ast*;
    body: ScopeAst*;
}

struct VariableAst : Ast {
    name: string;
    type: TypeAst*;
    pointer_index: int;
}

struct IndexAst : Ast {
    name: string;
    calls_overload: bool;
    overload: OperatorOverloadAst*;
    index: Ast*;
}

struct CompilerDirectiveAst : Ast {
    directive_type: DirectiveType;
    value: Ast*;
    import: Import;
    library: Library;
}

struct Import {
    name: string;
    path: string;
}

struct Library {
    name: string;
    path: string;
    absolute_path: string;
    file_name: string;
    lib_path: string;
    has_lib: bool;
    has_dll: bool;
}

struct CastAst : Ast {
    target_type_definition: TypeDefinition*;
    target_type: TypeAst*;
    has_generics: bool;
    value: Ast*;
}

struct OperatorOverloadAst : Function {
    op: Operator;
    type: TypeDefinition*;
}

struct InterfaceAst : Interface {
    // TypeKind TypeKind; = TypeKind.Interface;
    // uint Size; = 8;
    // uint Alignment; = 8;
    verified: bool;
    verifying: bool;
}

struct AssemblyAst : Ast {
    instructions: Array<AssemblyInstructionAst*>;
    in_registers: HashTable<string, AssemblyInputAst*>;
    out_values: Array<AssemblyInputAst*>;
    find_staging_input_register: bool;
    assembly_bytes: Array<u8>;
}

struct AssemblyInputAst : Ast {
    register: string;
    register_definition: RegisterDefinition*;
    ast: Ast*;
    get_pointer: bool;
    value: InstructionValue*;
}

struct AssemblyInstructionAst : Ast {
    instruction: string;
    value1: AssemblyValueAst*;
    value2: AssemblyValueAst*;
    definition: InstructionDefinition*;
}

struct AssemblyValueAst : Ast {
    dereference: bool;
    register: string;
    register_definition: RegisterDefinition*;
    constant: ConstantAst*;
}

struct SwitchAst : Ast {
    value: Ast*;
    cases: Array<SwitchCases>;
    default_case: ScopeAst*;
}

struct SwitchCases {
    first_case: Ast*;
    additional_cases: Array<Ast*>;
    body: ScopeAst*;
}

struct DeferAst : Ast {
    added: bool;
    statement: ScopeAst*;
}

struct TypeDefinition : Ast {
    name: string;
    is_generic: bool;
    compound: bool;
    generic_index: int;
    type_index: int;
    generics: Array<TypeDefinition*>;
    count: Ast*;
    const_count := -1;
    baked_type: TypeAst*;
}

enum Operator {
    None;
    And;              // &&
    Or;               // ||
    Equality;         // ==
    NotEqual;         // !=
    GreaterThanEqual; // >=
    LessThanEqual;    // <=
    ShiftLeft;        // <<
    ShiftRight;       // >>
    RotateLeft;       // <<<
    RotateRight;      // >>>
    Subscript;        // []
    Add         = '+';
    Subtract    = '-';
    Multiply    = '*';
    Divide      = '/';
    GreaterThan = '>';
    LessThan    = '<';
    BitwiseOr   = '|';
    BitwiseAnd  = '&';
    Xor         = '^';
    Modulus     = '%';
}

enum UnaryOperator {
    Not = '!';
    Negate = '-';
    Dereference = '*';
    Reference = '&';
}

enum DirectiveType {
    None;
    Run;
    If;
    Assert;
    ImportModule;
    ImportFile;
    Library;
    SystemLibrary;
}
