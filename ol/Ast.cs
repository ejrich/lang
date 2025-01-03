using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ol;

public interface IAst
{
    int FileIndex { get; set; }
    uint Line { get; init; }
    uint Column { get; init; }
}

public interface IType
{
    int FileIndex { get; set; }
    string Name { get; set; }
    int TypeIndex { get; set; }
    TypeKind TypeKind { get; set; }
    uint Size { get; set; }
    uint Alignment { get; set; }
    bool Used { get; set; }
    bool Private { get; set; }
}

public interface IInterface : IAst
{
    string Name { get; set; }
    FunctionFlags Flags { get; set; }
    IType ReturnType { get; set; }
    TypeDefinition ReturnTypeDefinition { get; set; }
    List<string> Generics { get; }
    List<DeclarationAst> Arguments { get; }
    int ArgumentCount { get; set; }
}

public interface IFunction : IInterface
{
    int ConstantCount { get; set; }
    int FunctionIndex { get; set; }
    ScopeAst Body { get; set; }
    IntPtr MessagePointer { get; set; }
}

[Flags]
public enum FunctionFlags
{
    Extern = 0x1,
    Compiler = 0x2,
    Syscall = 0x4,
    Varargs = 0x8,
    Params = 0x10,
    DefinitionVerified = 0x20,
    Verified = 0x40,
    HasDirectives = 0x80,
    CallsCompiler = 0x100,
    ReturnVoidAtEnd = 0x200,
    ReturnTypeHasGenerics = 0x400,
    PrintIR = 0x800,
    Queued = 0x1000,
    PassCallLocation = 0x2000,
    Inline = 0x4000,
    PublicFlagsMask = 0x400F
}

public interface IValues : IAst
{
    IAst Value { get; set; }
    Dictionary<string, AssignmentAst> Assignments { get; set; }
    List<Values> ArrayValues { get; set; }
}

public class Values : IValues
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IAst Value { get; set; }
    public Dictionary<string, AssignmentAst> Assignments { get; set; }
    public List<Values> ArrayValues { get; set; }
}

public interface IDeclaration : IValues
{
    TypeDefinition TypeDefinition { get; set; }
    IType Type { get; set; }
    IType ArrayElementType { get; set; }
}

public interface IScope
{
    IScope Parent { get; set; }
    IDictionary<string, IAst> Identifiers { get; }
}

public class GlobalScope : IScope
{
    public IScope Parent { get; set; } // This should never be set
    public IDictionary<string, IAst> Identifiers { get; } = new ConcurrentDictionary<string, IAst>();
    public ConcurrentDictionary<string, List<FunctionAst>> Functions { get; } = new();
    public ConcurrentDictionary<string, IType> Types { get; } = new();
    public ConcurrentDictionary<string, StructAst> PolymorphicStructs = new();
    public ConcurrentDictionary<string, InterfaceAst> PolymorphicInterfaces = new();
    public ConcurrentDictionary<string, List<FunctionAst>> PolymorphicFunctions = new();
}

public class PrivateScope : IScope
{
    public IScope Parent { get; set; }
    public IDictionary<string, IAst> Identifiers { get; } = new Dictionary<string, IAst>();
    public Dictionary<string, List<FunctionAst>> Functions { get; } = new();
    public Dictionary<string, IType> Types { get; } = new();
    public Dictionary<string, StructAst> PolymorphicStructs = new();
    public Dictionary<string, InterfaceAst> PolymorphicInterfaces = new();
    public Dictionary<string, List<FunctionAst>> PolymorphicFunctions = new();
}

public class ScopeAst : IScope, IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public bool Returns { get; set; }
    public bool Breaks { get; set; }
    public IScope Parent { get; set; }
    public IDictionary<string, IAst> Identifiers { get; } = new Dictionary<string, IAst>();
    public List<IAst> Children { get; } = new();
    public int DeferCount { get; set; }
    public List<ScopeAst> DeferredAsts { get; set; }
}

public class FunctionAst : IFunction, IType
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.Function;
    public int ConstantCount { get; set; }
    public int FunctionIndex { get; set; }
    public FunctionFlags Flags { get; set; }
    public uint Size { get; set; } // Will always be 0
    public uint Alignment { get; set; } // Will always be 0
    public bool Used { get; set; }
    public bool Private { get; set; }
    public int ArgumentCount { get; set; }
    public string ExternLib { get; set; }
    public string LibraryName { get; set; }
    public Library Library { get; set; }
    public int Syscall { get; set; }
    public IType ParamsElementType { get; set; }
    public IType ReturnType { get; set; }
    public TypeDefinition ReturnTypeDefinition { get; set; }
    public List<string> Generics { get; } = new();
    public List<DeclarationAst> Arguments { get; } = new();
    public ScopeAst Body { get; set; }
    public List<string> Attributes { get; set; }
    public IntPtr MessagePointer { get; set; }
}

public class StructAst : IAst, IType
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; }
    public uint Size { get; set; }
    public uint Alignment { get; set; }
    public bool Used { get; set; }
    public bool Private { get; set; }
    public List<string> Attributes { get; set; }
    public string BaseStructName { get; set; }
    public TypeDefinition BaseTypeDefinition { get; set; }
    public StructAst BaseStruct { get; set; }
    public bool Verified { get; set; }
    public bool Verifying { get; set; }
    public List<string> Generics { get; set; }
    public IType[] GenericTypes { get; set; }
    public List<StructFieldAst> Fields { get; } = new();
    public IntPtr MessagePointer { get; set; }
}

public class StructFieldAst : IDeclaration
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public uint Offset { get; set; }
    public TypeDefinition TypeDefinition { get; set; }
    public IType Type { get; set; }
    public IType ArrayElementType { get; set; }
    public bool HasGenerics { get; set; }
    public IAst Value { get; set; }
    public Dictionary<string, AssignmentAst> Assignments { get; set; }
    public List<Values> ArrayValues { get; set; }
    public List<string> Attributes { get; set; }
}

public class StructFieldRefAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public bool IsEnum { get; set; }
    public bool IsConstant { get; set; }
    public bool GlobalConstant { get; set; }
    public string String { get; set; }
    public bool ConstantStringLength { get; set; }
    public bool RawConstantString { get; set; }
    public int ConstantIndex { get; set; }
    public long ConstantValue { get; set; }
    public bool[] Pointers { get; set; }
    public IType[] Types { get; set; }
    public int[] ValueIndices { get; set; }
    public List<IAst> Children { get; } = new();
}

public class EnumAst : IAst, IType
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.Enum;
    public uint Size { get; set; } = 4;
    public uint Alignment { get; set; } = 4;
    public bool Used { get; set; }
    public bool Private { get; set; }
    public List<string> Attributes { get; set; }
    public TypeDefinition BaseTypeDefinition { get; set; }
    public PrimitiveAst BaseType { get; set; }
    public Dictionary<string, EnumValueAst> Values { get; } = new();
    public IntPtr MessagePointer { get; set; }
}

public class EnumValueAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public int Index { get; set; }
    public string Name { get; set; }
    public long Value { get; set; }
    public bool Defined { get; set; }
}

public class PrimitiveAst : IAst, IType
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; }
    public uint Size { get; set; }
    public uint Alignment { get; set; }
    public bool Used { get; set; }
    public bool Private { get; set; }
    public bool Signed { get; set; }
}

public class PointerType : IType
{
    public int FileIndex { get; set; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.Pointer;
    public uint Size { get; set; } = 8;
    public uint Alignment { get; set; } = 8;
    public bool Used { get; set; }
    public bool Private { get; set; }
    public IType PointedType { get; set; }
}

public class ArrayType : IType
{
    public int FileIndex { get; set; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.CArray;
    public uint Size { get; set; }
    public uint Alignment { get; set; }
    public bool Used { get; set; }
    public bool Private { get; set; }
    public uint Length { get; set; }
    public IType ElementType { get; set; }
}

public class UnionAst : IAst, IType
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.Union;
    public uint Size { get; set; }
    public uint Alignment { get; set; }
    public bool Used { get; set; }
    public bool Private { get; set; }
    public bool Verified { get; set; }
    public bool Verifying { get; set; }
    public List<UnionFieldAst> Fields { get; } = new();
    public IntPtr MessagePointer { get; set; }
}

public class UnionFieldAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public TypeDefinition TypeDefinition { get; set; }
    public IType Type { get; set; }
}

public class CompoundType : IType
{
    public int FileIndex { get; set; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.Compound;
    public uint Size { get; set; }
    // @Note Since compound types cannot be set as struct types, the alignment doesn't matter
    public uint Alignment { get; set; }
    public bool Used { get; set; }
    public bool Private { get; set; }
    public IType[] Types { get; set; }
}

public class ReturnAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IAst Value { get; set; }
}

public class ConstantAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IType Type { get; set; }
    public Constant Value { get; set; }
    public string String { get; set; }
}

public class NullAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IType TargetType { get; set; }
}

public class IdentifierAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public IType BakedType { get; set; }
    public int? TypeIndex { get; set; }
    public int? FunctionTypeIndex { get; set; }
}

public class ExpressionAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IType Type { get; set; }
    public List<Operator> Operators { get; } = new();
    public List<IType> ResultingTypes { get; } = new();
    public Dictionary<int, OperatorOverloadAst> OperatorOverloads { get; } = new();
    public List<IAst> Children { get; } = new();
}

public class CompoundExpressionAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public List<IAst> Children { get; } = new();
}

public class ChangeByOneAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public bool Prefix { get; set; }
    public bool Positive { get; set; }
    public IAst Value { get; set; }
    public IType Type { get; set; }
}

public class UnaryAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public UnaryOperator Operator { get; set; }
    public IAst Value { get; set; }
    public IType Type { get; set; }
}

public class CallAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public FunctionAst Function { get; set; }
    public IInterface Interface { get; set; }
    public List<TypeDefinition> Generics { get; set; }
    public Dictionary<string, IAst> SpecifiedArguments { get; set; }
    public List<IAst> Arguments { get; } = new();
    public IType TypeInfo { get; set; }
    public bool Inline { get; set; }
    public bool PassArrayToParams { get; set; }
}

public class DeclarationAst : IDeclaration
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public bool Global { get; set; }
    public bool Private { get; set; }
    public bool Verified { get; set; }
    public TypeDefinition TypeDefinition { get; set; }
    public IType Type { get; set; }
    public IType ArrayElementType { get; set; }
    public bool HasGenerics { get; set; }
    public bool Constant { get; set; }
    public int ConstantIndex { get; set; }
    public int PointerIndex { get; set; }
    public IAst Value { get; set; }
    public Dictionary<string, AssignmentAst> Assignments { get; set; }
    public List<Values> ArrayValues { get; set; }
    public IntPtr MessagePointer { get; set; }
}

public class CompoundDeclarationAst : IDeclaration
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public VariableAst[] Variables { get; set; }
    public TypeDefinition TypeDefinition { get; set; }
    public IType Type { get; set; }
    public IType ArrayElementType { get; set; }
    public bool HasGenerics { get; set; }
    public IAst Value { get; set; }
    public Dictionary<string, AssignmentAst> Assignments { get; set; }
    public List<Values> ArrayValues { get; set; }
}

public class AssignmentAst : IValues
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IAst Reference { get; set; }
    public Operator Operator { get; set; }
    public OperatorOverloadAst OperatorOverload { get; set; }
    public IAst Value { get; set; }
    public Dictionary<string, AssignmentAst> Assignments { get; set; }
    public List<Values> ArrayValues { get; set; }
}

public class ConditionalAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IAst Condition { get; set; }
    public ScopeAst IfBlock { get; set; }
    public ScopeAst ElseBlock { get; set; }
}

public class WhileAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IAst Condition { get; set; }
    public ScopeAst Body { get; set; }
}

public class EachAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public VariableAst IterationVariable { get; set; }
    public VariableAst IndexVariable { get; set; }
    public IAst Iteration { get; set; }
    public IAst RangeBegin { get; set; }
    public IAst RangeEnd { get; set; }
    public ScopeAst Body { get; set; }
}

public class VariableAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public IType Type { get; set; }
    public int PointerIndex { get; set; }
}

public class IndexAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public bool CallsOverload { get; set; }
    public OperatorOverloadAst Overload { get; set; }
    public IAst Index { get; set; }
}

public class CompilerDirectiveAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public DirectiveType Type { get; set; }
    public IAst Value { get; set; }
    public Import Import { get; set; }
    public Library Library { get; set; }
    public string StringValue { get; set; }
}

public class RunDirectiveFunction : IFunction
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int ConstantCount { get; set; }
    public int FunctionIndex { get; set; }
    public FunctionFlags Flags { get; set; }
    public IType ReturnType { get; set; }
    public TypeDefinition ReturnTypeDefinition { get; set; }
    public List<string> Generics { get; } = new();
    public List<DeclarationAst> Arguments { get; } = new();
    public int ArgumentCount { get; set; } = 2;
    public ScopeAst Body { get; set; }
    public IntPtr MessagePointer { get; set; }
}

public class Import
{
    public string Name { get; set; }
    public string Path { get; set; }
}

public class Library
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string AbsolutePath { get; set; }
    public string FileName { get; set; }
    public string LibPath { get; set; }
    public bool HasLib { get; set; }
    public bool HasDll { get; set; }
}

public class CastAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public TypeDefinition TargetTypeDefinition { get; set; }
    public IType TargetType { get; set; }
    public bool HasGenerics { get; set; }
    public IAst Value { get; set; }
}

public class BreakAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
}

public class ContinueAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
}

public class OperatorOverloadAst : IFunction
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int ConstantCount { get; set; }
    public int FunctionIndex { get; set; }
    public FunctionFlags Flags { get; set; }
    public Operator Operator { get; set; }
    public TypeDefinition Type { get; set; }
    public IType ReturnType { get; set; }
    public TypeDefinition ReturnTypeDefinition { get; set; }
    public List<string> Generics { get; } = new();
    public List<DeclarationAst> Arguments { get; } = new();
    public int ArgumentCount { get; set; } = 2;
    public ScopeAst Body { get; set; }
    public IntPtr MessagePointer { get; set; }
}

public class InterfaceAst : IInterface, IType
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public int TypeIndex { get; set; }
    public TypeKind TypeKind { get; set; } = TypeKind.Interface;
    public uint Size { get; set; } = 8;
    public uint Alignment { get; set; } = 8;
    public bool Used { get; set; }
    public bool Private { get; set; }
    public bool Verified { get; set; }
    public bool Verifying { get; set; }
    public FunctionFlags Flags { get; set; }
    public string BaseInterfaceName { get; set; }
    public IType ReturnType { get; set; }
    public TypeDefinition ReturnTypeDefinition { get; set; }
    public List<string> Generics { get; } = new();
    public IType[] GenericTypes { get; set; }
    public List<DeclarationAst> Arguments { get; } = new();
    public int ArgumentCount { get; set; }
    public IntPtr MessagePointer { get; set; }
}

public class AssemblyAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public List<AssemblyInstructionAst> Instructions { get; set; }
    public Dictionary<string, AssemblyInputAst> InRegisters { get; set; }
    public List<AssemblyInputAst> OutValues { get; set; }
    public bool FindStagingInputRegister { get; set; }
    public Byte[] AssemblyBytes { get; set; }
}

public class AssemblyInputAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Register { get; set; }
    public RegisterDefinition RegisterDefinition { get; set; }
    public IAst Ast { get; set; }
    public bool GetPointer { get; set; }
}

public class AssemblyInstructionAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Instruction { get; set; }
    public AssemblyValueAst Value1 { get; set; }
    public AssemblyValueAst Value2 { get; set; }
    public InstructionDefinition Definition { get; set; }
}

public class AssemblyValueAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public bool Dereference { get; set; }
    public string Register { get; set; }
    public RegisterDefinition RegisterDefinition { get; set; }
    public ConstantAst Constant { get; set; }
}

public class SwitchAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public IAst Value { get; set; }
    public List<(List<IAst>, ScopeAst)> Cases { get; } = new();
    public ScopeAst DefaultCase { get; set; }
}

public class DeferAst : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public bool Added { get; set; }
    public ScopeAst Statement { get; set; }
}

public class TypeDefinition : IAst
{
    public int FileIndex { get; set; }
    public uint Line { get; init; }
    public uint Column { get; init; }
    public string Name { get; set; }
    public bool IsGeneric { get; set; }
    public bool Compound { get; set; }
    public int GenericIndex { get; set; }
    public int TypeIndex { get; set; }
    public List<TypeDefinition> Generics { get; } = new();
    public IAst Count { get; set; }
    public uint? ConstCount { get; set; }
    public IType BakedType { get; set; }
}

public enum Operator
{
    None = 0,
    And, // &&
    Or, // ||
    Equality, // ==
    NotEqual, // !=
    GreaterThanEqual, // >=
    LessThanEqual, // <=
    ShiftLeft, // <<
    ShiftRight, // >>
    RotateLeft, // <<<
    RotateRight, // >>>
    Subscript, // []
    Add = '+',
    Subtract = '-',
    Multiply = '*',
    Divide = '/',
    GreaterThan = '>',
    LessThan = '<',
    BitwiseOr = '|',
    BitwiseAnd = '&',
    Xor = '^',
    Modulus = '%'
}

public enum UnaryOperator
{
    Not = '!',
    Negate = '-',
    Dereference = '*',
    Reference = '&'
}

public enum DirectiveType
{
    None,
    Run,
    If,
    Assert,
    ImportModule,
    ImportFile,
    Library,
    SystemLibrary,
    Insert,
    CopyToOutputDirectory
}

public enum TypeKind
{
    None,
    Void,
    Boolean,
    Integer,
    Float,
    String,
    Pointer,
    Array,
    CArray,
    Enum,
    Struct,
    Union,
    Interface,
    Type,
    Any,
    Compound,
    Function
}
