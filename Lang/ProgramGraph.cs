using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; } = new();
        public Dictionary<string, FunctionAst> Functions { get; } = new();
        public FunctionAst Start { get; set; }
        public List<CompilerDirectiveAst> Directives { get; } = new();
    }

    public class Data
    {
        public List<DeclarationAst> Variables { get; } = new();
        public List<IAst> Types { get; set; }
    }
}
