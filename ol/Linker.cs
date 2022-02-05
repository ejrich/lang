using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ol;

public static class Linker
{
    private const string BinaryDirectory = "bin";

    public static void Link(string objectFile)
    {
        // 1. Verify bin directory exists
        string binaryPath;
        if (BuildSettings.OutputDirectory == null)
        {
            binaryPath = Path.Combine(BuildSettings.Path, BinaryDirectory);
            if (!Directory.Exists(binaryPath))
                Directory.CreateDirectory(binaryPath);
        }
        else
        {
            binaryPath = BuildSettings.OutputDirectory;
        }

        // 2. Determine lib directories
        var libDirectory = DetermineLibDirectory();
        var linker = DetermineLinker(BuildSettings.Linker, libDirectory);
        var defaultObjects = DefaultObjects(libDirectory);

        // 3. Run the linker
        #if _LINUX
        var executableFile = Path.Combine(binaryPath, BuildSettings.Name);
        var libraries = string.Join(' ', BuildSettings.Libraries.Select(d => $"-l{d}"));
        var dependencies = string.Join(' ', BuildSettings.Dependencies);

        var linkerArguments = $"{linker} -o {executableFile} {objectFile} {defaultObjects} --start-group {libraries} {dependencies} --end-group";

        Console.WriteLine($"Linking: ld {linkerArguments}\n");
        var buildProcess = new Process {StartInfo = {FileName = "ld", Arguments = linkerArguments}};
        #endif

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
        #if _LINUX
        return new("/usr/lib");
        #endif
    }

    private static string DefaultObjects(DirectoryInfo libDirectory)
    {
        #if _LINUX
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Runtime/runtime.o");
        #endif
    }

    private static string DetermineLinker(LinkerType linkerType, DirectoryInfo libDirectory)
    {
        #if _LINUX
        if (linkerType == LinkerType.Static)
        {
            return "-static";
        }

        const string linkerPattern = "ld-linux-x86-64.so*";
        var linker = libDirectory.GetFiles(linkerPattern).FirstOrDefault();
        if (linker == null)
        {
            var platformDirectory = libDirectory.GetDirectories("x86_64*gnu").FirstOrDefault();
            if (platformDirectory == null)
            {
                Console.WriteLine($"Cannot find x86_64 libs in directory '{platformDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }

            linker = platformDirectory.GetFiles(linkerPattern).FirstOrDefault();

            if (linker == null)
            {
                Console.WriteLine($"Cannot find linker in directory '{libDirectory.FullName}'");
                Environment.Exit(ErrorCodes.LinkError);
            }
        }

        return $"-dynamic-linker {linker.FullName}";
        #endif
    }
}
