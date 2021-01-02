using System.IO;
using System.Text;
using LLVMSharp.Interop;

namespace Lang.Backend.LLVM
{
    public class LLVMWriter : IWriter
    {
        private const string ObjectDirectory = "obj";

        private LLVMModuleRef _module;
        private LLVMBuilderRef _builder;

        public string WriteFile(ProgramGraph programGraph, string projectName, string projectPath)
        {
            // 1. Initialize the LLVM module and builder
            InitLLVM(projectName);

            // 2. Verify obj directory exists
            var objectPath = Path.Combine(projectPath, ObjectDirectory);
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);

            // 3. Write Data section

            // 4. Write Functions
            foreach (var function in programGraph.Functions)
            {
            }

            // 5. Write Main function

            // 6. Write to file

            return string.Empty; // TODO put object file path here
        }

        private void InitLLVM(string projectName)
        {
            _module = LLVMModuleRef.CreateWithName(projectName);
            _builder = LLVMBuilderRef.Create(_module.Context);
        }
    }
}
