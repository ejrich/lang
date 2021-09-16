using System.Collections.Generic;

namespace Lang.Backend
{
    public interface IBackend
    {
        /// <summary>
        /// Builds the program executable
        /// </summary>
        /// <param name="programGraph">Graph of the program</param>
        /// <param name="sourceFiles">The source files for debugging</param>
        string Build(ProgramGraph programGraph, List<string> sourceFiles);
    }
}
