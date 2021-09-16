using System.Collections.Generic;

namespace Lang.Project
{
    public class Project
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public List<string> BuildFiles { get; set; }
        public List<Project> Dependencies { get; set; }
    }
}
