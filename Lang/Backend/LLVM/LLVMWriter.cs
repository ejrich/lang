using System.IO;
using Lang.Parsing;
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

            var objectFile = Path.Combine(objectPath, $"{projectName}.o");

            // 3. Write Data section
            WriteData(programGraph.Data);

            // 4. Write Functions
            foreach (var function in programGraph.Functions)
            {
                WriteFunction(function);
            }

            // 5. Write Main function
            WriteFunction(programGraph.Main);

            // 6. Compile to object file
            Compile(objectFile);

            return objectFile;
        }


        private void InitLLVM(string projectName)
        {
            _module = LLVMModuleRef.CreateWithName(projectName);
            _builder = LLVMBuilderRef.Create(_module.Context);
        }

        private void WriteData(Data data)
        {
            // TODO Implement me
        }

        private void WriteFunction(FunctionAst function)
        {
            // TODO Implement me
        }

        private void Compile(string objectFile)
        {
            // TODO Implement me
        }
    }
}
