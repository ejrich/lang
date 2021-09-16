using System.Collections.Generic;

namespace Lang
{
    public class ProgramGraph
    {
        public Data Data { get; set; }
        public List<Namespace> Namespaces { get; set; }
        public List<Function> Functions { get; set; }
        public Function Main { get; set; }
    }
    
    public class Namespace
    {
        public string Name { get; set; }
        public Data Data { get; set; }
        public List<Function> Functions { get; set; }
    }

    public class Data
    {
        public List<Variable> Variables { get; set; }
        public List<Struct> Structs { get; set; }
    }

    public class Struct
    {
        public string Name { get; set; }
        public List<Variable> Fields { get; set; }
    }

    public class Variable
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class Function
    {
        public List<Variable> Arguments { get; set; }
        //public List<IAst> Body { get; set; }
    }
}
