namespace Lang.Backend
{
    public interface IBackend
    {
        /// <summary>
        /// Builds the program executable
        /// </summary>
        /// <param name="projectPath">The path to the project</param>
        /// <param name="programGraph">Graph of the program</param>
        string Build(ProjectFile project, ProgramGraph programGraph);
    }
}
