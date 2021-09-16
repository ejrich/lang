using System.Diagnostics;
using System.IO;

namespace Lang.Backend.LLVM
{
    public class LLVMLinker : ILinker
    {
        private const string BinaryDirectory = "bin";

        public void Link(string objectFile, string projectPath)
        {
            // 1. Verify bin directory exists
            var binaryPath = Path.Combine(projectPath, BinaryDirectory);
            if (!Directory.Exists(binaryPath))
                Directory.CreateDirectory(binaryPath);

            // 2. Run the linker
            var executableFile = Path.Combine(projectPath, BinaryDirectory, Path.GetFileNameWithoutExtension(objectFile));
            var buildProcess = new Process
            {
                StartInfo =
                {
                    FileName = "gcc",
                    Arguments = $"{objectFile} -o {executableFile}"
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();
        }
    }
}
