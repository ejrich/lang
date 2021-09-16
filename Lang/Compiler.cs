using System;

namespace Lang
{
    public interface ICompiler
    {
        void Compile(string[] args);
    }

    public class Compiler : ICompiler
    {
        public void Compile(string[] args)
        {
            Console.WriteLine("Hello world!");
            // 1. Load files in project
            // 2. Parse source files to tokens
            // 3. Build dependency graph
            // 4. Generate assembly code
            // 5. Assemble and link binaries
            // 6. Clean up unused binaries
        }
    }
}
