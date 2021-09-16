using System.IO;
using System.Text;

namespace Lang.Backend.C
{
    public class CWriter : IWriter
    {
        private const string ObjectDirectory = "obj";

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

            // 3. Write Namespaces
            foreach (var ns in programGraph.Namespaces)
            {
                AppendNamespace(fileContents, ns);
            }

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

        private static void AppendFunction(StringBuilder fileContents, Function function)
        {
            // TODO Write out functions
        }

        private static void AppendMainFunction(StringBuilder fileContents, Function main)
        {
            fileContents.AppendLine("int main() {");
            fileContents.AppendLine("\treturn 0;");
            fileContents.AppendLine("}");
        }
    }
}
