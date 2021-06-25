using System;
using System.Diagnostics;
using System.Linq;
using Lang.Backend;

namespace Lang
{
    public class BuildSettings
    {
        public bool Release { get; set; }
        public bool OutputAssembly { get; set; }
        public string ProjectPath { get; set; }
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
            var buildSettings = new BuildSettings();
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "-R":
                    case "--release":
                        buildSettings.Release = true;
                        break;
                    case "-S":
                        buildSettings.OutputAssembly = true;
                        break;
                    default:
                        buildSettings.ProjectPath ??= arg;
                        break;
                }
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            // 2. Load files in project
            var project = _projectInterpreter.LoadProject(buildSettings.ProjectPath);

            // 3. Parse source files to tokens
            var parseResult = _parser.Parse(project.SourceFiles);

            if (parseResult.Errors.Any())
            {
                var currentFile = Int32.MinValue;
                foreach (var parseError in parseResult.Errors)
                {
                    if (currentFile != parseError.FileIndex)
                    {
                        if (currentFile != Int32.MinValue) Console.WriteLine();
                        currentFile = parseError.FileIndex;
                        Console.WriteLine($"Failed to parse file: \"{project.SourceFiles[currentFile].Replace(project.Path, string.Empty)}\":");
                    }
                    Console.WriteLine($"    {parseError.Error} at line {parseError.Token.Line}:{parseError.Token.Column}");
                }
                Environment.Exit(ErrorCodes.ParsingError);
            }

            // 4. Build program graph
            var programGraph = _graphBuilder.CreateProgramGraph(parseResult, project, buildSettings);
            var frontEndTime = stopwatch.Elapsed;

            if (programGraph.Errors.Any())
            {
                Console.WriteLine($"{programGraph.Errors.Count} compilation error(s):\n");
                foreach (var error in programGraph.Errors)
                {
                    Console.WriteLine($"    {project.SourceFiles[error.FileIndex].Replace(project.Path, string.Empty)}: {error.Error} at line {error.Line}:{error.Column}");
                }
                Environment.Exit(ErrorCodes.CompilationError);
            }

            // 5. Build program
            stopwatch.Restart();
            var objectFile = _backend.Build(project, programGraph, buildSettings);
            stopwatch.Stop();
            var buildTime = stopwatch.Elapsed;

            // 6. Link binaries
            stopwatch.Restart();
            _linker.Link(objectFile, project, programGraph);
            stopwatch.Stop();
            var linkTime = stopwatch.Elapsed;

            // 7. Log statistics
            Console.WriteLine($"Front-end time: {frontEndTime.TotalSeconds} seconds\n" +
                              $"LLVM build time: {buildTime.TotalSeconds} seconds\n" +
                              $"Linking time: {linkTime.TotalSeconds} seconds");
        }
    }
}
