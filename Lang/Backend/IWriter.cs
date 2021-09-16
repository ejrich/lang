namespace Lang.Backend
{
    public interface IWriter
    {
        /// <summary>
        /// Writes the program graph to a translated source file
        /// </summary>
        /// <returns>The path to the translated source file</returns>
        /// <param name="programGraph">Graph of the program</param>
        string WriteTranslatedFile(ProgramGraph programGraph);
    }
}
