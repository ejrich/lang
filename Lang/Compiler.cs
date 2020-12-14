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
            var parseResults = _parser.Parse(project.BuildFiles);
            var parseTime = stopwatch.Elapsed;

            if (parseResults.Any(_ => !_.Success))
            {
                foreach (var failedParse in parseResults.Where(_ => !_.Success))
                {
                    Console.WriteLine($"Failed to parse file: \"{failedParse.File}\":\n");
                    foreach (var parseError in failedParse.Errors)
                    {
                        Console.WriteLine($"\t{parseError.Error} at line {parseError.Token.Line}:{parseError.Token.Column}\n");
                    }
                }
                Environment.Exit(ErrorCodes.ParsingError);
            }

            // 3. Build program graph
            stopwatch.Restart();
            var programGraph = _graphBuilder.CreateProgramGraph(parseResults, out var errors);
            var graphTime = stopwatch.Elapsed;

            // 4. Generate translated code
            stopwatch.Restart();
            var translatedFile = _writer.WriteTranslatedFile(programGraph);
            var translationTime = stopwatch.Elapsed;

            // 5. Assemble and link binaries
            stopwatch.Restart();
            var objectFile = _builder.BuildTranslatedFile(translatedFile);
            _linker.Link(objectFile, project.Name);
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
