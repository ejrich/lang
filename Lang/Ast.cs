using System.Collections.Generic;
using System.Linq;

namespace Lang
{
    public interface IAst
    {
        int FileIndex { get; set; }
        int Line { get; init; }
        int Column { get; init; }
        List<IAst> Children { get; }
    }

    public interface IType
    {
        string Name { get; set; }
        int TypeIndex { get; set; }
        TypeKind TypeKind { get; set; }
        uint Size { get; set; }
    }

    public interface IFunction : IAst
    {
        bool Verified { get; set; }
        bool HasDirectives { get; set; }
        bool CallsCompiler { get; set; }
        TypeDefinition ReturnType { get; set; }
        bool ReturnTypeHasGenerics { get; set; }
        List<string> Generics { get; }
        List<DeclarationAst> Arguments { get; }
    }

    public class ScopeAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public List<IAst> Children { get; } = new();
    }

    public class FunctionAst : IFunction, IType
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public int TypeIndex { get; set; }
        public TypeKind TypeKind { get; set; } = TypeKind.Function;
        public uint Size { get; set; } // Will always be 0
        public bool Extern { get; set; }
        public bool Compiler { get; set; }
        public string ExternLib { get; set; }
        public bool Varargs { get; set; }
        public bool Params { get; set; }
        public bool Verified { get; set; }
        public bool HasDirectives { get; set; }
        public bool CallsCompiler { get; set; }
        public TypeDefinition ReturnType { get; set; }
        public bool ReturnTypeHasGenerics { get; set; }
        public List<string> Generics { get; } = new();
        public List<DeclarationAst> Arguments { get; } = new();
        public List<List<TypeDefinition>> VarargsCalls { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class StructAst : IAst, IType
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public int TypeIndex { get; set; }
        public TypeKind TypeKind { get; set; }
        public uint Size { get; set; }
        public bool Verified { get; set; }
        public List<string> Generics { get; } = new();
        public List<StructFieldAst> Fields { get; } = new();
        public List<IAst> Children => null;
    }

    public class StructFieldAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public bool HasGeneric { get; set; }
        public string Name { get; set; }
        public uint Offset { get; set; }
        public TypeDefinition Type { get; set; }
        public IAst DefaultValue { get; set; }
        public List<IAst> Children => null;
    }

    public class StructFieldRefAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public bool IsEnum { get; set; }
        public bool[] Pointers { get; set; }
        public string[] TypeNames { get; set; }
        public int[] ValueIndices { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class EnumAst : IAst, IType
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public int TypeIndex { get; set; }
        public uint Size { get; set; } = 4;
        public TypeKind TypeKind { get; set; } = TypeKind.Enum;
        public TypeDefinition BaseType { get; set; }
        public List<EnumValueAst> Values { get; } = new();
        public List<IAst> Children => null;
    }

    public class EnumValueAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public int Value { get; set; }
        public bool Defined { get; set; }
        public List<IAst> Children => null;
    }

    public class ReturnAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class PrimitiveAst : IAst, IType
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public int TypeIndex { get; set; }
        public uint Size { get; set; }
        public TypeKind TypeKind { get; set; }
        public IPrimitive Primitive { get; set; }
        public List<IAst> Children => null;
    }

    public class ConstantAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeDefinition Type { get; set; }
        public string Value { get; set; }
        public List<IAst> Children => null;
    }

    public class NullAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeDefinition TargetType { get; set; }
        public List<IAst> Children => null;
    }

    public class IdentifierAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public List<IAst> Children => null;
    }

    public class ExpressionAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeDefinition Type { get; set; }
        public List<Operator> Operators { get; } = new();
        public List<TypeDefinition> ResultingTypes { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class ChangeByOneAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public bool Prefix { get; set; }
        public bool Positive { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class UnaryAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public UnaryOperator Operator { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class CallAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Function { get; set; }
        public bool Params { get; set; }
        public int FunctionIndex { get; set; }
        public int VarargsIndex { get; set; }
        public List<TypeDefinition> Generics { get; set; }
        public Dictionary<string, IAst> SpecifiedArguments { get; set; }
        public List<IAst> Arguments { get; } = new();
        public List<IAst> Children => null;
    }

    public class DeclarationAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public TypeDefinition Type { get; set; }
        public IAst Value { get; set; }
        public bool Constant { get; set; }
        public bool HasGenerics { get; set; }
        public List<AssignmentAst> Assignments { get; } = new();
        public List<IAst> Children => null;
    }

    public class AssignmentAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public IAst Reference { get; set; }
        public Operator Operator { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class ConditionalAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public IAst Condition { get; set; }
        public List<IAst> Children { get; } = new();
        public List<IAst> Else { get; } = new();
    }

    public class WhileAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public IAst Condition { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class EachAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string IterationVariable { get; set; }
        public IAst Iteration { get; set; }
        public TypeDefinition IteratorType { get; set; }
        public IAst RangeBegin { get; set; }
        public IAst RangeEnd { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class IndexAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public bool CallsOverload { get; set; }
        public TypeDefinition OverloadType { get; set; }
        public IAst Index { get; set; }
        public List<IAst> Children => null;
    }

    public class CompilerDirectiveAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public DirectiveType Type { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class CastAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeDefinition TargetType { get; set; }
        public bool HasGenerics { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class OperatorOverloadAst : IFunction
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public Operator Operator { get; set; }
        public TypeDefinition Type { get; set; }
        public bool Verified { get; set; }
        public bool HasDirectives { get; set; }
        public bool CallsCompiler { get; set; }
        public TypeDefinition ReturnType { get; set; }
        public bool ReturnTypeHasGenerics { get; set; }
        public List<string> Generics { get; } = new();
        public List<DeclarationAst> Arguments { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class TypeDefinition : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeKind? TypeKind { get; set; }
        public string Name { get; set; }
        public bool IsGeneric { get; set; }
        public bool Constant { get; set; }
        public int GenericIndex { get; set; }
        public int? TypeIndex { get; set; }
        public bool CArray { get; set; }
        public IPrimitive PrimitiveType { get; set; }
        public List<TypeDefinition> Generics { get; } = new();
        public IAst Count { get; set; }
        public uint? ConstCount { get; set; }
        public List<IAst> Children => null;

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

    public interface IPrimitive
    {
        byte Bytes { get; }
        bool Signed { get; }
    }

    public class IntegerType : IPrimitive
    {
        public byte Bytes { get; init; }
        public bool Signed { get; init; }
    }

    public class FloatType : IPrimitive
    {
        public byte Bytes { get; set; }
        public bool Signed => true;
    }

    public class EnumType : IPrimitive
    {
        public byte Bytes { get; init; }
        public bool Signed { get; init; }
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
        List,
        Enum,
        Struct,
        Function,
        // Below not used in the backend
        VarArgs,
        Params,
        Type,
        Generic,
        Error
    }
}
