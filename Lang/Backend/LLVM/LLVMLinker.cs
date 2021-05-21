using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Lang.Backend.LLVM
{
    public class LLVMLinker : ILinker
    {
        private const string BinaryDirectory = "bin";

        public void Link(string objectFile, ProjectFile project, ProgramGraph programGraph, BuildSettings buildSettings)
        {
            // 1. Verify bin directory exists
            var binaryPath = Path.Combine(project.Path, BinaryDirectory);
            if (!Directory.Exists(binaryPath))
                Directory.CreateDirectory(binaryPath);

            // 2. Determine lib directories
            var libDirectory = DetermineLibDirectory();
            var linker = DetermineLinker(project.Linker, libDirectory);
            var gccDirectory = DetermineGCCDirectory(libDirectory);
            var defaultObjects = DefaultObjects(libDirectory);

            // 3. Run the linker
            var executableFile = Path.Combine(project.Path, BinaryDirectory, Path.GetFileNameWithoutExtension(objectFile));
            var dependencyList = string.Join(' ', programGraph.Dependencies.Select(d => $"-l{d}"));
            var buildProcess = new Process
            {
                StartInfo =
                {
                    FileName = "ld",
                    Arguments = $"{linker} -o {executableFile} {objectFile} {defaultObjects} " +
                                $"-L{gccDirectory} --start-group {dependencyList} -lgcc -lgcc_eh -lc --end-group"
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();
            if (buildProcess.ExitCode != 0)
            {
                Console.WriteLine("Unable to link executable, please see output");
                Environment.Exit(ErrorCodes.LinkError);
            }
        }

        private static DirectoryInfo DetermineLibDirectory()
        {
            return new("/usr/lib");
        }

        private readonly string[] _crtObjects = {
            "crt1.o", "crti.o", "crtn.o"
        };

        private string DefaultObjects(DirectoryInfo libDirectory)
        {
            var files = libDirectory.GetFiles();
            if (_crtObjects.All(o => files.Any(f => f.Name == o)))
            {
                return string.Join(' ', _crtObjects.Select(o => Path.Combine(libDirectory.FullName, o)));
            }

            var platformDirectory = libDirectory.GetDirectories("x86_64*gnu").FirstOrDefault();
            if (platformDirectory == null)
            {
                Console.WriteLine($"Cannot find x86_64 libs in directory '{libDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }
            files = platformDirectory.GetFiles();
            if (_crtObjects.All(o => files.Any(f => f.Name == o)))
            {
                return string.Join(' ', _crtObjects.Select(o => Path.Combine(platformDirectory.FullName, o)));
            }

            Console.WriteLine($"Unable to locate crt object files, valid locations are {libDirectory.FullName} or {platformDirectory.FullName}");
            Environment.Exit(ErrorCodes.LinkError);
            return null;
        }

        private static string DetermineLinker(Linker linkerType, DirectoryInfo libDirectory)
        {
            if (linkerType == Linker.Static)
            {
                return "-static";
            }

            const string linkerPattern = "ld-linux*.so*";
            var linker = libDirectory.GetFiles(linkerPattern).FirstOrDefault();
            if (linker == null)
            {
                var platformDirectory = libDirectory.GetDirectories("x86_64*gnu").FirstOrDefault();
                if (platformDirectory == null)
                {
                    Console.WriteLine($"Cannot find x86_64 libs in directory '{libDirectory.FullName}'");
                    Environment.Exit(ErrorCodes.LinkError);
                }
                linker = libDirectory.GetFiles(linkerPattern).FirstOrDefault();

                if (linker == null)
                {
                    Console.WriteLine($"Cannot find linker in directory '{libDirectory.FullName}'");
                    Environment.Exit(ErrorCodes.LinkError);
                }
            }

            return $"-dynamic-linker {linker.FullName}";
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
