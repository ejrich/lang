﻿using System.Collections.Generic;
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
            var argumentTypes = arguments.Select(arg => ConvertTypeDefinition(arg.Type)).ToArray();
            var function = _module.AddFunction(name, LLVMTypeRef.CreateFunction(ConvertTypeDefinition(returnType), argumentTypes));

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
            var localVariables = new Dictionary<string, LLVMValueRef>();

            // 2. Allocate arguments on the stack
            for (var i = 0; i < functionAst.Arguments.Count; i++)
            {
                var argument = function.GetParam((uint) i);
                var allocation = _builder.BuildAlloca(ConvertTypeDefinition(functionAst.Arguments[i].Type), argument.Name);
                _builder.BuildStore(argument, allocation);
                localVariables.Add(argument.Name, allocation);
            }

            // 3. Loop through function body
            foreach (var ast in functionAst.Children)
            {
                // 3a. Recursively write out lines
                WriteFunctionLine(ast, localVariables);
            }

            // 4. Verify the function
            function.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
        }

        private void WriteMainFunction(FunctionAst main)
        {
            // 1. Define main function
            var function = WriteFunctionDefinition("main", main.Arguments, main.ReturnType);
            _builder.PositionAtEnd(function.AppendBasicBlock("entry"));
            var localVariables = new Dictionary<string, LLVMValueRef>();

            // 2. Allocate arguments on the stack
            for (var i = 0; i < main.Arguments.Count; i++)
            {
                var argument = function.GetParam((uint) i);
                var allocation = _builder.BuildAlloca(ConvertTypeDefinition(main.Arguments[i].Type), argument.Name);
                _builder.BuildStore(argument, allocation);
                localVariables.Add(argument.Name, allocation);
            }

            // 2. Loop through function body
            foreach (var ast in main.Children)
            {
                // 2a. Recursively write out lines
                WriteFunctionLine(ast, localVariables);
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

        private void WriteFunctionLine(IAst ast, IDictionary<string, LLVMValueRef> localVariables)
        {
            switch (ast)
            {
                case ReturnAst returnAst:
                    WriteReturnStatement(returnAst, localVariables);
                    break;
                case DeclarationAst declaration:
                    WriteDeclaration(declaration, localVariables);
                    break;
                case AssignmentAst assignment:
                    WriteAssignment(assignment, localVariables);
                    break;
                case ScopeAst scope:
                    WriteScope(scope.Children, localVariables);
                    break;
                case ConditionalAst conditional:
                    WriteConditional(conditional, localVariables);
                    break;
                case WhileAst whileAst:
                    WriteWhile(whileAst, localVariables);
                    break;
                case EachAst each:
                    WriteEach(each, localVariables);
                    break;
                default:
                    WriteExpression(ast, localVariables);
                    break;
            }
        }

        private void WriteReturnStatement(ReturnAst returnAst, IDictionary<string, LLVMValueRef> localVariables)
        {
            // 1. Get the return value
            var returnValue = WriteExpression(returnAst.Value, localVariables);

            // 2. Write expression as return value
            _builder.BuildRet(returnValue);
        }

        private void WriteDeclaration(DeclarationAst declaration, IDictionary<string, LLVMValueRef> localVariables)
        {
            // 1. Declare variable on the stack
            var allocation = _builder.BuildAlloca(ConvertTypeDefinition(declaration.Type), declaration.Name);
            
            // 2. Set value if it exists
            if (declaration.Value != null)
            {
                var expressionValue = WriteExpression(declaration.Value, localVariables);
                _builder.BuildStore(expressionValue, allocation);
                localVariables.Add(declaration.Name, allocation);
            }
            else
            {
                localVariables.Add(declaration.Name, null);
            }
        }
        
        private void WriteAssignment(AssignmentAst assignment, IDictionary<string, LLVMValueRef> localVariables)
        {
            // 1. Get the variable on the stack
            var variableName = assignment.Variable switch
            {
                VariableAst var => var.Name,
                StructFieldRefAst fieldRef => fieldRef.Name,
                _ => string.Empty
            };
            var variable = localVariables[variableName];

            // 2. Evaluate the expression value
            var expressionValue = WriteExpression(assignment.Value, localVariables);
            if (assignment.Operator != Operator.None)
            {
                // TODO If operator exists, create expression using the existing expression value
            }

            // 3. Reallocate the value of the variable
            // TODO Set struct fields
            _builder.BuildStore(expressionValue, variable);
        }

        private void WriteScope(List<IAst> scopeChildren, IDictionary<string, LLVMValueRef> localVariables)
        {
            // TODO Implement me
        }

        private void WriteConditional(ConditionalAst conditional, IDictionary<string, LLVMValueRef> localVariables)
        {
            // TODO Implement me
        }

        private void WriteWhile(WhileAst whileAst, IDictionary<string, LLVMValueRef> localVariables)
        {
            // TODO Implement me
        }

        private void WriteEach(EachAst each, IDictionary<string, LLVMValueRef> localVariables)
        {
            // TODO Implement me
        }

        private LLVMValueRef WriteExpression(IAst ast, IDictionary<string, LLVMValueRef> localVariables)
        {
            switch (ast)
            {
                case ConstantAst constant:
                    var type = ConvertTypeDefinition(constant.Type);
                    switch (type.Kind)
                    {
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            // Specific case for parsing booleans
                            if (type == LLVMTypeRef.Int1)
                            {
                                return LLVMValueRef.CreateConstInt(type, constant.Value == "true" ? 1 : 0);
                            }
                            return LLVMValueRef.CreateConstInt(type, ulong.Parse(constant.Value), true);
                        case LLVMTypeKind.LLVMFloatTypeKind:
                            return LLVMValueRef.CreateConstRealOfStringAndSize(type, constant.Value, (uint) constant.Value.Length);
                        // TODO Implement more branches
                        default:
                            break;
                    }
                    break;
                case VariableAst variable:
                    return _builder.BuildLoad(localVariables[variable.Name]);
                case StructFieldRefAst structField:
                    // TODO Implement me
                    break;
                case CallAst call:
                    var function = _module.GetNamedFunction(call.Function);
                    var callArguments = new LLVMValueRef[call.Arguments.Count];
                    for (var i = 0; i < call.Arguments.Count; i++)
                    {
                        var value = WriteExpression(call.Arguments[i], localVariables);
                        callArguments[i] = value;
                    }
                    return _builder.BuildCall(function, callArguments, "callTmp");
                case ChangeByOneAst changeByOne:
                    if (changeByOne.Variable is VariableAst changeVariable)
                    {
                        var variable = localVariables[changeVariable.Name];
                        var value = _builder.BuildLoad(variable);

                        LLVMValueRef newValue;
                        if (value.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
                        {
                            newValue = changeByOne.Operator == Operator.Increment
                                ? _builder.BuildAdd(value, LLVMValueRef.CreateConstInt(value.TypeOf, 1), "inc")
                                : _builder.BuildSub(value, LLVMValueRef.CreateConstInt(value.TypeOf, 1), "dec");
                        }
                        else
                        {
                            newValue = changeByOne.Operator == Operator.Increment
                                ? _builder.BuildFAdd(value, LLVMValueRef.CreateConstReal(value.TypeOf, 1), "incf")
                                : _builder.BuildFSub(value, LLVMValueRef.CreateConstReal(value.TypeOf, 1), "decf");
                        }

                        _builder.BuildStore(newValue, variable);
                        return changeByOne.Prefix ? newValue : value;
                    }
                    else
                    {
                        // TODO Implement StructFieldRef writing
                    }
                    break;
                case NotAst not:
                    var notValue = WriteExpression(not.Value, localVariables);
                    return _builder.BuildNot(notValue, "not");
                case ExpressionAst expression:
                    // TODO Implement expression writing
                    break;
                default:
                    return null;
            }

            return LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0);
        }

        private static LLVMTypeRef ConvertTypeDefinition(TypeDefinition typeDef)
        {
            switch (typeDef.Name)
            {
                case "int":
                    return LLVMTypeRef.Int32;
                case "float":
                    return LLVMTypeRef.Float;
                case "bool":
                    return LLVMTypeRef.Int1;
                // TODO Add more type inference
                default:
                    return LLVMTypeRef.Double;
            }
        }
    }
}
