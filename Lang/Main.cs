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
// serviceCollection.AddTransient<IProgramRunner, ProgramRunner>();
serviceCollection.AddTransient<_IProgramRunner, _ProgramRunner>();
serviceCollection.AddTransient<IProgramIRBuilder, ProgramIRBuilder>();
serviceCollection.AddTransient<ILinker, Linker>();
// LLVM Backend
serviceCollection.AddTransient<IBackend, LLVMBackend>();

var container = serviceCollection.BuildServiceProvider();

var compiler = container.GetService<ICompiler>();
compiler.Compile(args);
