using System;
using System.Collections.Generic;
using System.Linq;

namespace Lang
{
    public interface IAst
    {
        int FileIndex { get; set; }
        uint Line { get; init; }
        uint Column { get; init; }
    }

    public interface IType
    {
        string Name { get; set; }
        string BackendName { get; set; }
        int TypeIndex { get; set; }
        TypeKind TypeKind { get; set; }
        uint Size { get; set; }
    }

    public interface IFunction : IAst
    {
        string Name { get; set; }
        FunctionFlags Flags { get; set; }
        IType ReturnType { get; set; }
        TypeDefinition ReturnTypeDefinition { get; set; }
        List<string> Generics { get; }
        List<DeclarationAst> Arguments { get; }
        ScopeAst Body { get; set; }
    }

    [Flags]
    public enum FunctionFlags
    {
        Extern = 0x1,
        Compiler = 0x2,
        Varargs = 0x4,
        Params = 0x8,
        Verified = 0x10,
        HasDirectives = 0x20,
        CallsCompiler = 0x40,
        ReturnVoidAtEnd = 0x80,
        ReturnTypeHasGenerics = 0x100,
        PrintIR = 0x200
    }

    public interface IDeclaration : IAst
    {
        string Name { get; set; }
        TypeDefinition TypeDefinition { get; set; }
        IType Type { get; set; }
        IType ArrayElementType { get; set; }
        bool HasGenerics { get; set; }
        IAst Value { get; set; }
        Dictionary<string, AssignmentAst> Assignments { get; set; }
        List<IAst> ArrayValues { get; set; }
    }

    public class ScopeAst : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public bool Returns { get; set; }
        public ScopeAst Parent { get; set; }
        public Dictionary<string, IAst> Identifiers { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class FunctionAst : IFunction, IType
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public string BackendName { get; set; }
        public int TypeIndex { get; set; }
        public int OverloadIndex { get; set; }
        public TypeKind TypeKind { get; set; } = TypeKind.Function;
        public FunctionFlags Flags { get; set; }
        public uint Size { get; set; } // Will always be 0
        public string ExternLib { get; set; }
        public IType ParamsElementType { get; set; }
        public IType ReturnType { get; set; }
        public TypeDefinition ReturnTypeDefinition { get; set; }
        public List<string> Generics { get; } = new();
        public List<DeclarationAst> Arguments { get; } = new();
        public List<IType[]> VarargsCallTypes { get; set; }
        public ScopeAst Body { get; set; }
    }

    public class StructAst : IAst, IType
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public string BackendName { get; set; }
        public int TypeIndex { get; set; }
        public TypeKind TypeKind { get; set; }
        public uint Size { get; set; }
        public string BaseStructName { get; set; }
        public TypeDefinition BaseTypeDefinition { get; set; }
        public StructAst BaseStruct { get; set; }
        public bool Verified { get; set; }
        public bool Verifying { get; set; }
        public List<string> Generics { get; set; }
        public IType[] GenericTypes { get; set; }
        public List<StructFieldAst> Fields { get; } = new();
    }

    public class StructFieldAst : IDeclaration
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public TypeDefinition TypeDefinition { get; set; }
        public IType Type { get; set; }
        public IType ArrayElementType { get; set; }
        public bool HasGenerics { get; set; }
        public IAst Value { get; set; }
        public Dictionary<string, AssignmentAst> Assignments { get; set; }
        public List<IAst> ArrayValues { get; set; }
    }

    public class StructFieldRefAst : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public bool IsEnum { get; set; }
        public bool IsConstant { get; set; }
        public uint ConstantValue { get; set; }
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
        public string BackendName { get; set; }
        public int TypeIndex { get; set; }
        public uint Size { get; set; } = 4;
        public TypeKind TypeKind { get; set; } = TypeKind.Enum;
        public TypeDefinition BaseTypeDefinition { get; set; }
        public PrimitiveAst BaseType { get; set; }
        public List<EnumValueAst> Values { get; } = new();
    }

    public class EnumValueAst : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public int Value { get; set; }
        public bool Defined { get; set; }
    }

    public class ReturnAst : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public IAst Value { get; set; }
    }

    public class PrimitiveAst : IAst, IType
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public string BackendName { get; set; }
        public int TypeIndex { get; set; }
        public TypeKind TypeKind { get; set; }
        public uint Size { get; set; }
        public bool Signed { get; set; }
        public IType PointerType { get; set; }
    }

    public class ArrayType : IType
    {
        public string Name { get; set; }
        public string BackendName { get; set; }
        public int TypeIndex { get; set; }
        public TypeKind TypeKind { get; set; } = TypeKind.CArray;
        public uint Size { get; set; }
        public uint Length { get; set; }
        public IType ElementType { get; set; }
    }

    public class ConstantAst : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string TypeName { get; set; }
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
        public string FunctionName { get; set; }
        public FunctionAst Function { get; set; }
        public int ExternIndex { get; set; }
        public List<TypeDefinition> Generics { get; set; }
        public Dictionary<string, IAst> SpecifiedArguments { get; set; }
        public List<IAst> Arguments { get; } = new();
    }

    public class DeclarationAst : IDeclaration
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public TypeDefinition TypeDefinition { get; set; }
        public IType Type { get; set; }
        public IType ArrayElementType { get; set; }
        public bool HasGenerics { get; set; }
        public bool Constant { get; set; }
        public int AllocationIndex { get; set; }
        public IAst Value { get; set; }
        public Dictionary<string, AssignmentAst> Assignments { get; set; }
        public List<IAst> ArrayValues { get; set; }
    }

    public class AssignmentAst : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public IAst Reference { get; set; }
        public Operator Operator { get; set; }
        public IAst Value { get; set; }
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
        public int? AllocationIndex { get; set; }
        public InstructionValue Pointer { get; set; }
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
        public FunctionFlags Flags { get; set; }
        public Operator Operator { get; set; }
        public TypeDefinition Type { get; set; }
        public IType ReturnType { get; set; }
        public TypeDefinition ReturnTypeDefinition { get; set; }
        public List<string> Generics { get; } = new();
        public List<DeclarationAst> Arguments { get; } = new();
        public ScopeAst Body { get; set; }
    }

    public class TypeDefinition : IAst
    {
        public int FileIndex { get; set; }
        public uint Line { get; init; }
        public uint Column { get; init; }
        public string Name { get; set; }
        public bool IsGeneric { get; set; }
        public int GenericIndex { get; set; }
        public int TypeIndex { get; set; }
        public List<TypeDefinition> Generics { get; } = new();
        public IAst Count { get; set; }
        public uint? ConstCount { get; set; }

        private string _genericName;
        public string GenericName
        {
            get
            {
                if (_genericName == null)
                    return _genericName = Generics.Aggregate(Name, (current, generic) => current + $".{generic.GenericName}");
                return _genericName;
            }
        }
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
        Assert
    }

    public enum TypeKind
    {
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
        Type,
        Any,
        Function
    }
}
