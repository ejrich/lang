using System.Collections.Generic;

namespace Lang.Backend
{
    public interface IBackend
    {
        /// <summary>
        /// Builds the program executable
        /// </summary>
        /// <param name="sourceFiles">The source files for debugging</param>
        string Build(List<string> sourceFiles);
    }
}
