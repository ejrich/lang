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

        public void Build(ProgramGraph programGraph, Project.Project project, BuildSettings buildSettings)
        {
            // 1. Build the object file
            var objectFile = _writer.WriteFile(programGraph, project.Name, project.Path, buildSettings.Optimize);

            // 2. Link binaries
            _linker.Link(objectFile, project.Path, project.Dependencies);
        }
    }
}
