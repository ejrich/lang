using System.IO;
using System.Text;
using Lang.Parsing;
using LLVMSharp.Interop;

namespace Lang.Backend.C
{
    public class CWriter : IWriter
    {
        private const string ObjectDirectory = "obj";

        private readonly LLVMModuleRef _module;
        private readonly LLVMBuilderRef _builder;

        public CWriter()
        {
            _module = LLVMModuleRef.CreateWithName("ol");
            _builder = LLVMBuilderRef.Create(_module.Context);
        }

        public string WriteTranslatedFile(ProgramGraph programGraph, string projectName, string projectPath)
        {
            // 1. Verify obj directory exists
            var objectPath = Path.Combine(projectPath, ObjectDirectory);
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);

            var file = Path.Combine(objectPath, $"{projectName}.cpp");
            var fileContents = new StringBuilder();

            // 2. Write Data section
            AppendData(fileContents, programGraph.Data);

            // // 3. Write Namespaces
            // foreach (var ns in programGraph.Namespaces)
            // {
            //     AppendNamespace(fileContents, ns);
            // }

            // 4. Write Functions
            foreach (var function in programGraph.Functions)
            {
                AppendFunction(fileContents, function);
            }

            // 5. Write Main function
            AppendMainFunction(fileContents, programGraph.Main);

            // 6. Write to file
            File.WriteAllText(file, fileContents.ToString());

            return file;
        }

        private static void AppendData(StringBuilder fileContents, Data data)
        {
            // TODO Write out data
        }

        private static void AppendNamespace(StringBuilder fileContents, Namespace ns)
        {
            // TODO Write out data and functions in namespace
        }

        private static void AppendFunction(StringBuilder fileContents, FunctionAst function)
        {
            // TODO Write out functions
        }

        private static void AppendMainFunction(StringBuilder fileContents, FunctionAst main)
        {
            // TODO Write out main function
            fileContents.AppendLine("int main() {");
            fileContents.AppendLine("\treturn 0;");
            fileContents.AppendLine("}");
        }
    }
}
