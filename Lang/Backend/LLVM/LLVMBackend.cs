namespace Lang.Backend.LLVM
{
    public class LLVMBackend : IBackend
    {
        private readonly IWriter _writer;
        private readonly ILinker _linker;

        public LLVMBackend(IWriter writer, ILinker linker)
        {
            _writer = writer;
            _linker = linker;
        }

        public void Build(ProjectFile project, ProgramGraph programGraph, BuildSettings buildSettings)
        {
            // 1. Build the object file
            var objectFile = _writer.WriteFile(project.Path, programGraph, buildSettings);

            // 2. Link binaries
            _linker.Link(objectFile, project, programGraph, buildSettings);
        }
    }
}
