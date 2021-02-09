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
                    FileName = "ld",
                    Arguments = $"-static -o {executableFile} /usr/lib/crt1.o /usr/lib/crti.o -L/usr/lib/gcc/x86_64-pc-linux-gnu/10.2.0 " +
                                $"{objectFile} --start-group -lgcc -lgcc_eh -lc --end-group /usr/lib/crtn.o"
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();
        }
    }
}
