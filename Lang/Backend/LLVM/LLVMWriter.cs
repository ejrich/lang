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
        private readonly Dictionary<string, TypeDefinition> _functionTypes = new();
        private readonly Dictionary<string, StructAst> _structs = new();
        private FunctionAst _currentFunction;

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
                _currentFunction = function;
                WriteFunction(function);
            }

            // 6. Write Main function
            _currentFunction = programGraph.Main;
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
            // 1. Declare structs
            var structs = new LLVMTypeRef[data.Structs.Count];
            for (var i = 0; i < data.Structs.Count; i++)
            {
                var structAst = data.Structs[i];
                structs[i] = LLVMApi.StructCreateNamed(LLVMApi.GetModuleContext(_module), structAst.Name);
                _structs.Add(structAst.Name, structAst);
            }
            for (var i = 0; i < data.Structs.Count; i++)
            {
                var fields = data.Structs[i].Fields.Select(field => ConvertTypeDefinition(field.Type)).ToArray();
                LLVMApi.StructSetBody(structs[i], fields, false);
            }

            // 2. Declare variables
            // TODO Implement me
        }

        private LLVMValueRef WriteFunctionDefinition(string name, List<Argument> arguments, TypeDefinition returnType)
        {
            _functionTypes.Add(name, returnType);
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
            var localVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>();

            // 2. Allocate arguments on the stack
            for (var i = 0; i < functionAst.Arguments.Count; i++)
            {
                var argument = LLVMApi.GetParam(function, (uint) i);
                var arg = functionAst.Arguments[i];
                var allocation = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(arg.Type), arg.Name);
                LLVMApi.BuildStore(_builder, argument, allocation);
                localVariables.Add(arg.Name, (arg.Type, allocation));
            }

            // 3. Loop through function body
            foreach (var ast in functionAst.Children)
            {
                // 3a. Recursively write out lines
                WriteFunctionLine(ast, localVariables, function);
            }

            // 4. Verify the function
            function.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
        }

        private void WriteMainFunction(FunctionAst main)
        {
            // 1. Define main function
            var function = WriteFunctionDefinition("main", main.Arguments, main.ReturnType);
            LLVMApi.PositionBuilderAtEnd(_builder, LLVMApi.AppendBasicBlock(function, "entry"));
            var localVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>();

            // 2. Allocate arguments on the stack
            for (var i = 0; i < main.Arguments.Count; i++)
            {
                var argument = function.GetParam((uint) i);
                var arg = main.Arguments[i];
                var allocation = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(arg.Type), arg.Name);
                LLVMApi.BuildStore(_builder, argument, allocation);
                localVariables.Add(arg.Name, (arg.Type, allocation));
            }

            // 2. Loop through function body
            foreach (var ast in main.Children)
            {
                // 2a. Recursively write out lines
                WriteFunctionLine(ast, localVariables, function);
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

        private bool WriteFunctionLine(IAst ast, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            switch (ast)
            {
                case ReturnAst returnAst:
                    WriteReturnStatement(returnAst, localVariables);
                    return true;
                case DeclarationAst declaration:
                    WriteDeclaration(declaration, localVariables);
                    break;
                case AssignmentAst assignment:
                    WriteAssignment(assignment, localVariables);
                    break;
                case ScopeAst scope:
                    return WriteScope(scope.Children, localVariables, function);
                case ConditionalAst conditional:
                    return WriteConditional(conditional, localVariables, function);
                case WhileAst whileAst:
                    return WriteWhile(whileAst, localVariables, function);
                case EachAst each:
                    return WriteEach(each, localVariables, function);
                default:
                    WriteExpression(ast, localVariables);
                    break;
            }
            return false;
        }

        private void WriteReturnStatement(ReturnAst returnAst, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Get the return value
            var (_, returnValue) = WriteExpression(returnAst.Value, localVariables);

            // 2. Write expression as return value
            returnValue = CastValue(returnValue, _currentFunction.ReturnType);
            LLVMApi.BuildRet(_builder, returnValue);
        }

        private void WriteDeclaration(DeclarationAst declaration, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Declare variable on the stack
            var type = ConvertTypeDefinition(declaration.Type);
            var variable = LLVMApi.BuildAlloca(_builder, type, declaration.Name);
            localVariables.Add(declaration.Name, (declaration.Type, variable));

            // 2. Set value if it exists
            if (declaration.Value != null)
            {
                var (_, expressionValue) = WriteExpression(declaration.Value, localVariables);
                var value = CastValue(expressionValue, type);

                LLVMApi.BuildStore(_builder, value, variable);
            }
            // 3. Initialize struct field default values
            else if (type.TypeKind == LLVMTypeKind.LLVMStructTypeKind)
            {
                InitializeStruct(declaration.Type, variable);
            }
            // 4. Or initialize to 0
            else
            {
                var zero = GetConstZero(type);
                LLVMApi.BuildStore(_builder, zero, variable);
            }
        }

        private void InitializeStruct(TypeDefinition typeDef, LLVMValueRef variable)
        {
            var structDef = _structs[typeDef.Name];
            for (var i = 0; i < structDef.Fields.Count; i++)
            {
                var structField = structDef.Fields[i];
                var field = LLVMApi.BuildStructGEP(_builder, variable, (uint) i, structField.Name);

                var type = ConvertTypeDefinition(structField.Type);
                if (type.TypeKind == LLVMTypeKind.LLVMStructTypeKind)
                {
                    InitializeStruct(structField.Type, field);
                }
                else
                {
                    var defaultValue = structField.DefaultValue == null ? GetConstZero(type) : BuildConstant(type, structField.DefaultValue);
                    LLVMApi.BuildStore(_builder, defaultValue, field);
                }
            }
        }

        private static LLVMValueRef GetConstZero(LLVMTypeRef type)
        {
            return type.TypeKind switch
            {
                LLVMTypeKind.LLVMIntegerTypeKind => LLVMApi.ConstInt(type, 0, false),
                LLVMTypeKind.LLVMFloatTypeKind => LLVMApi.ConstReal(type, 0),
                _ => LLVMApi.ConstInt(type, 0, false)
            };
        }

        private void WriteAssignment(AssignmentAst assignment, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Get the variable on the stack
            var variableName = assignment.Variable switch
            {
                VariableAst var => var.Name,
                StructFieldRefAst fieldRef => fieldRef.Name,
                _ => string.Empty
            };
            var variable = localVariables[variableName];
            if (assignment.Variable is StructFieldRefAst structField)
            {
                variable = BuildStructField(structField, variable.value);
            }

            // 2. Evaluate the expression value
            var (type, expressionValue) = WriteExpression(assignment.Value, localVariables);
            if (assignment.Operator != Operator.None)
            {
                // 2a. Build expression with variable value as the LHS
                var value = LLVMApi.BuildLoad(_builder, variable.value, variableName);
                expressionValue = BuildExpression((variable.type, value), (type, expressionValue), assignment.Operator, variable.type);
            }

            // 3. Reallocate the value of the variable
            expressionValue = CastValue(expressionValue, variable.type);
            LLVMApi.BuildStore(_builder, expressionValue, variable.value);
        }

        private bool WriteScope(List<IAst> scopeChildren, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(localVariables);

            // 2. Write function lines
            foreach (var ast in scopeChildren)
            {
                WriteFunctionLine(ast, scopeVariables, function);
                if (ast is ReturnAst)
                {
                    return true;
                }
            }
            return false;
        }

        private bool WriteConditional(ConditionalAst conditional, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            // 1. Write out the condition
            var (_, conditionExpression) = WriteExpression(conditional.Condition, localVariables);

            // 2. Write out the condition jump and blocks
            var condition = conditionExpression.TypeOf().TypeKind switch
            {
                LLVMTypeKind.LLVMIntegerTypeKind => LLVMApi.BuildICmp(_builder, LLVMIntPredicate.LLVMIntNE,
                    conditionExpression, LLVMApi.ConstInt(conditionExpression.TypeOf(), 0, false), "ifcond"),
                LLVMTypeKind.LLVMFloatTypeKind => LLVMApi.BuildFCmp(_builder, LLVMRealPredicate.LLVMRealONE,
                    conditionExpression, LLVMApi.ConstReal(conditionExpression.TypeOf(), 0), "ifcond"),
                _ => new LLVMValueRef()
            };
            var thenBlock = LLVMApi.AppendBasicBlock(function, "then");
            var elseBlock = LLVMApi.AppendBasicBlock(function, "else");
            var endBlock = LLVMApi.AppendBasicBlock(function, "ifcont");
            LLVMApi.BuildCondBr(_builder, condition, thenBlock, elseBlock);
            
            // 3. Write out if body
            LLVMApi.PositionBuilderAtEnd(_builder, thenBlock);
            var ifReturned = false;
            foreach (var ast in conditional.Children)
            {
                ifReturned = WriteFunctionLine(ast, localVariables, function);
                if (ifReturned)
                {
                    break;
                }
            }
            if (!ifReturned)
            {
                LLVMApi.BuildBr(_builder, endBlock);
            }

            // 4. Write out the else if necessary
            LLVMApi.PositionBuilderAtEnd(_builder, elseBlock);
            var elseReturned = false;
            if (conditional.Else != null)
            {
                elseReturned = WriteFunctionLine(conditional.Else, localVariables, function);
            }

            // 5. Jump to end block if necessary
            if (!elseReturned)
            {
                LLVMApi.BuildBr(_builder, endBlock);
            }

            // 6. Position builder at end block or delete if both branches have returned
            if (ifReturned && elseReturned)
            {
                LLVMApi.DeleteBasicBlock(endBlock);
                return true;
            }
            LLVMApi.PositionBuilderAtEnd(_builder, endBlock);
            return false;
        }

        private bool WriteWhile(WhileAst whileAst, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            // 1. Break to the while loop
            var whileCondition = LLVMApi.AppendBasicBlock(function, "whilecondblock");
            LLVMApi.BuildBr(_builder, whileCondition);

            // 2. Check condition of while loop and break if condition is not met
            LLVMApi.PositionBuilderAtEnd(_builder, whileCondition);
            var (_, conditionExpression) = WriteExpression(whileAst.Condition, localVariables);
            var condition = conditionExpression.TypeOf().TypeKind switch
            {
                LLVMTypeKind.LLVMIntegerTypeKind => LLVMApi.BuildICmp(_builder, LLVMIntPredicate.LLVMIntEQ,
                    conditionExpression, LLVMApi.ConstInt(conditionExpression.TypeOf(), 1, false), "whilecond"),
                LLVMTypeKind.LLVMFloatTypeKind => LLVMApi.BuildFCmp(_builder, LLVMRealPredicate.LLVMRealOEQ,
                    conditionExpression, LLVMApi.ConstReal(conditionExpression.TypeOf(), 1), "whilecond"),
                _ => new LLVMValueRef()
            };
            var whileBody = LLVMApi.AppendBasicBlock(function, "whilebody");
            var afterWhile = LLVMApi.AppendBasicBlock(function, "afterwhile");
            LLVMApi.BuildCondBr(_builder, condition, whileBody, afterWhile);

            // 3. Write out while body
            LLVMApi.PositionBuilderAtEnd(_builder, whileBody);
            foreach (var ast in whileAst.Children)
            {
                var returned = WriteFunctionLine(ast, localVariables, function);
                if (returned)
                {
                    LLVMApi.DeleteBasicBlock(afterWhile);
                    return true;
                }
            }

            // 4. Jump back to the loop
            LLVMApi.BuildBr(_builder, whileCondition);

            // 5. Position builder to after block
            LLVMApi.PositionBuilderAtEnd(_builder, afterWhile);
            return false;
        }

        private bool WriteEach(EachAst each, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            var eachVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(localVariables);

            LLVMValueRef variable;
            if (each.Iteration != null)
            {
                // TODO Implement iterators
                variable = new LLVMValueRef();
            }
            else
            {
                variable = LLVMApi.BuildAlloca(_builder, LLVMTypeRef.Int32Type(), each.IterationVariable);
                var (type, value) = WriteExpression(each.RangeBegin, localVariables);
                LLVMApi.BuildStore(_builder, value, variable);
                eachVariables.Add(each.IterationVariable, (type, variable));
            }

            // 1. Break to the each condition loop
            var eachCondition = LLVMApi.AppendBasicBlock(function, "eachcond");
            LLVMApi.BuildBr(_builder, eachCondition);

            // 2. Check condition of each loop and break if condition is not met
            LLVMApi.PositionBuilderAtEnd(_builder, eachCondition);
            LLVMValueRef condition;
            if (each.Iteration != null)
            {
                // TODO Implement iterators
                condition = new LLVMValueRef();
            }
            else
            {
                var value = LLVMApi.BuildLoad(_builder, variable, "curr");
                var (_, rangeEnd) = WriteExpression(each.RangeEnd, localVariables);
                condition = LLVMApi.BuildICmp(_builder, LLVMIntPredicate.LLVMIntSLE, value, rangeEnd, "rangecond");
            }
            var eachBody = LLVMApi.AppendBasicBlock(function, "eachbody");
            var afterEach = LLVMApi.AppendBasicBlock(function, "aftereach");
            LLVMApi.BuildCondBr(_builder, condition, eachBody, afterEach);

            // 3. Write out each loop body
            LLVMApi.PositionBuilderAtEnd(_builder, eachBody);
            foreach (var ast in each.Children)
            {
                var returned = WriteFunctionLine(ast, eachVariables, function);
                if (returned)
                {
                    LLVMApi.DeleteBasicBlock(afterEach);
                    return true;
                }
            }

            // 4. Increment or move the iteration variable
            if (each.Iteration != null)
            {
                // TODO Implement iterators
            }
            else
            {
                var value = LLVMApi.BuildLoad(_builder, variable, "iter");
                var nextValue = BuildExpression(value, LLVMApi.ConstInt(LLVMApi.Int32Type(), 1, true), Operator.Add);
                LLVMApi.BuildStore(_builder, nextValue, variable);
            }

            // 5. Write jump to the loop
            LLVMApi.BuildBr(_builder, eachCondition);

            // 6. Position builder to after block
            LLVMApi.PositionBuilderAtEnd(_builder, afterEach);
            return false;
        }

        private (TypeDefinition type, LLVMValueRef value) WriteExpression(IAst ast, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            switch (ast)
            {
                case ConstantAst constant:
                {
                    var type = ConvertTypeDefinition(constant.Type);
                    return (constant.Type, BuildConstant(type, constant));
                }
                case VariableAst variable:
                {
                    var (type, value) = localVariables[variable.Name];
                    return (type, LLVMApi.BuildLoad(_builder, value, variable.Name));
                }
                case StructFieldRefAst structField:
                {
                    var (type, field) = BuildStructField(structField, localVariables[structField.Name].value);
                    return (type, LLVMApi.BuildLoad(_builder, field, structField.Name));
                }
                case CallAst call:
                    var function = LLVMApi.GetNamedFunction(_module, call.Function);
                    var callArguments = new LLVMValueRef[call.Arguments.Count];
                    for (var i = 0; i < call.Arguments.Count; i++)
                    {
                        var value = WriteExpression(call.Arguments[i], localVariables);
                        callArguments[i] = value.value;
                    }
                    var functionType = _functionTypes[call.Function];
                    return (functionType, LLVMApi.BuildCall(_builder, function, callArguments, "callTmp"));
                case ChangeByOneAst changeByOne:
                {
                    var variableName = changeByOne.Variable switch
                    {
                        VariableAst var => var.Name,
                        StructFieldRefAst fieldRef => fieldRef.Name,
                        _ => string.Empty
                    };
                    var variable = localVariables[variableName];
                    if (changeByOne.Variable is StructFieldRefAst structField)
                    {
                        variable = BuildStructField(structField, variable.value);
                    }

                    var value = LLVMApi.BuildLoad(_builder, variable.value, variableName);
                    var type = ConvertTypeDefinition(variable.type);

                    LLVMValueRef newValue;
                    if (variable.type.PrimitiveType is IntegerType)
                    {
                        newValue = changeByOne.Positive
                            ? LLVMApi.BuildAdd(_builder, value, LLVMApi.ConstInt(type, 1, false), "inc")
                            : LLVMApi.BuildSub(_builder, value, LLVMApi.ConstInt(type, 1, false), "dec");
                    }
                    else
                    {
                        newValue = changeByOne.Positive
                            ? LLVMApi.BuildFAdd(_builder, value, LLVMApi.ConstReal(type, 1), "incf")
                            : LLVMApi.BuildFSub(_builder, value, LLVMApi.ConstReal(type, 1), "decf");
                    }

                    LLVMApi.BuildStore(_builder, newValue, variable.value);
                    return changeByOne.Prefix ? (variable.type, newValue) : (variable.type, value);
                }
                case UnaryAst unary:
                {
                    var (type, value) = WriteExpression(unary.Value, localVariables);
                    return unary.Operator switch
                    {
                        UnaryOperator.Not => (type, LLVMApi.BuildNot(_builder, value, "not")),
                        UnaryOperator.Minus => type.PrimitiveType switch
                        {
                            IntegerType => (type, LLVMApi.BuildNeg(_builder, value, "neg")),
                            FloatType => (type, LLVMApi.BuildFNeg(_builder, value, "fneg")),
                            // @Cleanup This branch should not be hit
                            _ => (null, new LLVMValueRef())
                        },
                        // @Cleanup This branch should not be hit
                        _ => (null, new LLVMValueRef())
                    };
                }
                case ExpressionAst expression:
                    var expressionValue = WriteExpression(expression.Children[0], localVariables);
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var rhs = WriteExpression(expression.Children[i], localVariables);
                        expressionValue.value = BuildExpression(expressionValue, rhs, expression.Operators[i - 1], expression.ResultingTypes[i - 1]);
                        expressionValue.type = expression.ResultingTypes[i - 1];
                    }
                    return expressionValue;
                default:
                    // @Cleanup This branch should not be hit since we've already verified that these ASTs are handled,
                    // but notify the user and exit just in case
                    Console.WriteLine("Unexpected syntax tree");
                    Environment.Exit(ErrorCodes.BuildError);
                    return (null, new LLVMValueRef()); // Return never happens
            }
        }

        private static LLVMValueRef BuildConstant(LLVMTypeRef type, ConstantAst constant)
        {
            switch (type.TypeKind)
            {
                case LLVMTypeKind.LLVMIntegerTypeKind:
                    // Specific case for parsing booleans
                    if (type.ToString() == "i1")
                    {
                        return LLVMApi.ConstInt(type, constant.Value == "true" ? 1 : 0, false);
                    }
                    var primitive = constant.Type.PrimitiveType as IntegerType;
                    return LLVMApi.ConstInt(type, (ulong)long.Parse(constant.Value), false);
                case LLVMTypeKind.LLVMFloatTypeKind:
                case LLVMTypeKind.LLVMDoubleTypeKind:
                    return LLVMApi.ConstRealOfStringAndSize(type, constant.Value, (uint) constant.Value.Length);
                // TODO Implement more branches
                default:
                    break;
            }
            return LLVMApi.ConstInt(LLVMApi.Int32Type(), 0, true);
        }

        private (TypeDefinition type, LLVMValueRef value) BuildStructField(StructFieldRefAst structField, LLVMValueRef variable)
        {
            var value = structField.Value;
            var field = LLVMApi.BuildStructGEP(_builder, variable, (uint) structField.ValueIndex, value.Name);

            if (value.Value == null)
            {
                var type = _structs[structField.StructName].Fields[structField.ValueIndex].Type;
                return (type, field);
            }

            return BuildStructField(value, field);
        }

        private LLVMValueRef BuildExpression((TypeDefinition type, LLVMValueRef value) lhs,
            (TypeDefinition type, LLVMValueRef value) rhs, Operator op, TypeDefinition targetType)
        {
            // 1. Handle simple operators like && and ||
            switch (op)
            {
                case Operator.And:
                    return LLVMApi.BuildAnd(_builder, lhs.value, rhs.value, "tmpand");
                case Operator.Or:
                    return LLVMApi.BuildOr(_builder, lhs.value, rhs.value, "tmpor");
            }

            // 2. Check the signage of the input types
            var signed = lhs.type.PrimitiveType.Signed || rhs.type.PrimitiveType.Signed;
            // TODO Implement 2a
            // 2a. If both are unsigned, expand to larger type and perform unsigned operations
            // 3. Handle compares, since the lhs and rhs should not be cast to the target type 
            switch (op)
            {
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return BuildCompare(lhs, rhs, op, signed);
            }

            // 3. Cast lhs and rhs to the target types
            lhs.value = CastValue(lhs, targetType);
            rhs.value = CastValue(rhs, targetType);

            // 4. Handle the rest of the simple operators
            switch (op)
            {
                case Operator.BitwiseAnd:
                    return LLVMApi.BuildAnd(_builder, lhs.value, rhs.value, "tmpband");
                case Operator.BitwiseOr:
                    return LLVMApi.BuildOr(_builder, lhs.value, rhs.value, "tmpbor");
                case Operator.Xor:
                    return LLVMApi.BuildXor(_builder, lhs.value, rhs.value, "tmpxor");
            }

            // 5. Handle binary operations
            return BuildBinaryOperation(lhs.value, rhs.value, op, signed);
        }

        // @Cleanup This should be removed eventually once the new function is fully implemented
        private LLVMValueRef BuildExpression(LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            return op switch
            {
                // Bit operators
                Operator.And => LLVMApi.BuildAnd(_builder, lhs, rhs, "tmpand"),
                Operator.Or => LLVMApi.BuildOr(_builder, lhs, rhs, "tmpor"),
                Operator.BitwiseAnd => LLVMApi.BuildAnd(_builder, lhs, rhs, "tmpband"),
                Operator.BitwiseOr => LLVMApi.BuildOr(_builder, lhs, rhs, "tmpbor"),
                Operator.Xor => LLVMApi.BuildXor(_builder, lhs, rhs, "tmpxor"),
                // Comparison operators
                Operator.Equality => BuildCompare(lhs, rhs, op),
                Operator.NotEqual => BuildCompare(lhs, rhs, op),
                Operator.GreaterThanEqual => BuildCompare(lhs, rhs, op),
                Operator.LessThanEqual => BuildCompare(lhs, rhs, op),
                Operator.GreaterThan => BuildCompare(lhs, rhs, op),
                Operator.LessThan => BuildCompare(lhs, rhs, op),
                // Arithmetic operators
                Operator.Add => BuildBinaryOperation(lhs, rhs, op),
                Operator.Subtract => BuildBinaryOperation(lhs, rhs, op),
                Operator.Multiply => BuildBinaryOperation(lhs, rhs, op),
                Operator.Divide => BuildBinaryOperation(lhs, rhs, op),
                Operator.Modulus => BuildBinaryOperation(lhs, rhs, op),
                // @Cleanup This branch should never be hit
                _ => new LLVMValueRef()
            };
        }

        private LLVMValueRef BuildCompare((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, Operator op, bool signed = true)
        {
            // TODO Actually implement me
            return BuildCompare(lhs.value, rhs.value, op, signed);
        }

        // @Cleanup This should be removed eventually once the new function is implemented
        private LLVMValueRef BuildCompare(LLVMValueRef lhs, LLVMValueRef rhs, Operator op, bool signed = true)
        {
            var lhsType = lhs.TypeOf();
            var rhsType = rhs.TypeOf();
            switch (lhsType.TypeKind)
            {
                case LLVMTypeKind.LLVMIntegerTypeKind:
                    switch (rhsType.TypeKind)
                    {
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                        {
                            var lhsWidth = lhsType.GetIntTypeWidth();
                            var rhsWidth = rhsType.GetIntTypeWidth();
                            if (lhsWidth > rhsWidth)
                            {
                                rhs = CastValue(rhs, lhsType);
                            }
                            else if (lhsWidth < rhsWidth)
                            {
                                lhs = CastValue(lhs, rhsType);
                            }
                            var (predicate, name) = ConvertIntOperator(op, signed);
                            return LLVMApi.BuildICmp(_builder, predicate, lhs, rhs, name);
                        }
                        case LLVMTypeKind.LLVMFloatTypeKind:
                        case LLVMTypeKind.LLVMDoubleTypeKind:
                        {
                            var (predicate, name) = ConvertRealOperator(op);
                            lhs = LLVMApi.BuildSIToFP(_builder, lhs, rhsType, "tmpfloat");
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                        }
                    }
                    break;
                case LLVMTypeKind.LLVMFloatTypeKind:
                {
                    var (predicate, name) = ConvertRealOperator(op);
                    switch (rhsType.TypeKind)
                    {
                        case LLVMTypeKind.LLVMFloatTypeKind:
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                        case LLVMTypeKind.LLVMDoubleTypeKind:
                            lhs = LLVMApi.BuildFPExt(_builder, lhs, rhsType, "tmpfloat");
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            // TODO Check if signed or unsigned
                            rhs = LLVMApi.BuildSIToFP(_builder, rhs, lhsType, "tmpfloat");
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                    }
                    break;
                }
                case LLVMTypeKind.LLVMDoubleTypeKind:
                {
                    var (predicate, name) = ConvertRealOperator(op);
                    switch (rhsType.TypeKind)
                    {
                        case LLVMTypeKind.LLVMDoubleTypeKind:
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                        case LLVMTypeKind.LLVMFloatTypeKind:
                            rhs = LLVMApi.BuildFPExt(_builder, rhs, lhsType, "tmpfloat");
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            // TODO Check if signed or unsigned
                            rhs = LLVMApi.BuildSIToFP(_builder, rhs, lhsType, "tmpfloat");
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs, rhs, name);
                    }
                    break;
                }
            }

            throw new NotImplementedException($"{op} not compatible with types '{lhs.TypeOf().TypeKind}' and '{rhs.TypeOf().TypeKind}'");
        }

        private LLVMValueRef BuildBinaryOperation(LLVMValueRef lhs, LLVMValueRef rhs, Operator op, bool signed = true)
        {
            var lhsType = lhs.TypeOf();
            var rhsType = rhs.TypeOf();
            switch (lhsType.TypeKind)
            {
                case LLVMTypeKind.LLVMIntegerTypeKind:
                    switch (rhsType.TypeKind)
                    {
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            var lhsWidth = lhsType.GetIntTypeWidth();
                            var rhsWidth = rhsType.GetIntTypeWidth();
                            if (lhsWidth > rhsWidth)
                            {
                                rhs = CastValue(rhs, lhsType);
                            }
                            else if (lhsWidth < rhsWidth)
                            {
                                lhs = CastValue(lhs, rhsType);
                            }
                            return BuildIntOperation(lhs, rhs, op, signed);
                        case LLVMTypeKind.LLVMFloatTypeKind:
                        case LLVMTypeKind.LLVMDoubleTypeKind:
                            lhs = LLVMApi.BuildSIToFP(_builder, lhs, rhsType, "tmpfloat");
                            return BuildRealOperation(lhs, rhs, op);
                    }
                    break;
                case LLVMTypeKind.LLVMFloatTypeKind:
                    switch (rhsType.TypeKind)
                    {
                        case LLVMTypeKind.LLVMFloatTypeKind:
                            return BuildRealOperation(lhs, rhs, op);
                        case LLVMTypeKind.LLVMDoubleTypeKind:
                            lhs = LLVMApi.BuildFPExt(_builder, lhs, rhsType, "tmpfloat");
                            return BuildRealOperation(lhs, rhs, op);
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            // TODO Check if signed or unsigned
                            rhs = LLVMApi.BuildSIToFP(_builder, rhs, lhsType, "tmpfloat");
                            return BuildRealOperation(lhs, rhs, op);
                    }
                    break;
                case LLVMTypeKind.LLVMDoubleTypeKind:
                    switch (rhsType.TypeKind)
                    {
                        case LLVMTypeKind.LLVMDoubleTypeKind:
                            return BuildRealOperation(lhs, rhs, op);
                        case LLVMTypeKind.LLVMFloatTypeKind:
                            rhs = LLVMApi.BuildFPExt(_builder, rhs, rhsType, "tmpfloat");
                            return BuildRealOperation(lhs, rhs, op);
                        case LLVMTypeKind.LLVMIntegerTypeKind:
                            // TODO Check if signed or unsigned
                            rhs = LLVMApi.BuildSIToFP(_builder, rhs, lhsType, "tmpfloat");
                            return BuildRealOperation(lhs, rhs, op);
                    }
                    break;
            }

            throw new NotImplementedException($"{op} not compatible with types '{lhs.TypeOf().TypeKind}' and '{rhs.TypeOf().TypeKind}'");
        }

        private LLVMValueRef CastValue((TypeDefinition type, LLVMValueRef value) value, TypeDefinition targetType)
        {
            // TODO Implement better type casting
            return CastValue(value.value, targetType);
        }

        private LLVMValueRef CastValue(LLVMValueRef value, TypeDefinition targetType)
        {
            return CastValue(value, ConvertTypeDefinition(targetType), targetType.PrimitiveType?.Signed);
        }

        private LLVMValueRef CastValue(LLVMValueRef value, LLVMTypeRef targetType, bool? signed = true)
        {
            var a = value.TypeOf().PrintTypeToString();
            var b = targetType.PrintTypeToString();
            if (a == b) return value;

            switch (value.TypeOf().TypeKind)
            {
                case LLVMTypeKind.LLVMIntegerTypeKind:
                    return LLVMApi.BuildIntCast(_builder, value, targetType, "tmpint");
                case LLVMTypeKind.LLVMFloatTypeKind:
                case LLVMTypeKind.LLVMDoubleTypeKind:
                    return LLVMApi.BuildFPCast(_builder, value, targetType, "tmpfp");
            }

            return value;
        }

        private static (LLVMIntPredicate predicate, string name) ConvertIntOperator(Operator op, bool signed = true)
        {
            return op switch
            {
                Operator.Equality => (LLVMIntPredicate.LLVMIntEQ, "tmpeq"),
                Operator.NotEqual => (LLVMIntPredicate.LLVMIntNE, "tmpne"),
                Operator.GreaterThan => (signed ? LLVMIntPredicate.LLVMIntSGT : LLVMIntPredicate.LLVMIntUGT, "tmpgt"),
                Operator.GreaterThanEqual => (signed ? LLVMIntPredicate.LLVMIntSGE : LLVMIntPredicate.LLVMIntUGE, "tmpgte"),
                Operator.LessThan => (signed ? LLVMIntPredicate.LLVMIntSLT : LLVMIntPredicate.LLVMIntULT, "tmplt"),
                Operator.LessThanEqual => (signed ? LLVMIntPredicate.LLVMIntSLE : LLVMIntPredicate.LLVMIntULE, "tmplte"),
                // @Cleanup This branch should never be hit
                _ => (LLVMIntPredicate.LLVMIntEQ, "tmpeq")
            };
        }

        private static (LLVMRealPredicate predicate, string name) ConvertRealOperator(Operator op)
        {
            return op switch
            {
                Operator.Equality => (LLVMRealPredicate.LLVMRealOEQ, "tmpeq"),
                Operator.NotEqual => (LLVMRealPredicate.LLVMRealONE, "tmpne"),
                Operator.GreaterThan => (LLVMRealPredicate.LLVMRealOGT, "tmpgt"),
                Operator.GreaterThanEqual => (LLVMRealPredicate.LLVMRealOGE, "tmpgte"),
                Operator.LessThan => (LLVMRealPredicate.LLVMRealOLT, "tmplt"),
                Operator.LessThanEqual => (LLVMRealPredicate.LLVMRealOLE, "tmplte"),
               // @Cleanup This branch should never be hit
                _ => (LLVMRealPredicate.LLVMRealOEQ, "tmpeq")
            };
        }

        private LLVMValueRef BuildIntOperation(LLVMValueRef lhs, LLVMValueRef rhs, Operator op, bool signed = true)
        {
            return op switch
            {
                Operator.Add => LLVMApi.BuildAdd(_builder, lhs, rhs, "tmpadd"),
                Operator.Subtract => LLVMApi.BuildSub(_builder, lhs, rhs, "tmpsub"),
                Operator.Multiply => LLVMApi.BuildMul(_builder, lhs, rhs, "tmpmul"),
                Operator.Divide => signed ? LLVMApi.BuildSDiv(_builder, lhs, rhs, "tmpdiv") :
                    LLVMApi.BuildUDiv(_builder, lhs, rhs, "tmpdiv"),
                Operator.Modulus => signed ? LLVMApi.BuildSRem(_builder, lhs, rhs, "tmpmod") :
                    LLVMApi.BuildURem(_builder, lhs, rhs, "tmpmod"),
                // @Cleanup This branch should never be hit
                _ => new LLVMValueRef()
            };
        }

        private LLVMValueRef BuildRealOperation(LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            return op switch
            {
                Operator.Add => LLVMApi.BuildFAdd(_builder, lhs, rhs, "tmpadd"),
                Operator.Subtract => LLVMApi.BuildFSub(_builder, lhs, rhs, "tmpsub"),
                Operator.Multiply => LLVMApi.BuildFMul(_builder, lhs, rhs, "tmpmul"),
                Operator.Divide => LLVMApi.BuildFDiv(_builder, lhs, rhs, "tmpdiv"),
                Operator.Modulus => LLVMApi.BuildFRem(_builder, lhs, rhs, "tmpmod"),
                // @Cleanup This branch should never be hit
                _ => new LLVMValueRef()
            };
        }

        private LLVMTypeRef ConvertTypeDefinition(TypeDefinition typeDef)
        {
            return typeDef.PrimitiveType switch
            {
                IntegerType integerType => integerType.Bytes switch
                {
                    1 => LLVMTypeRef.Int8Type(),
                    2 => LLVMTypeRef.Int16Type(),
                    4 => LLVMTypeRef.Int32Type(),
                    8 => LLVMTypeRef.Int64Type(),
                    _ => LLVMTypeRef.Int32Type()
                },
                FloatType floatType => floatType.Bytes == 8 ? LLVMApi.DoubleType() : LLVMTypeRef.FloatType(),
                _ => typeDef.Name switch
                {
                    "bool" => LLVMTypeRef.Int1Type(),
                    // TODO Implement more types
                    "List" => LLVMTypeRef.DoubleType(),
                    _ => LLVMApi.GetTypeByName(_module, typeDef.Name)
                }
            };
        }
    }
}
