using System.Collections.Generic;
namespace Lang.Parsing
{
    public interface IAst
    {
        List<IAst> Children { get; }
    }

    public class FunctionAst : IAst
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<Variable> Arguments { get; } = new();
        public List<IAst> Children { get; } = new();
    }

    public class ConstantAst : IAst
    {
        public string Value { get; set; }
        public List<IAst> Children { get; } = null;
    }

    public class ReturnAst : IAst
    {
        public List<IAst> Children { get; } = new();
    }

    public class Variable
    {
        public string Type { get; set; }
        public string Name { get; set; }
    }
}
