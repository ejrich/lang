using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ol;

public static class BuildSettings
{
    public static string Name { get; set; }
    public static LinkerType Linker { get; set; }
    public static OutputTypeTableConfiguration OutputTypeTable { get; set; }
    public static OutputArchitecture OutputArchitecture { get; set; }
    public static bool Release { get; set; }
    public static bool OutputAssembly { get; set; }
    public static string Path { get; set; }
    public static string OutputDirectory { get; set; }
    public static List<string> Files { get; } = new();
    public static List<FileInfo> FilesToCopy { get; } = new();
    // These are the libraries that are linked in with -l{name}
    public static HashSet<string> LibraryNames { get; } = new();
    // These are the paths the linker should look for libraries in
    public static HashSet<string> LibraryDirectories { get; } = new();
    // These are additional dependencies that need to be linked with the executable
    public static HashSet<Library> Libraries { get; } = new();
}

public enum LinkerType : byte
{
    Static,
    Dynamic
}

public enum OutputTypeTableConfiguration : byte
{
    Full,
    Used,
    None
}

public enum OutputArchitecture : byte
{
    None,
    X86,
    X64,
    Arm,
    Arm64
}

public static class ol
{
    public static void Main(string[] args)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // 1. Load cli args into build settings
        string entrypoint = null;
        var noThreads = false;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-R":
                case "--release":
                    BuildSettings.Release = true;
                    break;
                case "-S":
                    BuildSettings.OutputAssembly = true;
                    break;
                case "-noThreads":
                    noThreads = true;
                    break;
                default:
                    if (arg[0] == '-')
                    {
                        ErrorReporter.Report($"Unrecognized compiler flag '{arg}'");
                    }
                    else if (entrypoint != null)
                    {
                        ErrorReporter.Report($"Multiple program entrypoints defined '{arg}'");
                    }
                    else
                    {
                        if (!File.Exists(arg) || !arg.EndsWith(".ol"))
                        {
                            ErrorReporter.Report($"Entrypoint file does not exist or is not an .ol file '{arg}'");
                        }
                        else
                        {
                            BuildSettings.Name = Path.GetFileNameWithoutExtension(arg);
                            entrypoint = Path.GetFullPath(arg);
                            BuildSettings.Path = Path.GetDirectoryName(entrypoint);
                        }
                    }
                    break;
            }
        }
        if (entrypoint == null)
        {
            ErrorReporter.Report("Program entrypoint not defined");
        }

        ErrorReporter.ListErrorsAndExit(ErrorCodes.ArgumentsError);

        // 2. Parse source files to asts
        ThreadPool.Init(noThreads);
        TypeChecker.Init();
        Parser.Parse(entrypoint);
        ErrorReporter.ListErrorsAndExit(ErrorCodes.ParsingError);

        // 3. Check types and build the program ir
        TypeChecker.CheckTypes();
        ErrorReporter.ListErrorsAndExit(ErrorCodes.CompilationError);
        ThreadPool.CompleteWork();
        var frontEndTime = stopwatch.Elapsed;

        // 4. Build program
        stopwatch.Restart();
        var objectFile = LLVMBackend.Build();
        stopwatch.Stop();
        var buildTime = stopwatch.Elapsed;

        // 5. Link binaries
        stopwatch.Restart();
        Linker.Link(objectFile);
        stopwatch.Stop();
        var linkTime = stopwatch.Elapsed;

        // 6. Log statistics
        Console.WriteLine($"Front-end time: {frontEndTime.TotalSeconds} seconds");
        Console.WriteLine($"LLVM build time: {buildTime.TotalSeconds} seconds");
        Console.WriteLine($"Linking time: {linkTime.TotalSeconds} seconds");

        Allocator.Free();
        Environment.Exit(0);
    }
}
