using System.Diagnostics;
using System.IO;

namespace Lang.Backend.C
{
    public class CBuilder : IBuilder
    {
        public string BuildFile(string filePath)
        {
            var objectFile = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + ".o");
            var buildProcess = new Process
            {
                StartInfo =
                {
                    FileName = "g++",
                    Arguments = $"-c {filePath} -o {objectFile}"
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();

            return objectFile;
        }
    }
}
