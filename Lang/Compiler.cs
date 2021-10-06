using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lang.Backend;

namespace Lang
{
    public static class BuildSettings
    {
        public static string Name { get; set; }
        public static LinkerType Linker { get; set; }
        public static bool Release { get; set; }
        public static bool OutputAssembly { get; set; }
        public static string Path { get; set; }
        public static HashSet<string> Dependencies { get; } = new();
    }

    public enum LinkerType
    {
        Static,
        Dynamic
    }

    public interface ICompiler
    {
        void Compile(string[] args);
    }

    public class Compiler : ICompiler
    {
        private readonly IProjectInterpreter _projectInterpreter;
        private readonly IParser _parser;
        private readonly ITypeChecker _typeChecker;
        private readonly IBackend _backend;
        private readonly ILinker _linker;

        public Compiler(IProjectInterpreter projectInterpreter, IParser parser, ITypeChecker typeChecker, IBackend backend, ILinker linker)
        {
            _projectInterpreter = projectInterpreter;
            _parser = parser;
            _typeChecker = typeChecker;
            _backend = backend;
            _linker = linker;
        }

        public void Compile(string[] args)
        {
            // 1. Load cli args into build settings
            string entryPoint = null;
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
                    default:
                        if (arg.StartsWith("-"))
                        {
                            ErrorReporter.Report($"Unrecognized compiler flag '{arg}'");
                        }
                        else
                        {
                            entryPoint ??= arg;
                        }
                        break;
                }
            }

            ErrorReporter.ListErrorsAndExit(ErrorCodes.ArgumentsError);

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            // 2. Load files in project
            var sourceFiles = _projectInterpreter.LoadProject(entryPoint);

            // 3. Parse source files to tokens
            var asts = _parser.Parse(sourceFiles);

            if (ErrorReporter.Errors.Any())
            {
                var currentFile = Int32.MinValue;
                foreach (var parseError in ErrorReporter.Errors)
                {
                    if (currentFile != parseError.FileIndex)
                    {
                        if (currentFile != Int32.MinValue) Console.WriteLine();
                        currentFile = parseError.FileIndex.Value;
                        Console.WriteLine($"Failed to parse file: \"{sourceFiles[currentFile].Replace(BuildSettings.Path, string.Empty)}\":");
                    }
                    Console.WriteLine($"    {parseError.Message} at line {parseError.Line}:{parseError.Column}");
                }
                Environment.Exit(ErrorCodes.ParsingError);
            }

            // 4. Check types and build the program ir
            _typeChecker.CheckTypes(asts);
            var frontEndTime = stopwatch.Elapsed;

            if (ErrorReporter.Errors.Any())
            {
                Console.WriteLine($"{ErrorReporter.Errors.Count} compilation error(s):\n");
                foreach (var error in ErrorReporter.Errors)
                {
                    if (error.FileIndex.HasValue)
                    {
                        Console.WriteLine($"    {sourceFiles[error.FileIndex.Value].Replace(BuildSettings.Path, string.Empty)}: {error.Message} at line {error.Line}:{error.Column}");
                    }
                    else
                    {
                        Console.WriteLine($"    {error.Message}");
                    }
                }
                Environment.Exit(ErrorCodes.CompilationError);
            }

            // 5. Build program
            stopwatch.Restart();
            var objectFile = _backend.Build(sourceFiles);
            stopwatch.Stop();
            var buildTime = stopwatch.Elapsed;

            // 6. Link binaries
            stopwatch.Restart();
            _linker.Link(objectFile);
            stopwatch.Stop();
            var linkTime = stopwatch.Elapsed;

            // 7. Log statistics
            Console.WriteLine($"Front-end time: {frontEndTime.TotalSeconds} seconds\n" +
                              $"LLVM build time: {buildTime.TotalSeconds} seconds\n" +
                              $"Linking time: {linkTime.TotalSeconds} seconds");
        }
    }
}
