namespace Lang.Backend
{
    public interface ILinker
    {
        /// <summary>
        /// Links the object file and creates an executable file
        /// </summary>
        /// <param name="objectFile">Path to the object file</param>
        /// <param name="projectPath">Name of the executable file</param>
        void Link(string objectFile, string projectPath);
    }
}
