using System;
using Microsoft.Extensions.DependencyInjection;
using ol;

var serviceCollection = new ServiceCollection();
serviceCollection.AddTransient<ICompiler, Compiler>();
serviceCollection.AddTransient<ITypeChecker, TypeChecker>();
serviceCollection.AddTransient<IPolymorpher, Polymorpher>();
serviceCollection.AddTransient<IProgramRunner, ProgramRunner>();
serviceCollection.AddTransient<IProgramIRBuilder, ProgramIRBuilder>();
serviceCollection.AddTransient<ILinker, Linker>();
// LLVM Backend
serviceCollection.AddTransient<IBackend, LLVMBackend>();

var container = serviceCollection.BuildServiceProvider();

var compiler = container.GetService<ICompiler>();
compiler.Compile(args);
Allocator.Free();
Environment.Exit(0);
