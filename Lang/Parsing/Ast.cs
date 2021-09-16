﻿using System.Collections.Generic;

namespace Lang.Parsing
{
    public interface IAst
    {
        List<IAst> Children { get; }
        List<ParseError> Errors { get; }
    }

    public struct ParseError
    {
        public string Error { get; init; }
        public Token Token { get; init; }
    }

    public class FunctionAst : IAst
    {
        public string Name { get; set; }
        public TypeDefinition ReturnType { get; set; }
        public List<Variable> Arguments { get; } = new();
        public List<IAst> Children { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }

    public class ConstantAst : IAst
    {
        public string Value { get; set; }
        public List<IAst> Children => null;
        public List<ParseError> Errors => null;
    }

    public class ReturnAst : IAst
    {
        public List<IAst> Children { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }

    public class Variable
    {
        public TypeDefinition Type { get; set; }
        public string Name { get; set; }
    }

    public class TypeDefinition
    {
        public string Type { get; set; }
        public List<string> Generics { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }
}
