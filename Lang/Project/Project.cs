using System.Collections.Generic;

namespace Lang.Project
{
    public class Project
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public List<string> BuildFiles { get; init; }
        public List<string> Dependencies { get; init; }
    }
}
