using Lang;
using Lang.Backend;
using Lang.Backend.C;
using Lang.Parsing;
using Lang.Project;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection.AddTransient<ICompiler, Compiler>();
serviceCollection.AddTransient<ILexer, Lexer>();
serviceCollection.AddTransient<IParser, Parser>();
serviceCollection.AddTransient<IProjectInterpreter, ProjectInterpreter>();
serviceCollection.AddTransient<IWriter, CWriter>();
serviceCollection.AddTransient<IBuilder, CBuilder>();
serviceCollection.AddTransient<ILinker, CLinker>();

var container = serviceCollection.BuildServiceProvider();

var compiler = container.GetService<ICompiler>();
compiler.Compile(args);
