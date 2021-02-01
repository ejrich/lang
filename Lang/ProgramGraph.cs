using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; init; }
        public List<FunctionAst> Functions { get; } = new();
        public FunctionAst Main { get; set; }
    }

    public class Data
    {
        public List<Argument> Variables { get; } = new();
        public List<StructAst> Structs { get; set; }
    }
}
