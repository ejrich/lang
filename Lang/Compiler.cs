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
        private readonly IWriter _writer;
        private readonly IBuilder _builder;
        private readonly ILinker _linker;

        public Compiler(IProjectInterpreter projectInterpreter, IParser parser, IProgramGraphBuilder graphBuilder,
            IWriter writer, IBuilder builder, ILinker linker)
        {
            _projectInterpreter = projectInterpreter;
            _parser = parser;
            _graphBuilder = graphBuilder;
            _writer = writer;
            _builder = builder;
            _linker = linker;
        }

        public void Compile(string[] args)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            // 1. Load files in project
            var project = _projectInterpreter.LoadProject(args.FirstOrDefault());
            var projectTime = stopwatch.Elapsed;

            // 2. Parse source files to tokens
            stopwatch.Restart();
            var parseResult = _parser.Parse(project.BuildFiles);
            var parseTime = stopwatch.Elapsed;

            if (!parseResult.Success)
            {
                var currentFile = string.Empty;
                foreach (var parseError in parseResult.Errors)
                {
                    if (currentFile != parseError.File)
                    {
                        currentFile = parseError.File;
                        Console.WriteLine($"\nFailed to parse file: \"{currentFile}\":\n");
                    }
                    Console.WriteLine($"\t{parseError.Error} at line {parseError.Token.Line}:{parseError.Token.Column}");
                }
                Environment.Exit(ErrorCodes.ParsingError);
            }

            // 3. Build program graph
            stopwatch.Restart();
            var programGraph = _graphBuilder.CreateProgramGraph(parseResult, out var errors);
            var graphTime = stopwatch.Elapsed;

            if (errors.Any())
            {
                Console.WriteLine($"{errors.Count} compilation errors:\n");
                foreach (var error in errors)
                {
                    Console.WriteLine($"\t{error.File}: {error.Error} at line {error.Line}:{error.Column}");
                }
                Environment.Exit(ErrorCodes.CompilationError);
            }

            // 4. Generate translated code
            stopwatch.Restart();
            var translatedFile = _writer.WriteTranslatedFile(programGraph, project.Name, project.Path);
            var translationTime = stopwatch.Elapsed;

            // 5. Assemble and link binaries
            stopwatch.Restart();
            var objectFile = _builder.BuildTranslatedFile(translatedFile);
            _linker.Link(objectFile, project.Path);
            var buildTime = stopwatch.Elapsed;

            // 6. Log statistics
            stopwatch.Stop();
            Console.WriteLine($"Project time: {projectTime.TotalSeconds} seconds");
            Console.WriteLine($"Lexing/Parsing time: {parseTime.TotalSeconds} seconds");
            Console.WriteLine($"Project Graph time: {graphTime.TotalSeconds} seconds");
            Console.WriteLine($"Writing time: {translationTime.TotalSeconds} seconds");
            Console.WriteLine($"Building time: {buildTime.TotalSeconds} seconds");
        }
    }
}
