using System.Collections.Generic;

namespace Lang.Backend
{
    public interface IBackend
    {
        void Build(ProgramGraph programGraph, string projectName, string projectPath, List<string> dependencies);
    }
}
