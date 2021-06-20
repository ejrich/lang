using Lang;
using Lang.Backend;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection.AddTransient<ICompiler, Compiler>();
serviceCollection.AddTransient<ILexer, Lexer>();
serviceCollection.AddTransient<IParser, Parser>();
serviceCollection.AddTransient<IProjectInterpreter, ProjectInterpreter>();
serviceCollection.AddTransient<IProgramGraphBuilder, ProgramGraphBuilder>();
serviceCollection.AddTransient<IPolymorpher, Polymorpher>();
serviceCollection.AddTransient<IProgramRunner, ProgramRunner>();
// LLVM Backend
serviceCollection.AddTransient<IBackend, LLVMBackend>();

var container = serviceCollection.BuildServiceProvider();

var compiler = container.GetService<ICompiler>();
compiler.Compile(args);
