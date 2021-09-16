using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; set; }
        // public List<Namespace> Namespaces { get; } = new(); TODO Implement later
        public List<FunctionAst> Functions { get; } = new();
        public FunctionAst Main { get; set; }
    }

    public class Namespace
    {
        public string Name { get; set; }
        public Data Data { get; set; }
        public List<FunctionAst> Functions { get; set; } = new();
    }

    public class Data
    {
        public List<Variable> Variables { get; } = new();
        public List<StructAst> Structs { get; } = new();
    }
}
