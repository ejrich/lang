using Lang;
using Lang.Backend;
using Lang.Backend.LLVM;
using Lang.Parsing;
using Lang.Project;
using Lang.Translation;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection.AddTransient<ICompiler, Compiler>();
serviceCollection.AddTransient<ILexer, Lexer>();
serviceCollection.AddTransient<IParser, Parser>();
serviceCollection.AddTransient<IProjectInterpreter, ProjectInterpreter>();
serviceCollection.AddTransient<IProgramGraphBuilder, ProgramGraphBuilder>();
// LLVM Backend
serviceCollection.AddTransient<IWriter, LLVMWriter>();
serviceCollection.AddTransient<ILinker, LLVMLinker>();
serviceCollection.AddTransient<IBackend, LLVMBackend>();

var container = serviceCollection.BuildServiceProvider();

var compiler = container.GetService<ICompiler>();
compiler.Compile(args);
