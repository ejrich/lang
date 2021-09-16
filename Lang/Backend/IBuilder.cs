namespace Lang.Backend
{
    public interface IBuilder
    {
        /// <summary>
        /// Builds the translated source file
        /// and returns the path to the object file
        /// </summary>
        /// <param name="filePath">The path to the translated source file</param>
        /// <returns>The path to the object file</returns>
        string BuildFile(string filePath, BuildSettings buildSettings);
    }
}
