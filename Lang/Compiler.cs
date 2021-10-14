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
        public static List<string> Files { get; } = new();
        public static HashSet<string> Dependencies { get; } = new();
    }

    public enum LinkerType : byte
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
        private readonly ITypeChecker _typeChecker;
        private readonly IBackend _backend;
        private readonly ILinker _linker;

        public Compiler(ITypeChecker typeChecker, IBackend backend, ILinker linker)
        {
            _typeChecker = typeChecker;
            _backend = backend;
            _linker = linker;
        }

        public void Compile(string[] args)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // 1. Load cli args into build settings
            string entrypoint = null;
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
            ThreadPool.Init();
            var asts = Parser.Parse(entrypoint);

            ErrorReporter.ListErrorsAndExit(ErrorCodes.ParsingError);

            // 3. Check types and build the program ir
            _typeChecker.CheckTypes(asts);
            var frontEndTime = stopwatch.Elapsed;

            ErrorReporter.ListErrorsAndExit(ErrorCodes.CompilationError);

            // 4. Build program
            stopwatch.Restart();
            var objectFile = _backend.Build();
            stopwatch.Stop();
            var buildTime = stopwatch.Elapsed;

            // 5. Link binaries
            stopwatch.Restart();
            _linker.Link(objectFile);
            stopwatch.Stop();
            var linkTime = stopwatch.Elapsed;

            // 6. Log statistics
            Console.WriteLine($"Front-end time: {frontEndTime.TotalSeconds} seconds\n" +
                              $"LLVM build time: {buildTime.TotalSeconds} seconds\n" +
                              $"Linking time: {linkTime.TotalSeconds} seconds");
        }
    }
}
