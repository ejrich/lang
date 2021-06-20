namespace Lang.Backend
{
    public interface IBackend
    {
        /// <summary>
        /// Builds the program executable
        /// </summary>
        /// <param name="projectPath">The path to the project</param>
        /// <param name="programGraph">Graph of the program</param>
        /// <param name="buildSettings">Build settings from the cli args</param>
        void Build(ProjectFile project, ProgramGraph programGraph, BuildSettings buildSettings);
    }
}
