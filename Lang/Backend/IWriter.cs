namespace Lang.Backend
{
    public interface IWriter
    {
        /// <summary>
        /// Writes the program graph to a translated source file
        /// </summary>
        /// <returns>The path to the translated source file</returns>
        /// <param name="programGraph">Graph of the program</param>
        /// <param name="projectName">The name of the project</param>
        /// <param name="projectPath">The path to the project</param>
        string WriteFile(ProgramGraph programGraph, string projectName, string projectPath);
    }
}
