using System.Collections.Generic;

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
        public TypeDefinition ReturnType { get; set; }
        public List<Argument> Arguments { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class StructAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public List<StructFieldAst> Fields { get; } = new();
        public List<IAst> Children => null;
    }

    public class StructFieldAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeDefinition Type { get; set; }
        public string Name { get; set; }
        public ConstantAst DefaultValue { get; set; }
        public List<IAst> Children => null;
    }

    public class StructFieldRefAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public StructFieldRefAst Value { get; set; }
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
        public List<Operator> Operators { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class ChangeByOneAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public bool Prefix { get; set; }
        public Operator Operator { get; set; }
        public IAst Variable { get; set; }
        public List<IAst> Children => null;
    }

    public class CallAst : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Function { get; set; }
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
        public IAst Else { get; set; }
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
        public IAst RangeBegin { get; set; }
        public IAst RangeEnd { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class Argument : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public TypeDefinition Type { get; set; }
        public string Name { get; set; }
        public List<IAst> Children => null;
    }

    public class TypeDefinition : IAst
    {
        public int FileIndex { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public string Name { get; set; }
        public List<TypeDefinition> Generics { get; } = new();
        public List<IAst> Children => null;
    }

    public enum Operator
    {
        None = 0,
        And, // &&
        Or, // ||
        Equality, // ==
        Increment, // ++
        Decrement, // --
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
}
