using System.Collections.Generic;

namespace Lang.Parsing
{
    public class Variable
    {
        public string Type { get; set; }
        public string Name { get; set; }
    }

    public class FunctionAst
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<Variable> Arguments { get; } = new();
        public List<Token> Body { get; } = new();
    }
}
