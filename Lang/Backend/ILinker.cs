namespace Lang.Backend
{
    public interface ILinker
    {
        /// <summary>
        /// Links the object file and creates an executable file
        /// </summary>
        /// <param name="objectFile">Path to the object file</param>
        /// <param name="project">The project to build</param>
        /// <param name="programGraph">The project to build</param>
        /// <param name="buildSettings">Build settings from the cli args</param>
        void Link(string objectFile, ProjectFile project, ProgramGraph programGraph, BuildSettings buildSettings);
    }
}
