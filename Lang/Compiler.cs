using System;
using System.Diagnostics;
using System.Linq;
using Lang.Backend;
using Lang.Parsing;
using Lang.Project;
using Lang.Translation;

namespace Lang
{
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

        public Compiler(IProjectInterpreter projectInterpreter, IParser parser, IProgramGraphBuilder graphBuilder, IBackend backend)
        {
            _projectInterpreter = projectInterpreter;
            _parser = parser;
            _graphBuilder = graphBuilder;
            _backend = backend;
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
                    case "--no-stats":
                        buildSettings.NoStats = true;
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
            var projectTime = stopwatch.Elapsed;

            // 3. Parse source files to tokens
            stopwatch.Restart();
            var parseResult = _parser.Parse(project.SourceFiles);
            var parseTime = stopwatch.Elapsed;

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
            stopwatch.Restart();
            var programGraph = _graphBuilder.CreateProgramGraph(parseResult, project, buildSettings);
            var graphTime = stopwatch.Elapsed;

            if (programGraph.Errors.Any())
            {
                Console.WriteLine($"{programGraph.Errors.Count} compilation error(s):\n");
                foreach (var error in programGraph.Errors)
                {
                    Console.WriteLine($"    {project.SourceFiles[error.FileIndex].Replace(project.Path, string.Empty)}: {error.Error} at line {error.Line}:{error.Column}");
                }
                Environment.Exit(ErrorCodes.CompilationError);
            }

            // 5. Build program and link binaries
            stopwatch.Restart();
            _backend.Build(project, programGraph, buildSettings);
            stopwatch.Stop();
            var buildTime = stopwatch.Elapsed;

            // 6. Log statistics
            if (!buildSettings.NoStats)
            {
                Console.WriteLine($"Project time: {projectTime.TotalSeconds} seconds\n" +
                                  $"Lexing/Parsing time: {parseTime.TotalSeconds} seconds\n" +
                                  $"Project Graph time: {graphTime.TotalSeconds} seconds\n" +
                                  $"Building time: {buildTime.TotalSeconds} seconds");
            }
        }
    }
}
