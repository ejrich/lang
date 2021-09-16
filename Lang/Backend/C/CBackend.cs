namespace Lang.Backend.C
{
    public class CBackend : IBackend
    {
        private readonly IWriter _writer;
        private readonly IBuilder _builder;
        private readonly ILinker _linker;

        public CBackend(IWriter writer, IBuilder builder, ILinker linker)
        {
            _writer = writer;
            _builder = builder;
            _linker = linker;
        }

        public void Build(ProgramGraph programGraph, string projectName, string projectPath)
        {
            // 1. Write translated file
            var translatedFile = _writer.WriteFile(programGraph, projectName, projectPath);

            // 2. Build object file
            var objectFile = _builder.BuildFile(translatedFile);

            // 3. Link binaries
            _linker.Link(objectFile, projectPath);
        }
    }
}
