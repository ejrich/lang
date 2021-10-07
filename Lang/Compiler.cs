using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        public static List<string> Files { get; set; }
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
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // 1. Load cli args into build settings
            var entrypointDefined = false;
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
                        else if (entrypointDefined)
                        {
                            ErrorReporter.Report($"Multiple program entrypoints defined '{arg}'");
                        }
                        else
                        {
                            if (!File.Exists(arg))
                            {
                                ErrorReporter.Report($"Entrypoint file does not exist or is not a file '{arg}'");
                            }
                            else
                            {
                                BuildSettings.Files = new List<string> {arg};
                                BuildSettings.Path = Path.GetDirectoryName(Path.GetFullPath(arg));
                            }
                            entrypointDefined = true;
                        }
                        break;
                }
            }
            if (!entrypointDefined)
            {
                ErrorReporter.Report("Program entrypoint not defined");
            }

            ErrorReporter.ListErrorsAndExit(ErrorCodes.ArgumentsError);

            // 2. Load files in project
            // var sourceFiles = _projectInterpreter.LoadProject(entryPoint);

            // 3. Parse source files to asts
            var asts = _parser.Parse();

            ErrorReporter.ListErrorsAndExit(ErrorCodes.ParsingError);

            // 4. Check types and build the program ir
            _typeChecker.CheckTypes(asts);
            var frontEndTime = stopwatch.Elapsed;

            ErrorReporter.ListErrorsAndExit(ErrorCodes.CompilationError);

            // 5. Build program
            stopwatch.Restart();
            var objectFile = _backend.Build();
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
