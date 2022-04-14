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
}

struct Ast {
    type: AstType;
    file_index: s32;
    line: u32;
    column: u32;
}

struct Type {
    int FileIndex;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind;
    uint Size;
    uint Alignment;
    bool Used;
    bool Private;
}

struct Interface : Ast {
    string Name;
    IType ReturnType;
    TypeDefinition ReturnTypeDefinition;
    List<DeclarationAst> Arguments;
    int ArgumentCount;
}

interface IFunction : IInterface {
    int ConstantCount;
    int FunctionIndex;
    FunctionFlags Flags;
    List<string> Generics;
    ScopeAst Body;
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
    IAst Value;
    Dictionary<string, AssignmentAst> Assignments;
    List<IAst> ArrayValues;
}

struct Declaration : Values {
    TypeDefinition TypeDefinition;
    IType Type;
    IType ArrayElementType;
}

struct Scope {
    IScope Parent;
    IDictionary<string, IAst> Identifiers;
}

struct GlobalScope : IScope {
    IScope Parent; // This should never be set
    IDictionary<string, IAst> Identifiers;
    ConcurrentDictionary<string, List<FunctionAst>> Functions;
    ConcurrentDictionary<string, IType> Types;
    ConcurrentDictionary<string, StructAst> PolymorphicStructs;
    ConcurrentDictionary<string, List<FunctionAst>> PolymorphicFunctions;
}

struct PrivateScope : IScope {
    IScope Parent;
    IDictionary<string, IAst> Identifiers; = new Dictionary<string, IAst>();
    Dictionary<string, List<FunctionAst>> Functions;
    Dictionary<string, IType> Types;
    Dictionary<string, StructAst> PolymorphicStructs;
    Dictionary<string, List<FunctionAst>> PolymorphicFunctions;
}

struct ScopeAst : IScope, IAst {
    int FileIndex;
    uint Line;
    uint Column;
    bool Returns;
    IScope Parent;
    IDictionary<string, IAst> Identifiers;
    List<IAst> Children;
}

struct FunctionAst : IFunction, IType {
    int FileIndex;
    uint Line;
    uint Column;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.Function;
    int ConstantCount;
    int FunctionIndex;
    FunctionFlags Flags;
    uint Size; // Will always be 0
    uint Alignment; // Will always be 0
    bool Used;
    bool Private;
    int ArgumentCount;
    string ExternLib;
    string LibraryName;
    Library Library;
    int Syscall;
    IType ParamsElementType;
    IType ReturnType;
    TypeDefinition ReturnTypeDefinition;
    List<string> Generics;
    List<DeclarationAst> Arguments;
    List<Type[]> VarargsCallTypes;
    ScopeAst Body;
    List<string> Attributes;
}

struct StructAst : IAst, IType {
    int FileIndex;
    uint Line;
    uint Column;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind;
    uint Size;
    uint Alignment;
    bool Used;
    bool Private;
    List<string> Attributes;
    string BaseStructName;
    TypeDefinition BaseTypeDefinition;
    StructAst BaseStruct;
    bool Verified;
    bool Verifying;
    List<string> Generics;
    IType[] GenericTypes;
    List<StructFieldAst> Fields;
}

struct StructFieldAst : IDeclaration {
    int FileIndex;
    uint Line;
    uint Column;
    string Name;
    uint Offset;
    TypeDefinition TypeDefinition;
    IType Type;
    IType ArrayElementType;
    bool HasGenerics;
    IAst Value;
    Dictionary<string, AssignmentAst> Assignments;
    List<IAst> ArrayValues;
    List<string> Attributes;
}

struct StructFieldRefAst : IAst {
    bool IsEnum;
    bool IsConstant;
    bool GlobalConstant;
    bool ConstantStringLength;
    bool RawConstantString;
    int ConstantIndex;
    int ConstantValue;
    bool[] Pointers;
    IType[] Types;
    int[] ValueIndices;
    List<IAst> Children;
}

struct EnumAst : IAst, IType {
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.Enum;
    uint Size; = 4;
    uint Alignment; = 4;
    bool Used;
    bool Private;
    List<string> Attributes;
    TypeDefinition BaseTypeDefinition;
    PrimitiveAst BaseType;
    Dictionary<string, EnumValueAst> Values;
}

struct EnumValueAst : IAst {
    int Index;
    string Name;
    int Value;
    bool Defined;
}

struct PrimitiveAst : IAst, IType {
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind;
    uint Size;
    uint Alignment;
    bool Used;
    bool Private;
    bool Signed;
}

struct PointerType : IType {
    int FileIndex;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.Pointer;
    uint Size; = 8;
    uint Alignment; = 8;
    bool Used;
    bool Private;
    IType PointedType;
}

struct ArrayType : IType {
    int FileIndex;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.CArray;
    uint Size;
    uint Alignment;
    bool Used;
    bool Private;
    uint Length;
    IType ElementType;
}

struct UnionAst : IAst, IType {
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.Union;
    uint Size;
    uint Alignment;
    bool Used;
    bool Private;
    bool Verified;
    bool Verifying;
    List<UnionFieldAst> Fields;
}

struct UnionFieldAst : IAst {
    string Name;
    TypeDefinition TypeDefinition;
    IType Type;
}

struct CompoundType : IType {
    int FileIndex;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.Compound;
    uint Size;
    // @Note Since compound types cannot be set as struct types, the alignment doesn't matter
    uint Alignment;
    bool Used;
    bool Private;
    IType[] Types;
}

struct ReturnAst : IAst {
    IAst Value;
}

struct ConstantAst : IAst {
    string TypeName;
    IType Type;
    Constant Value;
    string String;
}

struct NullAst : IAst {
    IType TargetType;
}

struct IdentifierAst : IAst {
    string Name;
    IType BakedType;
    int? TypeIndex;
    int? FunctionTypeIndex;
}

struct ExpressionAst : IAst {
    IType Type;
    List<Operator> Operators;
    List<IType> ResultingTypes;
    Dictionary<int, OperatorOverloadAst> OperatorOverloads;
    List<IAst> Children;
}

struct CompoundExpressionAst : IAst {
    List<IAst> Children;
}

struct ChangeByOneAst : IAst {
    bool Prefix;
    bool Positive;
    IAst Value;
    IType Type;
}

struct UnaryAst : IAst {
    UnaryOperator Operator;
    IAst Value;
    IType Type;
}

struct CallAst : IAst {
    string Name;
    FunctionAst Function;
    IInterface Interface;
    int ExternIndex;
    List<TypeDefinition> Generics;
    Dictionary<string, IAst> SpecifiedArguments;
    List<IAst> Arguments;
    IType TypeInfo;
    bool Inline;
    bool PassArrayToParams;
}

struct DeclarationAst : IDeclaration {
    int FileIndex;
    uint Line;
    uint Column;
    string Name;
    bool Global;
    bool Private;
    bool Verified;
    TypeDefinition TypeDefinition;
    IType Type;
    IType ArrayElementType;
    bool HasGenerics;
    bool Constant;
    int ConstantIndex;
    int PointerIndex;
    IAst Value;
    Dictionary<string, AssignmentAst> Assignments;
    List<IAst> ArrayValues;
}

struct CompoundDeclarationAst : IDeclaration {
    VariableAst[] Variables;
    TypeDefinition TypeDefinition;
    IType Type;
    IType ArrayElementType;
    bool HasGenerics;
    IAst Value;
    Dictionary<string, AssignmentAst> Assignments;
    List<IAst> ArrayValues;
}

struct AssignmentAst : IValues {
    IAst Reference;
    Operator Operator;
    IAst Value;
    Dictionary<string, AssignmentAst> Assignments;
    List<IAst> ArrayValues;
}

struct ConditionalAst : IAst {
    IAst Condition;
    ScopeAst IfBlock;
    ScopeAst ElseBlock;
}

struct WhileAst : IAst {
    IAst Condition;
    ScopeAst Body;
}

struct EachAst : IAst {
    VariableAst IterationVariable;
    VariableAst IndexVariable;
    IAst Iteration;
    IAst RangeBegin;
    IAst RangeEnd;
    ScopeAst Body;
}

struct VariableAst : IAst {
    string Name;
    IType Type;
    int PointerIndex;
}

struct IndexAst : IAst {
    string Name;
    bool CallsOverload;
    OperatorOverloadAst Overload;
    IAst Index;
}

struct CompilerDirectiveAst : IAst {
    DirectiveType Type;
    IAst Value;
    Import Import;
    Library Library;
}

struct Import {
    string Name;
    string Path;
}

struct Library {
    string Name;
    string Path;
    string AbsolutePath;
    string FileName;
    string LibPath;
    bool HasLib;
    bool HasDll;
}

struct CastAst : IAst {
    TypeDefinition TargetTypeDefinition;
    IType TargetType;
    bool HasGenerics;
    IAst Value;
}

struct OperatorOverloadAst : IFunction {
    int FileIndex;
    uint Line;
    uint Column;
    string Name;
    int ConstantCount;
    int FunctionIndex;
    FunctionFlags Flags;
    Operator Operator;
    TypeDefinition Type;
    IType ReturnType;
    TypeDefinition ReturnTypeDefinition;
    List<string> Generics;
    List<DeclarationAst> Arguments;
    int ArgumentCount; = 2;
    ScopeAst Body;
}

struct InterfaceAst : IInterface, IType {
    int FileIndex;
    uint Line;
    uint Column;
    string Name;
    string BackendName;
    int TypeIndex;
    TypeKind TypeKind; = TypeKind.Interface;
    uint Size; = 8;
    uint Alignment; = 8;
    bool Used;
    bool Private;
    bool Verified;
    bool Verifying;
    IType ReturnType;
    TypeDefinition ReturnTypeDefinition;
    List<DeclarationAst> Arguments;
    int ArgumentCount;
}

struct AssemblyAst : IAst {
    List<AssemblyInstructionAst> Instructions;
    Dictionary<string, AssemblyInputAst> InRegisters;
    List<AssemblyInputAst> OutValues;
    bool FindStagingInputRegister;
    Byte[] AssemblyBytes;
}

struct AssemblyInputAst : IAst {
    string Register;
    RegisterDefinition RegisterDefinition;
    IAst Ast;
    bool GetPointer;
    InstructionValue Value;
}

struct AssemblyInstructionAst : IAst {
    string Instruction;
    AssemblyValueAst Value1;
    AssemblyValueAst Value2;
    InstructionDefinition Definition;
}

struct AssemblyValueAst : IAst {
    bool Dereference;
    string Register;
    RegisterDefinition RegisterDefinition;
    ConstantAst Constant;
}

struct SwitchAst : IAst {
    IAst Value;
    List<(List<IAst>, ScopeAst)> Cases;
    ScopeAst DefaultCase;
}

struct TypeDefinition : IAst {
    string Name;
    bool IsGeneric;
    bool Compound;
    int GenericIndex;
    int TypeIndex;
    List<TypeDefinition> Generics;
    IAst Count;
    uint? ConstCount;
    IType BakedType;
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
