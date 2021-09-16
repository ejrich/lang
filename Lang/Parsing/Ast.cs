using System.Collections.Generic;
using System.Linq;

namespace Lang.Parsing
{
    public interface IAst
    {
        int FileIndex { get; set; }
        int Line { get; init; }
        int Column { get; init; }
        List<IAst> Children { get; }
    }

    public class ScopeAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public List<IAst> Children { get; } = new();
    }

    public class FunctionAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public bool Extern { get; set; }
        public string ExternLib { get; set; }
        public bool Varargs { get; set; }
        public bool Params { get; set; }
        public TypeDefinition ReturnType { get; set; }
        public List<Argument> Arguments { get; } = new();
        public List<List<TypeDefinition>> VarargsCalls { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class StructAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
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
        public TypeDefinition Type { get; set; }
        public IAst DefaultValue { get; set; }
        public List<IAst> Children => null;
    }

    public class StructFieldRefAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public bool IsEnum { get; set; }
        public bool IsPointer { get; set; }
        public string StructName { get; set; }
        public StructFieldRefAst Value { get; set; }
        public int ValueIndex { get; set; }
        public List<IAst> Children => null;
    }

    public class EnumAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
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

    public class VariableAst : IAst
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
        public IAst Variable { get; set; }
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
        public List<AssignmentAst> Assignments { get; } = new();
        public List<IAst> Children => null;
    }

    public class AssignmentAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public IAst Variable { get; set; }
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
        public IAst Variable { get; set; }
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

    public class Argument : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public TypeDefinition Type { get; set; }
        public IAst DefaultValue { get; set; }
        public List<IAst> Children => null;
    }

    public class TypeDefinition : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public bool IsGeneric { get; set; }
        public int GenericIndex { get; set; }
        public IPrimitive PrimitiveType { get; set; }
        public List<TypeDefinition> Generics { get; } = new();
        public IAst Count { get; set; }
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

    public enum Operator
    {
        None = 0,
        And, // &&
        Or, // ||
        Equality, // ==
        NotEqual, // !=
        GreaterThanEqual, // >=
        LessThanEqual, // <=
        Add = '+',
        Subtract = '-',
        Multiply = '*',
        Divide = '/',
        GreaterThan = '>',
        LessThan = '<',
        BitwiseOr = '|',
        BitwiseAnd = '&',
        Xor = '^',
        Modulus = '%',
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
        If
    }
}
