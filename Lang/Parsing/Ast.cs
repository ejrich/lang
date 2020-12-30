﻿using System.Collections.Generic;

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

    public class FunctionAst : IAst
    {
        public string Name { get; set; }
        public TypeDefinition ReturnType { get; init; }
        public List<Argument> Arguments { get; } = new();
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
        public TypeDefinition Type { get; init; }
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
        public TypeDefinition Type { get; init; }
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
        public IAst Variable { get; set; }
        public List<IAst> Children => null;
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

    public class Argument
    {
        public TypeDefinition Type { get; init; }
        public string Name { get; set; }
    }

    public class TypeDefinition
    {
        public string Name { get; init; }
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
