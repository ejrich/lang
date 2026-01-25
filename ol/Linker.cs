using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ol;

public static class Linker
{
    private const string DefaultOutputDirectory = "bin";

    #if _LINUX
    private const string LinkerName = "ld";
    #elif _WINDOWS
    private const string LinkerName = "lld-link.exe";

    public enum WindowsSubsystem
    {
        Console,
        Windows
    }

    public static WindowsSubsystem Subsystem = WindowsSubsystem.Console;
    #endif

    public static void Link(string objectFile)
    {
        // 1. Verify bin directory exists
        string outputDirectory;
        if (BuildSettings.OutputDirectory == null)
        {
            outputDirectory = Path.Combine(BuildSettings.Path, DefaultOutputDirectory);
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
        }
        else
        {
            outputDirectory = BuildSettings.OutputDirectory;
        }

        // 2. Copy files to the output directory
        foreach (var file in BuildSettings.FilesToCopy)
        {
            var outputPath = Path.Combine(outputDirectory, file.Name);
            if (File.Exists(outputPath))
            {
                File.SetAttributes(outputPath, FileAttributes.Normal);
                try {
                    File.Delete(outputPath);
                }
                catch {
                    Console.WriteLine($"Unable to copy file '{file}' to path '{outputPath}', file is either in use or path is invalid");
                    Environment.Exit(ErrorCodes.LinkError);
                }
            }
            file.CopyTo(outputPath, true);
        }

        // 3. Determine lib directories
        var libDirectory = DetermineLibDirectory();
        var defaultObjects = DefaultObjects();
        var executableFile = Path.Combine(outputDirectory, BuildSettings.Name);

        // 4. Run the linker
        #if _LINUX
        var linker = DetermineLinker(BuildSettings.Linker, libDirectory);
        var libraries = string.Join(' ', BuildSettings.LibraryNames.Select(lib => $"-l{lib}"));
        var libraryDirectories = string.Join(' ', BuildSettings.LibraryDirectories.Select(d => $"/libpath:\"{d}\""));
        var dependencies = BuildSettings.Linker == LinkerType.Static ?
            string.Join(' ', BuildSettings.Libraries.Select(lib => GetLibraryName(lib, "a"))) :
            string.Join(' ', BuildSettings.Libraries.Select(lib => GetLibraryName(lib, "so")));

        var linkerArguments = $"{linker} -o \"{executableFile}\" \"{objectFile}\" \"{defaultObjects}\" {libraryDirectories} --start-group {libraries} {dependencies} --end-group --no-warn-execstack";

        Console.WriteLine($"Linking: ld {linkerArguments}\n");
        #elif _WINDOWS
        var debug = BuildSettings.EmitDebug ? "-debug " : string.Empty;
        var libraries = string.Join(' ', BuildSettings.LibraryNames.Select(lib => $"{lib}.lib"));
        var libraryDirectories = string.Join(' ', BuildSettings.LibraryDirectories.Select(d => $"/libpath:\"{d}\""));
        var dependencies = string.Join(' ', BuildSettings.Libraries.Select(lib => GetLibraryName(lib, "lib")));
        var resources = string.Join(' ', BuildSettings.ResourceFiles.Select(r => $"\"{r}\""));

        var subsystem = Subsystem switch
        {
            WindowsSubsystem.Console => "console",
            WindowsSubsystem.Windows => "windows",
            _ => "console"
        };

        var linkerArguments = $"/entry:_start {debug}/out:\"{executableFile}.exe\" \"{objectFile}\" \"{defaultObjects}\" {resources} /libpath:\"{libDirectory.FullName}\" {libraryDirectories} {libraries} {dependencies} /subsystem:{subsystem}";

        Console.WriteLine($"Linking: lld-link.exe {linkerArguments}\n");
        #endif

        var buildProcess = new Process
        {
            StartInfo =
            {
                FileName = LinkerName,
                Arguments = linkerArguments,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            }
        };

        buildProcess.Start();
        buildProcess.WaitForExit();

        if (buildProcess.ExitCode != 0)
        {
            Console.WriteLine("Unable to link executable, please see output");
            Environment.Exit(ErrorCodes.LinkError);
        }

        Messages.Submit(MessageType.ExecutableLinked, executableFile);
    }

    private static DirectoryInfo DetermineLibDirectory()
    {
        #if _LINUX
        return new("/usr/lib");
        #elif _WINDOWS
        var windowsKits = new DirectoryInfo(@"C:\Program Files (x86)\Windows Kits");

        if (!windowsKits.Exists)
        {
            Console.WriteLine($"Cannot find 'Windows Kits' directory '{windowsKits.FullName}'");
            Environment.Exit(ErrorCodes.LinkError);
        }

        var latestVersion = windowsKits.GetDirectories().FirstOrDefault();

        if (latestVersion == null)
        {
            Console.WriteLine($"Cannot find Windows SDK version in directory '{windowsKits.FullName}'");
            Environment.Exit(ErrorCodes.LinkError);
        }

        var libDirectory = latestVersion.GetDirectories("Lib").FirstOrDefault();

        if (libDirectory == null)
        {
            Console.WriteLine($"Cannot find 'lib' directory in '{latestVersion.FullName}'");
            Environment.Exit(ErrorCodes.LinkError);
        }

        latestVersion = libDirectory.GetDirectories().LastOrDefault();

        if (latestVersion == null)
        {
            Console.WriteLine($"Cannot find Windows SDK version in directory '{libDirectory.FullName}'");
            Environment.Exit(ErrorCodes.LinkError);
        }

        libDirectory = latestVersion.GetDirectories("um").FirstOrDefault();

        if (libDirectory == null)
        {
            Console.WriteLine($"Cannot find 'um' directory in '{latestVersion.FullName}'");
            Environment.Exit(ErrorCodes.LinkError);
        }

        var x64LibDirectory = libDirectory.GetDirectories("x64").FirstOrDefault();

        if (x64LibDirectory == null)
        {
            Console.WriteLine($"Cannot find 'x64' directory in '{libDirectory.FullName}'");
            Environment.Exit(ErrorCodes.LinkError);
        }

        return x64LibDirectory;
        #endif
    }

    private static string DefaultObjects()
    {
        #if _LINUX
        const string runtime = "Runtime/runtime.o";
        #elif _WINDOWS
        const string runtime = "Runtime\\runtime.obj";
        #endif

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, runtime);
    }

    #if _LINUX
    private static string DetermineLinker(LinkerType linkerType, DirectoryInfo libDirectory)
    {
        if (linkerType == LinkerType.Static)
        {
            return "-static";
        }

        string linkerPattern = null, lib = null;
        switch (BuildSettings.OutputArchitecture)
        {
            case OutputArchitecture.X64:
                linkerPattern = "ld-linux-x86-64.so*";
                lib = "x86_64";
                break;
            case OutputArchitecture.Arm64:
                linkerPattern = "ld-linux-aarch64.so*";
                lib = "aarch64";
                break;
            default:
                Debug.Assert(false, "Incompatible target architecture");
                break;
        }

        var linker = libDirectory.GetFiles(linkerPattern).FirstOrDefault();
        if (linker == null)
        {
            var platformDirectory = libDirectory.GetDirectories($"{lib}*gnu").FirstOrDefault();
            if (platformDirectory == null)
            {
                Console.WriteLine($"Cannot find {lib} libs in directory '{platformDirectory.FullName}'");
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
    }
    #endif

    private static string GetLibraryName(Library library, string extension)
    {
        if (library.FileName == null)
        {
            return $"\"{library.AbsolutePath}.{extension}\"";
        }

        #if _LINUX
        if (library.LibPath == null)
        {
            return $"-l{library.FileName}";
        }
        return $"-L\"{library.LibPath}\" -l{library.FileName}";
        #elif _WINDOWS
        if (library.LibPath == null)
        {
            return $"{library.FileName}.{extension}";
        }
        return $"/libpath:\"{library.LibPath}\" {library.FileName}.lib";
        #endif
    }
}
