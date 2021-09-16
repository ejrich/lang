using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; init; }
        public List<FunctionAst> Functions { get; } = new();
        public FunctionAst Main { get; set; }
        public FunctionAst Start { get; set; }
    }

    public class Data
    {
        public List<DeclarationAst> Variables { get; } = new();
        public List<StructAst> Structs { get; set; }
    }
}
