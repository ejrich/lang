using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Lang.Parsing;
using LLVMSharp;
using LLVMApi = LLVMSharp.LLVM;

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
            LLVMApi.PrintModuleToFile(_module, Path.Combine(objectPath, $"{projectName}.ll"), out _);
            #endif
            Compile(objectFile);

            return objectFile;
        }


        private void InitLLVM(string projectName)
        {
            _module = LLVMApi.ModuleCreateWithName(projectName);
            _builder = LLVMApi.CreateBuilder();
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
            var function = LLVMApi.AddFunction(_module, name, LLVMApi.FunctionType(ConvertTypeDefinition(returnType), argumentTypes, false));

            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = LLVMApi.GetParam(function, (uint) i);
                LLVMApi.SetValueName(argument, arguments[i].Name);
            }

            return function;
        }

        private void WriteFunction(FunctionAst functionAst)
        {
            // 1. Get function definition
            var function = LLVMApi.GetNamedFunction(_module, functionAst.Name);
            LLVMApi.PositionBuilderAtEnd(_builder, function.AppendBasicBlock("entry"));
            var localVariables = new Dictionary<string, LLVMValueRef>();

            // 2. Allocate arguments on the stack
            for (var i = 0; i < functionAst.Arguments.Count; i++)
            {
                var argument = LLVMApi.GetParam(function, (uint) i);
                var argumentName = functionAst.Arguments[i].Name;
                var allocation = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(functionAst.Arguments[i].Type), argumentName);
                LLVMApi.BuildStore(_builder, argument, allocation);
                localVariables.Add(argumentName, allocation);
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
            LLVMApi.PositionBuilderAtEnd(_builder, LLVMApi.AppendBasicBlock(function, "entry"));
            var localVariables = new Dictionary<string, LLVMValueRef>();

            // 2. Allocate arguments on the stack
            for (var i = 0; i < main.Arguments.Count; i++)
            {
                var argument = function.GetParam((uint) i);
                var argumentName = main.Arguments[i].Name;
                var allocation = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(main.Arguments[i].Type), argumentName);
                LLVMApi.BuildStore(_builder, argument, allocation);
                localVariables.Add(argumentName, allocation);
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

            var target = LLVMApi.GetTargetFromName("x86-64");
            var targetTriple = Marshal.PtrToStringAnsi(LLVMApi.GetDefaultTargetTriple());
            LLVMApi.SetTarget(_module, targetTriple);

            var targetMachine = LLVMApi.CreateTargetMachine(target, targetTriple, "generic", "",
                LLVMCodeGenOptLevel.LLVMCodeGenLevelNone, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);
            LLVMApi.SetDataLayout(_module, Marshal.PtrToStringAnsi(LLVMApi.CreateTargetDataLayout(targetMachine).Pointer));

            var file = Marshal.StringToCoTaskMemAnsi(objectFile);
            LLVMApi.TargetMachineEmitToFile(targetMachine, _module, file, LLVMCodeGenFileType.LLVMObjectFile, out _);
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
            LLVMApi.BuildRet(_builder, returnValue);
        }

        private void WriteDeclaration(DeclarationAst declaration, IDictionary<string, LLVMValueRef> localVariables)
        {
            // 1. Declare variable on the stack
            var allocation = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(declaration.Type), declaration.Name);

            // 2. Set value if it exists
            if (declaration.Value != null)
            {
                var expressionValue = WriteExpression(declaration.Value, localVariables);
                LLVMApi.BuildStore(_builder, expressionValue, allocation);
            }
            localVariables.Add(declaration.Name, allocation);
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
                // 2a. Build expression with variable value as the LHS
                var value = LLVMApi.BuildLoad(_builder, variable, variableName);
                expressionValue = BuildExpression(value, expressionValue, assignment.Operator);
            }

            // 3. Reallocate the value of the variable
            // TODO Set struct fields
            LLVMApi.BuildStore(_builder, expressionValue, variable);
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
                    switch (type.TypeKind)
                    {
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            // Specific case for parsing booleans
                            if (type.ToString() == "i1")
                            {
                                return LLVMApi.ConstInt(type, constant.Value == "true" ? 1 : 0, false);
                            }
                            return LLVMApi.ConstInt(type, ulong.Parse(constant.Value), true);
                        case LLVMTypeKind.LLVMFloatTypeKind:
                            return LLVMApi.ConstRealOfStringAndSize(type, constant.Value, (uint) constant.Value.Length);
                        // TODO Implement more branches
                        default:
                            break;
                    }
                    break;
                case VariableAst variable:
                    return LLVMApi.BuildLoad(_builder, localVariables[variable.Name], variable.Name);
                case StructFieldRefAst structField:
                    // TODO Implement me
                    break;
                case CallAst call:
                    var function = LLVMApi.GetNamedFunction(_module, call.Function);
                    var callArguments = new LLVMValueRef[call.Arguments.Count];
                    for (var i = 0; i < call.Arguments.Count; i++)
                    {
                        var value = WriteExpression(call.Arguments[i], localVariables);
                        callArguments[i] = value;
                    }
                    return LLVMApi.BuildCall(_builder, function, callArguments, "callTmp");
                case ChangeByOneAst changeByOne:
                    if (changeByOne.Variable is VariableAst changeVariable)
                    {
                        var variable = localVariables[changeVariable.Name];
                        var value = LLVMApi.BuildLoad(_builder, variable, changeVariable.Name);

                        LLVMValueRef newValue;
                        if (value.TypeOf().TypeKind == LLVMTypeKind.LLVMIntegerTypeKind)
                        {
                            newValue = changeByOne.Operator == Operator.Increment
                                ? LLVMApi.BuildAdd(_builder, value, LLVMApi.ConstInt(value.TypeOf(), 1, false), "inc")
                                : LLVMApi.BuildSub(_builder, value, LLVMApi.ConstInt(value.TypeOf(), 1, false), "dec");
                        }
                        else
                        {
                            newValue = changeByOne.Operator == Operator.Increment
                                ? LLVMApi.BuildFAdd(_builder, value, LLVMApi.ConstReal(value.TypeOf(), 1), "incf")
                                : LLVMApi.BuildFSub(_builder, value, LLVMApi.ConstReal(value.TypeOf(), 1), "decf");
                        }

                        LLVMApi.BuildStore(_builder, newValue, variable);
                        return changeByOne.Prefix ? newValue : value;
                    }
                    else
                    {
                        // TODO Implement StructFieldRef writing
                        break;
                    }
                case NotAst not:
                    var notValue = WriteExpression(not.Value, localVariables);
                    return LLVMApi.BuildNot(_builder, notValue, "not");
                case ExpressionAst expression:
                    var expressionValue = WriteExpression(expression.Children[0], localVariables);
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var rhs = WriteExpression(expression.Children[i], localVariables);
                        expressionValue = BuildExpression(expressionValue, rhs, expression.Operators[i - 1]);
                    }
                    return expressionValue;
                default:
                    // This branch should not be hit since we've already verified that these ASTs are handled,
                    // but notify the user and exit just in case
                    Console.WriteLine("Unexpected syntax tree");
                    Environment.Exit(ErrorCodes.BuildError);
                    break;
            }

            return LLVMApi.ConstInt(LLVMApi.Int32Type(), 0, true);
        }

        private LLVMValueRef BuildExpression(LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            switch (op)
            {
                // TODO Get value type to determine correct instruction to use
                case Operator.Add:
                    return LLVMApi.BuildAdd(_builder, lhs, rhs, "tmpadd");
                case Operator.Subtract:
                    return LLVMApi.BuildSub(_builder, lhs, rhs, "tmpsub");
                case Operator.Multiply:
                    return LLVMApi.BuildMul(_builder, lhs, rhs, "tmpmul");
                case Operator.Divide:
                    return LLVMApi.BuildSDiv(_builder, lhs, rhs, "tmpdiv");
                // TODO Implement more operators
                default:
                    throw new NotImplementedException(op.ToString());
            }
        }

        private static LLVMTypeRef ConvertTypeDefinition(TypeDefinition typeDef)
        {
            switch (typeDef.Name)
            {
                case "int":
                    return LLVMTypeRef.Int32Type();
                case "float":
                    return LLVMTypeRef.FloatType();
                case "bool":
                    return LLVMTypeRef.Int1Type();
                // TODO Add more type inference
                default:
                    return LLVMTypeRef.DoubleType();
            }
        }
    }
}
