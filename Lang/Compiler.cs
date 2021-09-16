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

    public static class ErrorCodes
    {
        public const int ProjectFileNotFound = 1;
        public const int ParsingError = 2;
        public const int CompilationError = 3;
        public const int BuildError = 4;
        public const int LinkError = 5;
    }

    public interface ICompiler
    {
        void Compile(string[] args);
    }

    public class Compiler : ICompiler
    {
        private readonly IProjectInterpreter _projectInterpreter;
        private readonly IParser _parser;
        private readonly IProgramGraphBuilder _graphBuilder;
        private readonly IBackend _backend;
        private readonly ILinker _linker;

        public Compiler(IProjectInterpreter projectInterpreter, IParser parser, IProgramGraphBuilder graphBuilder, IBackend backend, ILinker linker)
        {
            _projectInterpreter = projectInterpreter;
            _parser = parser;
            _graphBuilder = graphBuilder;
            _backend = backend;
            _linker = linker;
        }

        public void Compile(string[] args)
        {
            // 1. Load cli args into build settings
            string projectPath = null;
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
                        projectPath ??= arg;
                        break;
                }
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            // 2. Load files in project
            var sourceFiles = _projectInterpreter.LoadProject(projectPath);

            // 3. Parse source files to tokens
            var parseResult = _parser.Parse(sourceFiles);

            if (parseResult.Errors.Any())
            {
                var currentFile = Int32.MinValue;
                foreach (var parseError in parseResult.Errors)
                {
                    if (currentFile != parseError.FileIndex)
                    {
                        if (currentFile != Int32.MinValue) Console.WriteLine();
                        currentFile = parseError.FileIndex;
                        Console.WriteLine($"Failed to parse file: \"{sourceFiles[currentFile].Replace(BuildSettings.Path, string.Empty)}\":");
                    }
                    Console.WriteLine($"    {parseError.Error} at line {parseError.Token.Line}:{parseError.Token.Column}");
                }
                Environment.Exit(ErrorCodes.ParsingError);
            }

            // 4. Build program graph
            var programGraph = _graphBuilder.CreateProgramGraph(parseResult);
            var frontEndTime = stopwatch.Elapsed;

            if (programGraph.Errors.Any())
            {
                Console.WriteLine($"{programGraph.Errors.Count} compilation error(s):\n");
                foreach (var error in programGraph.Errors)
                {
                    Console.WriteLine($"    {sourceFiles[error.FileIndex].Replace(BuildSettings.Path, string.Empty)}: {error.Error} at line {error.Line}:{error.Column}");
                }
                Environment.Exit(ErrorCodes.CompilationError);
            }

            // 5. Build program
            stopwatch.Restart();
            var objectFile = _backend.Build(programGraph, sourceFiles);
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
