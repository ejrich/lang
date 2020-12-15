using System.IO;
using System.Text;

namespace Lang.Backend.C
{
    public class CWriter : IWriter
    {
        public string WriteTranslatedFile(ProgramGraph programGraph, string projectName, string projectPath)
        {
            // 1. Verify obj directory exists
            var objectPath = Path.Combine(projectPath, "obj");
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);
            
            var file = Path.Combine(objectPath, $"{projectName}.cpp");
            var fileContents = new StringBuilder();

            // 2. Write Data section
            // 3. Write Namespaces
            // 4. Write Functions
            // 5. Write Main function
            fileContents.AppendLine("int main() {");
            fileContents.AppendLine("\treturn 0;");
            fileContents.AppendLine("}");

            // 6. Write to file
            File.WriteAllText(file, fileContents.ToString());

            return file;
        }
    }
}
