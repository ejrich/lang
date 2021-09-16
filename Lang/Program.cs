using Lang;
using Lang.Parsing;
using Lang.Project;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection.AddTransient<ICompiler, Compiler>();
serviceCollection.AddTransient<IParser, Parser>();
serviceCollection.AddTransient<IProjectInterpreter, ProjectInterpreter>();

var container = serviceCollection.BuildServiceProvider();

var compiler = container.GetService<ICompiler>();
compiler.Compile(args);
