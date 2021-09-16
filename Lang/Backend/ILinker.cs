namespace Lang.Backend
{
    public interface ILinker
    {
        /// <summary>
        /// Links the object file and creates an executable file
        /// </summary>
        /// <param name="objectPath">Path to the object file</param>
        /// <param name="executableName">Name of the executable file</param>
        void Link(string objectPath, string executableName);
    }
}
