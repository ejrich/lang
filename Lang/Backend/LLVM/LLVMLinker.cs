using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Lang.Backend.LLVM
{
    public class LLVMLinker : ILinker
    {
        private const string BinaryDirectory = "bin";

        public void Link(string objectFile, string projectPath, List<string> dependencies)
        {
            // 1. Verify bin directory exists
            var binaryPath = Path.Combine(projectPath, BinaryDirectory);
            if (!Directory.Exists(binaryPath))
                Directory.CreateDirectory(binaryPath);

            // 2. Determine lib directories
            var libDirectory = DetermineLibDirectory();
            var linker = DetermineLinker(libDirectory);
            var gccDirectory = DetermineGCCDirectory();

            // 3. Run the linker
            var executableFile = Path.Combine(projectPath, BinaryDirectory, Path.GetFileNameWithoutExtension(objectFile));
            var dependencyList = string.Join(' ', dependencies.Select(d => $"-l{d}"));
            var buildProcess = new Process
            {
                StartInfo =
                {
                    FileName = "ld",
                    Arguments = $"-dynamic-linker {linker} -o {executableFile} {objectFile} {DefaultDependencies(libDirectory)} " +
                                $"{dependencyList} -L{gccDirectory} --start-group -lgcc -lgcc_eh -lc --end-group"
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();
        }

        private static string DetermineLibDirectory()
        {
            return "/usr/lib";
        }

        private readonly string[] _cRuntimeObjects = {
            "crt1.o", "crti.o", "crtn.o"
        };

        private string DefaultDependencies(string libDirectory)
        {
            return string.Join(' ', _cRuntimeObjects.Select(o => Path.Combine(libDirectory, o)));
        }

        private static string DetermineLinker(string libDirectory)
        {
            return Path.Combine(libDirectory, "ld-2.33.so");
        }

        private static string DetermineGCCDirectory()
        {
            return "/usr/lib/gcc/x86_64-pc-linux-gnu/10.2.0";
        }
    }
}
