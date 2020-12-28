using System;
using System.Collections.Generic;
using System.Linq;
using Lang.Translation;

namespace Lang.Parsing
{
    public interface IAst
    {
        List<IAst> Children { get; }
    }

    public class ScopeAst : IAst
    {
        public List<IAst> Children { get; } = new();
    }

    public class ParseError
    {
        public string File { get; set; }
        public string Error { get; init; }
        public Token Token { get; init; }
    }

    public class FunctionAst : IAst
    {
        public string Name { get; set; }
        public TypeDefinition ReturnType { get; init; }
        public List<Variable> Arguments { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class StructAst : IAst
    {
        public string Name { get; set; }
        public List<StructFieldAst> Fields { get; } = new();
        public List<IAst> Children => null;
    }

    public class StructFieldAst : IAst
    {
        public TypeDefinition Type { get; set; }
        public string Name { get; set; }
        public ConstantAst DefaultValue { get; set; }
        public List<IAst> Children => null;
    }

    public class StructFieldRefAst : IAst
    {
        public string Name { get; init; }
        public StructFieldRefAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class ReturnAst : IAst
    {
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class ConstantAst : IAst
    {
        public Type Type { get; init; }
        public string Value { get; init; }
        public List<IAst> Children => null;
    }

    public class VariableAst : IAst
    {
        public string Name { get; init; }
        public List<IAst> Children => null;
    }

    public class ExpressionAst : IAst
    {
        public List<Operator> Operators { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class ChangeByOneAst : IAst
    {
        public bool Prefix { get; init; }
        public Operator Operator { get; init; }
        public List<IAst> Children { get; } = new();
    }

    public class CallAst : IAst
    {
        public string Function { get; set; }
        public List<IAst> Arguments { get; } = new();
        public List<IAst> Children => null;
    }

    public class DeclarationAst : IAst
    {
        public string Name { get; set; }
        public TypeDefinition Type { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class AssignmentAst : IAst
    {
        public IAst Variable { get; set; }
        public Operator Operator { get; set; }
        public IAst Value { get; set; }
        public List<IAst> Children => null;
    }

    public class ConditionalAst : IAst
    {
        public IAst Condition { get; set; }
        public List<IAst> Children { get; } = new();
        public IAst Else { get; set; }
    }

    public class WhileAst : IAst
    {
        public IAst Condition { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class EachAst : IAst
    {
        public string IterationVariable { get; set; }
        public IAst Iteration { get; set; }
        public IAst RangeBegin { get; set; }
        public IAst RangeEnd { get; set; }
        public List<IAst> Children { get; } = new();
    }

    public class Variable
    {
        public TypeDefinition Type { get; init; }
        public string Name { get; set; }
    }

    public class TypeDefinition
    {
        public string Type { get; init; }
        public List<TypeDefinition> Generics { get; } = new();
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

    public enum Type
    {
        Int,
        Float,
        Boolean,
        String,
        List,
        Void,
        Other,
        Error
    }
}
