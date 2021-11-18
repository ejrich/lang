using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ol;

public static class BuildSettings
{
    public static string Name { get; set; }
    public static LinkerType Linker { get; set; } = LinkerType.Dynamic; // TODO How to remove libc
    public static bool Release { get; set; }
    public static bool OutputAssembly { get; set; }
    public static string Path { get; set; }
    public static List<string> Files { get; } = new();
    public static HashSet<string> Dependencies { get; } = new();
}

public enum LinkerType : byte
{
    Static,
    Dynamic
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
                    if (arg.StartsWith("-"))
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
        Parser.Parse(entrypoint);

        ErrorReporter.ListErrorsAndExit(ErrorCodes.ParsingError);

        // 3. Check types and build the program ir
        TypeChecker.CheckTypes();
        var frontEndTime = stopwatch.Elapsed;

        ErrorReporter.ListErrorsAndExit(ErrorCodes.CompilationError);

        // 4. Build program
        stopwatch.Restart();
        var backend = new LLVMBackend();
        var objectFile = backend.Build();
        stopwatch.Stop();
        var buildTime = stopwatch.Elapsed;

        // 5. Link binaries
        stopwatch.Restart();
        Linker.Link(objectFile);
        stopwatch.Stop();
        var linkTime = stopwatch.Elapsed;

        // 6. Log statistics
        Console.WriteLine($"Front-end time: {frontEndTime.TotalSeconds} seconds\n" +
                          $"LLVM build time: {buildTime.TotalSeconds} seconds\n" +
                          $"Linking time: {linkTime.TotalSeconds} seconds");

        Allocator.Free();
        Environment.Exit(0);
    }
}
