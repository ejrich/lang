using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; } = new();
        public Dictionary<string, FunctionAst> Functions { get; set; }
        public FunctionAst Start { get; set; }
        public List<CompilerDirectiveAst> Directives { get; } = new(); // TODO Get rid of
    }

    public class Data
    {
        public List<DeclarationAst> Variables { get; } = new();
        public Dictionary<string, IAst> Types { get; set; }
    }
}
