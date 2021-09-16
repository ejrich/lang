using Microsoft.Extensions.DependencyInjection;

namespace Lang
{
    class Program
    {
        static void Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddTransient<ICompiler, Compiler>();

            var container = serviceCollection.BuildServiceProvider();

            var compiler = container.GetService<ICompiler>();
            compiler.Compile(args);
        }
    }
}
