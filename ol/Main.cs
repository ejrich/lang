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
    public static bool EmitDebug { get; set; } = true;
    public static bool OutputAssembly { get; set; }
    public static string Path { get; set; }
    public static string ObjectDirectory { get; set; }
    public static string OutputDirectory { get; set; }
    public static string GeneratedCodeFile { get; set; }
    public static List<string> Files { get; } = new();
    public static List<IntPtr> FileNames { get; } = new();
    public static List<FileInfo> FilesToCopy { get; } = new();
    // These are the libraries that are linked in with -l{name}
    public static HashSet<string> LibraryNames { get; } = new();
    // These are the paths the linker should look for libraries in
    public static HashSet<string> LibraryDirectories { get; } = new();
    // These are additional dependencies that need to be linked with the executable
    public static HashSet<Library> Libraries { get; } = new();
    public static Dictionary<string, InputVariable> InputVariables { get; } = new();

    public static string FileName(int index) => GetFileName(BuildSettings.Files[index]);

    public static int AddFile(string file)
    {
        var fileIndex = Files.Count;
        Files.Add(file);
        FileNames.Add(Allocator.MakeString(GetFileName(file), false));
        TypeChecker.PrivateScopes.Add(null);
        return fileIndex;
    }

    private static string GetFileName(string name) => name.Replace(BuildSettings.Path, string.Empty);

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

public class InputVariable
{
    public Token Token { get; set; }
    public bool Used { get; set; }
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
        var parsingVariables = false;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-R":
                case "--release":
                    BuildSettings.Release = true;
                    break;
                case "--noDebug":
                    BuildSettings.EmitDebug = false;
                    break;
                case "-S":
                    BuildSettings.OutputAssembly = true;
                    break;
                case "-V":
                    parsingVariables = true;
                    break;
                case "-noThreads":
                    noThreads = true;
                    break;
                default:
                    if (arg[0] == '-')
                    {
                        ErrorReporter.Report($"Unrecognized compiler flag '{arg}'");
                    }
                    else if (parsingVariables)
                    {
                        var tokens = new List<Token>(3);
                        Lexer.ParseTokens(arg, -1, tokens);
                        if (tokens.Count != 3 || tokens[0].Type != TokenType.Identifier || tokens[1].Type != TokenType.Equals || tokens[2].Type is not (TokenType.Number or TokenType.Boolean))
                        {
                            ErrorReporter.Report($"Malformed input variable '{arg}', should be formatted like 'foo=true'");
                        }
                        else if (!BuildSettings.InputVariables.TryAdd(tokens[0].Value, new InputVariable {Token = tokens[2]}))
                        {
                            ErrorReporter.Report($"Multiple definitions for input variable '{arg}'");
                        }
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
                            BuildSettings.ObjectDirectory = Path.Combine(BuildSettings.Path, "obj");
                            BuildSettings.GeneratedCodeFile = Path.Combine(BuildSettings.ObjectDirectory, ".generated_code.ol");
                            if (!Directory.Exists(BuildSettings.ObjectDirectory))
                                Directory.CreateDirectory(BuildSettings.ObjectDirectory);
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
        ThreadPool.CompleteWork(ThreadPool.IRQueue);
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

        // 6. Wait for intercepting loop to complete
        Messages.Completed();
        while (Messages.Intercepting);

        // 7. Log statistics
        Console.WriteLine($"Front-end time: {frontEndTime.TotalSeconds} seconds");
        Console.WriteLine($"LLVM build time: {buildTime.TotalSeconds} seconds");
        Console.WriteLine($"Linking time: {linkTime.TotalSeconds} seconds");

        Allocator.Free();
        Environment.Exit(0);
    }
}
