using System.Collections.Generic;

namespace Lang.Project
{
    public class Project
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public Linker Linker { get; init; }
        public List<string> BuildFiles { get; init; }
        public List<string> Dependencies { get; init; }
    }

    public enum ProjectFileSection
    {
        None,
        Name,
        Dependencies,
        Packages,
        Linker
    }

    public enum Linker
    {
        Static,
        Dynamic
    }

    public class ProjectFile
    {
        public string Name { get; set; }
        public Linker Linker { get; set; }
        public List<string> Dependencies { get; } = new();
        public List<string> Packages { get; } = new();
    }
}
