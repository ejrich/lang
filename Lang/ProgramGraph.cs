using System.Collections.Generic;
using Lang.Parsing;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; set; }
        public List<Namespace> Namespaces { get; set; } = new();
        public List<FunctionAst> Functions { get; set; } = new();
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
        public List<Variable> Variables { get; set; } = new();
        public List<Struct> Structs { get; set; } = new();
    }

    public class Struct
    {
        public string Name { get; set; }
        public List<Variable> Fields { get; set; } = new();
    }
}
