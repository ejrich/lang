using System.Collections.Generic;

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

        public void Build(ProgramGraph programGraph, string projectName, string projectPath, List<string> dependencies)
        {
            // 1. Build the object file
            var objectFile = _writer.WriteFile(programGraph, projectName, projectPath);

            // 2. Link binaries
            _linker.Link(objectFile, projectPath, dependencies);
        }
    }
}
