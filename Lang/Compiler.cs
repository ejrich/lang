using System;
using System.Diagnostics;
using System.Linq;
using Lang.Parsing;
using Lang.Project;

namespace Lang
{
    public interface ICompiler
    {
        void Compile(string[] args);
    }

    public class Compiler : ICompiler
    {
        private readonly IParser _parser;
        private readonly IProjectInterpreter _projectInterpreter;

        public Compiler(IParser parser, IProjectInterpreter projectInterpreter)
        {
            _parser = parser;
            _projectInterpreter = projectInterpreter;
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

            // 3. Build dependency graph
            // 4. Generate assembly code
            // 5. Assemble and link binaries
            // 6. Clean up unused binaries

            // 7. Log statistics
            stopwatch.Stop();
            Console.WriteLine($"Project time: {projectTime.TotalSeconds} seconds");
            Console.WriteLine($"Lexing/Parsing time: {parseTime.TotalSeconds} seconds");
        }
    }
}
