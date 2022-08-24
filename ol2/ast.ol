#import "assembly.ol"
#import "hash_table.ol"

enum AstType {
    None;
    Function;
    OperatorOverload;
    Interface;
    Scope;
    Type;
    Struct;
    StructField;
    StructFieldRef;
    Enum;
    EnumValue;
    Union;
    UnionField;
    Pointer;
    Array;
    Compound;
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

[flags]
enum AstFlags {
    None;

    // Relating to types
    IsType    = 0x1;
    Used      = 0x2;
    Signed    = 0x4;
    Private   = 0x8;
    Verified  = 0x10;
    Verifying = 0x20;

    // Misc
    Generic          = 0x40;
    Global           = 0x80;
    Constant         = 0x100;
    Returns          = 0x200;
    Final            = 0x400;
    Added            = 0x800;
    EnumValueDefined = 0x1000;

    // Struct field refs
    IsEnum               = 0x2000;
    ConstantStringLength = 0x4000;
    RawConstantString    = 0x8000;

    // ++ and --
    Prefixed = 0x10000;
    Positive = 0x20000;

    // Calls
    InlineCall        = 0x40000;
    PassArrayToParams = 0x80000;

    // Assembly
    FindStagingInputRegister = 0x100000;
    GetPointer               = 0x200000;
    Dereference              = 0x400000;

    // Type Definitions
    Compound = 0x10000000;
}

struct Ast {
    ast_type: AstType;
    flags: AstFlags;
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
}

struct InterfaceAst : TypeAst {
    return_type: TypeAst*;
    return_type_definition: TypeDefinition*;
    arguments: Array<DeclarationAst*>;
    argument_count: int;
}

struct Function : InterfaceAst {
    constant_count: int;
    function_index: int;
    function_flags: FunctionFlags;
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
}

struct Scope : Ast {
    parent: Scope*;
    identifiers: HashTable<string, Ast*>;
}

struct GlobalScope : Scope {
    functions: HashTable<string, Array<FunctionAst*>*>;
    polymorphic_structs: HashTable<string, StructAst*>;
    polymorphic_functions: HashTable<string, Array<FunctionAst*>*>;
}

struct ScopeAst : Scope {
    defer_count: int;
    children: Array<Ast*>;
    deferred_asts: Array<ScopeAst*>;
}

struct FunctionAst : Function {
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
    generics: Array<string>;
    generic_types: Array<TypeAst*>;
    fields: Array<StructFieldAst*>;
}

struct StructFieldAst : Declaration {
    offset: u32;
    attributes: Array<string>;
}

struct StructFieldRefAst : Ast {
    constant_index: int;
    constant_value: int;
    pointers: Array<bool>;
    types: Array<TypeAst*>;
    value_indices: Array<int>;
    children: Array<Ast*>;
}

struct EnumAst : TypeAst {
    attributes: Array<string>;
    base_type_definition: TypeDefinition*;
    base_type: TypeAst*;
    values: HashTable<string, EnumValueAst*>;
}

struct EnumValueAst : Ast {
    index: int;
    value: int;
    name: string;
}

struct PointerType : TypeAst {
    pointed_type: TypeAst*;
}

struct ArrayType : TypeAst {
    length: u32;
    element_type: TypeAst*;
}

struct UnionAst : TypeAst {
    fields: Array<UnionFieldAst*>;
}

struct UnionFieldAst : Ast {
    name: string;
    type_definition: TypeDefinition*;
    type: TypeAst*;
}

// @Note Since compound types cannot be set as struct types, the alignment doesn't matter
struct CompoundType : TypeAst {
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
    overload: OperatorOverloadAst*;
    l_value: Ast*;
    r_value: Ast*;
}

struct CompoundExpressionAst : Ast {
    children: Array<Ast*>;
}

struct ChangeByOneAst : Ast {
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
    function_pointer: InterfaceAst*;
    generics: Array<TypeDefinition*>;
    specified_arguments: HashTable<string, Ast*>;
    arguments: Array<Ast*>;
    type_info: TypeAst*;
}

struct DeclarationAst : Declaration {
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
}

struct CastAst : Ast {
    target_type_definition: TypeDefinition*;
    target_type: TypeAst*;
    value: Ast*;
}

struct OperatorOverloadAst : Function {
    op: Operator;
    type: TypeDefinition*;
}

struct AssemblyAst : Ast {
    instructions: Array<AssemblyInstructionAst*>;
    in_registers: HashTable<string, AssemblyInputAst*>;
    out_values: Array<AssemblyInputAst*>;
    assembly_bytes: Array<u8>;
}

struct AssemblyInputAst : Ast {
    register: string;
    register_definition: RegisterDefinition*;
    ast: Ast*;
    value: InstructionValue*;
}

struct AssemblyInstructionAst : Ast {
    instruction: string;
    value1: AssemblyValueAst*;
    value2: AssemblyValueAst*;
    definition: InstructionDefinition*;
}

struct AssemblyValueAst : Ast {
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
    statement: ScopeAst*;
}

struct TypeDefinition : Ast {
    name: string;
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
