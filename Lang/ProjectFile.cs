using System.Collections.Generic;

namespace Lang
{
    public class ProjectFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public Linker Linker { get; set; }
        public List<string> SourceFiles { get; set; }
        public List<string> Dependencies { get; } = new();
        public List<string> Packages { get; } = new();
    }

    public enum Linker
    {
        Static,
        Dynamic
    }
}
