using System.Collections.Generic;
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

            // 4. Write Function definitions
            foreach (var function in programGraph.Functions)
            {
                WriteFunctionDefinition(function.Name, function.Arguments, function.ReturnType);
            }

            // 5. Write Function bodies
            foreach (var function in programGraph.Functions)
            {
                WriteFunction(function);
            }

            // 6. Write Main function
            WriteMainFunction(programGraph.Main);

            // 7. Compile to object file
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

        private LLVMValueRef WriteFunctionDefinition(string name, List<Argument> arguments, TypeDefinition returnType)
        {
            var argumentTypes = arguments.Select(_ => LLVMTypeRef.Double).ToArray();
            var function = _module.AddFunction(name, LLVMTypeRef.CreateFunction(ConvertTypeDefinition(returnType), argumentTypes));
            function.Linkage = LLVMLinkage.LLVMExternalLinkage;

            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = function.GetParam((uint) i);
                argument.Name = arguments[i].Name;
            }

            return function;
        }

        private void WriteFunction(FunctionAst functionAst)
        {
            // 1. Get function definition
            var function = _module.GetNamedFunction(functionAst.Name);
            _builder.PositionAtEnd(function.AppendBasicBlock("entry"));

            // 2. Loop through function body
            foreach (var ast in functionAst.Children)
            {
                // 2a. Recursively write out lines
                WriteFunctionLine(ast);
            }

            // 3. Verify the function
            function.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
        }

        private void WriteMainFunction(FunctionAst main)
        {
            // 1. Define main function
            var function = WriteFunctionDefinition("main", main.Arguments, main.ReturnType);
            _builder.PositionAtEnd(function.AppendBasicBlock("entry"));

            // 2. Loop through function body
            foreach (var ast in main.Children)
            {
                // 2a. Recursively write out lines
                WriteFunctionLine(ast);
            }

            // 3. Verify the function
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

        private void WriteFunctionLine(IAst ast)
        {
            switch (ast)
            {
                case ReturnAst returnAst:
                    WriteReturnStatement(returnAst);
                    break;
                case DeclarationAst declaration:
                    WriteDeclaration(declaration);
                    break;
                case AssignmentAst assignment:
                    WriteAssignment(assignment);
                    break;
                case ScopeAst scope:
                    WriteScope(scope.Children);
                    break;
                case ConditionalAst conditional:
                    WriteConditional(conditional);
                    break;
                case WhileAst whileAst:
                    WriteWhile(whileAst);
                    break;
                case EachAst each:
                    WriteEach(each);
                    break;
                default:
                    WriteExpression(ast);
                    break;
            }
        }

        private void WriteReturnStatement(ReturnAst returnAst)
        {
            // 1. Determine to write constant, variable, or evaluate an expression
            switch (returnAst.Value)
            {
                case ConstantAst constant:
                    var type = ConvertTypeDefinition(constant.Type);
                    switch (type.Kind)
                    {
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            _builder.BuildRet(LLVMValueRef.CreateConstInt(type, ulong.Parse(constant.Value), true));
                            break;
                    }
                    return;
                case VariableAst variable:
                    break;
            }

            // 2. Write expression as return value
            _builder.BuildRet(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0));
        }

        private void WriteDeclaration(DeclarationAst declaration)
        {
            // TODO Implement me
            // _builder.BuildAlloca();
        }
        
        private void WriteAssignment(AssignmentAst assignment)
        {
            // TODO Implement me
        }

        private void WriteScope(List<IAst> scopeChildren)
        {
            // TODO Implement me
        }

        private void WriteConditional(ConditionalAst conditional)
        {
            // TODO Implement me
        }

        private void WriteWhile(WhileAst whileAst)
        {
            // TODO Implement me
        }

        private void WriteEach(EachAst each)
        {
            // TODO Implement me
        }

        private void WriteExpression(IAst ast)
        {
            // TODO Implement me
        }

        private static LLVMTypeRef ConvertTypeDefinition(TypeDefinition typeDef)
        {
            switch (typeDef.Name)
            {
                case "int":
                    return LLVMTypeRef.Int32;
                case "float":
                    return LLVMTypeRef.Float;
                // TODO Add more type inference
                default:
                    return LLVMTypeRef.Double;
            }
        }
    }
}
