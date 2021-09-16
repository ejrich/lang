using System;
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
            var gccDirectory = DetermineGCCDirectory(libDirectory);
            var defaultObjects = DefaultObjects(libDirectory.FullName);

            // 3. Run the linker
            var executableFile = Path.Combine(projectPath, BinaryDirectory, Path.GetFileNameWithoutExtension(objectFile));
            var dependencyList = string.Join(' ', dependencies.Select(d => $"-l{d}"));
            var buildProcess = new Process
            {
                StartInfo =
                {
                    FileName = "ld",
                    Arguments = $"-dynamic-linker {linker} -o {executableFile} {objectFile} {defaultObjects} " +
                                $"{dependencyList} -L{gccDirectory} --start-group -lgcc -lgcc_eh -lc --end-group"
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();
        }

        private static DirectoryInfo DetermineLibDirectory()
        {
            return new("/usr/lib");
        }

        private readonly string[] _crtObjects = {
            "crt1.o", "crti.o", "crtn.o"
        };

        private string DefaultObjects(string libDirectory)
        {
            return string.Join(' ', _crtObjects.Select(o => Path.Combine(libDirectory, o)));
        }

        private static string DetermineLinker(DirectoryInfo libDirectory)
        {
            var linker = libDirectory.GetFiles("ld*.so").FirstOrDefault();
            if (linker == null)
            {
                Console.WriteLine($"Cannot find linker in directory '{libDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }
            return linker.FullName;
        }

        private static string DetermineGCCDirectory(DirectoryInfo libDirectory)
        {
            var gccDirectory = libDirectory.GetDirectories("gcc").FirstOrDefault();
            if (gccDirectory == null)
            {
                Console.WriteLine($"Cannot find gcc in directory '{libDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }

            var platformDirectory = gccDirectory.GetDirectories("x86_64*gnu").FirstOrDefault();
            if (platformDirectory == null)
            {
                Console.WriteLine($"Cannot find x86_64 libs in directory '{gccDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }

            var versionDirectory = platformDirectory.GetDirectories().FirstOrDefault();
            if (versionDirectory == null)
            {
                Console.WriteLine($"Cannot find any versions of gcc directory {platformDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }
            return versionDirectory.FullName;
        }
    }
}
