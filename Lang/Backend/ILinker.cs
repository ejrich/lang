using System.Collections.Generic;

namespace Lang.Backend
{
    public interface ILinker
    {
        /// <summary>
        /// Links the object file and creates an executable file
        /// </summary>
        /// <param name="objectFile">Path to the object file</param>
        /// <param name="projectPath">Name of the executable file</param>
        /// <param name="dependencies">The library dependencies to link the executable with</param>
        /// <param name="buildSettings">Build settings from the cli args</param>
        void Link(string objectFile, string projectPath, List<string> dependencies, BuildSettings buildSettings);
    }
}
