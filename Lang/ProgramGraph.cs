using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public List<DeclarationAst> Variables { get; } = new();
        public Dictionary<string, IAst> Types { get; } = new();
        public Dictionary<string, FunctionAst> Functions { get; } = new();
        public FunctionAst Start { get; set; }
        public List<CompilerDirectiveAst> Directives { get; } = new(); // TODO Get rid of
    }
}
