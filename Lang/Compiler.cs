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
            // 1. Load files in project
            var project = _projectInterpreter.LoadProject();

            // 2. Parse source files to tokens
            var parseResult = _parser.Parse(project.BuildFiles);

            // 3. Build dependency graph
            // 4. Generate assembly code
            // 5. Assemble and link binaries
            // 6. Clean up unused binaries
        }
    }
}
