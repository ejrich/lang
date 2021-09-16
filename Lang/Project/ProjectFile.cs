using System.Collections.Generic;

namespace Lang.Project
{
    public enum ProjectFileSection
    {
        None,
        Name,
        Dependencies,
        Packages
    }

    public class ProjectFile
    {
        public string Name { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public List<string> Packages { get; set; } = new();
    }
}
