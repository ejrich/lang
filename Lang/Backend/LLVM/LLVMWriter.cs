using System;
using System.IO;
using System.Linq;
using Lang.Parsing;
using LLVMSharp.Interop;
using LLVMApi = LLVMSharp.Interop.LLVM;

namespace Lang.Backend.LLVM
{
    public class LLVMWriter : IWriter
    {
        private const string ObjectDirectory = "obj";

        private LLVMModuleRef _module;
        private LLVMBuilderRef _builder;

        public string WriteFile(ProgramGraph programGraph, string projectName, string projectPath)
        {
            // 1. Initialize the LLVM module and builder
            InitLLVM(projectName);

            // 2. Verify obj directory exists
            var objectPath = Path.Combine(projectPath, ObjectDirectory);
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);

            var objectFile = Path.Combine(objectPath, $"{projectName}.o");

            // 3. Write Data section
            WriteData(programGraph.Data);

            // 4. Write Functions
            foreach (var function in programGraph.Functions)
            {
                WriteFunction(function);
            }

            // 5. Write Main function
            WriteMainFunction(programGraph.Main);

            // 6. Compile to object file
            #if DEBUG
            _module.TryPrintToFile(Path.Combine(objectPath, $"{projectName}.ll"), out _);
            #endif
            Compile(objectFile);

            return objectFile;
        }


        private void InitLLVM(string projectName)
        {
            _module = LLVMModuleRef.CreateWithName(projectName);
            _builder = LLVMBuilderRef.Create(_module.Context);
        }

        private void WriteData(Data data)
        {
            // TODO Implement me
            // 1. Declare structs
            // 2. Declare variables
        }

        private void WriteFunction(FunctionAst function)
        {
            // TODO Implement me
            // 1. Declare function definition
            // 2. Loop through function body
            // 2a. Recursively write out lines
        }

        private void WriteMainFunction(FunctionAst main)
        {
            // TODO Implement me
            var arguments = Array.Empty<LLVMTypeRef>();
            var function = _module.AddFunction("main", LLVMTypeRef.CreateFunction(LLVMTypeRef.Int32, arguments));
            function.Linkage = LLVMLinkage.LLVMExternalLinkage;
            _builder.PositionAtEnd(function.AppendBasicBlock("entry"));
            _builder.BuildRet(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0));
            function.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
        }

        private void Compile(string objectFile)
        {
            LLVMApi.InitializeX86TargetInfo();
            LLVMApi.InitializeX86Target();
            LLVMApi.InitializeX86TargetMC();
            LLVMApi.InitializeX86AsmParser();
            LLVMApi.InitializeX86AsmPrinter();

            var target = LLVMTargetRef.Targets.First(_ => _.Name == "x86-64");
            _module.Target = LLVMTargetRef.DefaultTriple;
            var targetMachine = target.CreateTargetMachine(_module.Target, "generic", "",
                LLVMCodeGenOptLevel.LLVMCodeGenLevelNone, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);
            _module.DataLayout = targetMachine.CreateTargetDataLayout();

            targetMachine.EmitToFile(_module, objectFile, LLVMCodeGenFileType.LLVMObjectFile);
        }
    }
}
