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
        private LLVMPassManagerRef _passManager;
        private FunctionAst _currentFunction;
        private LLVMValueRef _stackPointer;
        private bool _stackPointerExists;
        private bool _stackSaved;

        private Dictionary<string, FunctionAst> _functions;
        private readonly Dictionary<string, IAst> _types = new();
        private readonly Queue<LLVMValueRef> _allocationQueue = new();

        public string WriteFile(ProgramGraph programGraph, string projectName, string projectPath, BuildSettings buildSettings)
        {
            // 1. Initialize the LLVM module and builder
            InitLLVM(projectName, buildSettings.Release);

            // 2. Verify obj directory exists
            var objectPath = Path.Combine(projectPath, ObjectDirectory);
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);

            var objectFile = Path.Combine(objectPath, $"{projectName}.o");

            // 3. Write Data section
            var globals = WriteData(programGraph.Data);

            // 4. Write Function definitions
            _functions = programGraph.Functions;
            foreach (var (name, function) in programGraph.Functions)
            {
                WriteFunctionDefinition(name, function.Arguments, function.ReturnType, function.Varargs);
            }

            // 5. Write Function bodies
            foreach (var (name, functionAst) in programGraph.Functions)
            {
                if (functionAst.Extern) continue;

                _currentFunction = functionAst;
                var function = LLVMApi.GetNamedFunction(_module, name);
                WriteFunction(functionAst, globals, function);
            }

            // 6. Write __start function
            var start = programGraph.Start;
            _currentFunction = start;
            var startFunction = WriteFunctionDefinition("main", start.Arguments, start.ReturnType);
            WriteFunction(start, globals, startFunction);

            // 7. Compile to object file
            Compile(objectFile, buildSettings.OutputAssembly);

            return objectFile;
        }

        private void InitLLVM(string projectName, bool optimize)
        {
            _module = LLVMApi.ModuleCreateWithName(projectName);
            _builder = LLVMApi.CreateBuilder();
            _passManager = LLVMApi.CreateFunctionPassManagerForModule(_module);
            if (optimize)
            {
                LLVMApi.AddBasicAliasAnalysisPass(_passManager);
                LLVMApi.AddPromoteMemoryToRegisterPass(_passManager);
                LLVMApi.AddInstructionCombiningPass(_passManager);
                LLVMApi.AddReassociatePass(_passManager);
                LLVMApi.AddGVNPass(_passManager);
                LLVMApi.AddCFGSimplificationPass(_passManager);

                LLVMApi.InitializeFunctionPassManager(_passManager);
            }
        }

        private IDictionary<string, (TypeDefinition type, LLVMValueRef value)> WriteData(Data data)
        {
            // 1. Declare structs
            var structs = new Dictionary<string, LLVMTypeRef>();
            foreach (var (name, type) in data.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        structs[name] = LLVMApi.StructCreateNamed(LLVMApi.GetModuleContext(_module), structAst.Name);
                        _types.Add(structAst.Name, structAst);
                        break;
                    case EnumAst enumAst:
                        _types.Add(enumAst.Name, enumAst);
                        break;
                }
            }
            foreach (var (name, type) in data.Types)
            {
                if (type is StructAst structAst)
                {
                    var fields = structAst.Fields.Select(field => ConvertTypeDefinition(field.Type)).ToArray();
                    LLVMApi.StructSetBody(structs[name], fields, false);
                }
            }

            // 2. Declare variables
            var globals = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>();
            foreach (var globalVariable in data.Variables)
            {
                var type = ConvertTypeDefinition(globalVariable.Type);
                var global = LLVMApi.AddGlobal(_module, type, globalVariable.Name);
                LLVMApi.SetLinkage(global, LLVMLinkage.LLVMPrivateLinkage);
                if (globalVariable.Value != null)
                {
                    LLVMApi.SetInitializer(global, BuildConstant(type, globalVariable.Value as ConstantAst));
                }
                else if (type.TypeKind != LLVMTypeKind.LLVMStructTypeKind && type.TypeKind != LLVMTypeKind.LLVMArrayTypeKind)
                {
                    LLVMApi.SetInitializer(global, GetConstZero(type));
                }
                globals.Add(globalVariable.Name, (globalVariable.Type, global));
            }

            return globals;
        }

        private LLVMValueRef WriteFunctionDefinition(string name, List<Argument> arguments, TypeDefinition returnType, bool varargs = false)
        {
            var argumentCount = varargs ? arguments.Count - 1 : arguments.Count;
            var argumentTypes = new LLVMTypeRef[argumentCount];

            // 1. Determine argument types and varargs
            for (var i = 0; i < argumentCount; i++)
            {
                argumentTypes[i] = ConvertTypeDefinition(arguments[i].Type);
            }

            // 2. Declare function
            var function = LLVMApi.AddFunction(_module, name, LLVMApi.FunctionType(ConvertTypeDefinition(returnType), argumentTypes.ToArray(), varargs));

            // 3. Set argument names
            for (var i = 0; i < argumentCount; i++)
            {
                var argument = LLVMApi.GetParam(function, (uint) i);
                LLVMApi.SetValueName(argument, arguments[i].Name);
            }

            return function;
        }

        private void WriteFunction(FunctionAst functionAst, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> globals, LLVMValueRef function)
        {
            // 1. Get function definition
            LLVMApi.PositionBuilderAtEnd(_builder, function.AppendBasicBlock("entry"));
            var localVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(globals);

            // 2. Allocate arguments on the stack
            var argumentCount = functionAst.Varargs ? functionAst.Arguments.Count - 1 : functionAst.Arguments.Count;
            for (var i = 0; i < argumentCount; i++)
            {
                var arg = functionAst.Arguments[i];
                var allocation = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(arg.Type), arg.Name);
                localVariables[arg.Name] = (arg.Type, allocation);
            }

            // 3. Build allocations at the beginning of the function
            foreach (var ast in functionAst.Children)
            {
                if (BuildAllocations(ast))
                {
                    break;
                }
            }

            // 4. Store initial argument values
            for (var i = 0; i < argumentCount; i++)
            {
                var arg = functionAst.Arguments[i];
                var argument = LLVMApi.GetParam(function, (uint) i);
                var variable = localVariables[arg.Name].value;
                LLVMApi.BuildStore(_builder, argument, variable);
            }

            // 5. Loop through function body
            var returned = false;
            foreach (var ast in functionAst.Children)
            {
                // 5a. Recursively write out lines
                if (WriteFunctionLine(ast, localVariables, function))
                {
                    returned = true;
                    break;
                }
            }
            
            // 6. Write returns for void functions
            if (!returned && functionAst.ReturnType.Name == "void")
            {
                BuildStackRestore();
                LLVMApi.BuildRetVoid(_builder);
            }
            _stackPointerExists = false; 
            _stackSaved = false;

            // 7. Verify the function
            LLVMApi.RunFunctionPassManager(_passManager, function);
            #if DEBUG
            function.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
            #endif
        }

        private void Compile(string objectFile, bool outputIntermediate)
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

            if (outputIntermediate)
            {
                var llvmIrFile = objectFile[..^1] + "ll";
                LLVMApi.PrintModuleToFile(_module, llvmIrFile, out _);

                var assemblyFile = objectFile[..^1] + "s";
                var assemblyFilePtr = Marshal.StringToCoTaskMemAnsi(assemblyFile);
                LLVMApi.TargetMachineEmitToFile(targetMachine, _module, assemblyFilePtr, LLVMCodeGenFileType.LLVMAssemblyFile, out _);
            }

            var file = Marshal.StringToCoTaskMemAnsi(objectFile);
            LLVMApi.TargetMachineEmitToFile(targetMachine, _module, file, LLVMCodeGenFileType.LLVMObjectFile, out _);
        }

        private bool BuildAllocations(IAst ast)
        {
            switch (ast)
            {
                case ReturnAst:
                    return true;
                case CallAst call:
                {
                    if (call.Params)
                    {
                        var functionDef = _functions[call.Function];

                        var paramsTypeDef = functionDef.Arguments[^1].Type;
                        var paramsType = ConvertTypeDefinition(paramsTypeDef);

                        var paramsVariable = LLVMApi.BuildAlloca(_builder, paramsType, "params");
                        _allocationQueue.Enqueue(paramsVariable);

                        var targetType = ConvertTypeDefinition(paramsTypeDef);
                        var arrayType = LLVMTypeRef.ArrayType(targetType, (uint)(call.Arguments.Count - functionDef.Arguments.Count + 1));
                        var listData = LLVMApi.BuildAlloca(_builder, arrayType, "listdata");
                        _allocationQueue.Enqueue(listData);
                    }

                    foreach (var argument in call.Arguments)
                    {
                        BuildAllocations(argument);
                    }
                    break;
                }
                case DeclarationAst declaration:
                    var type = ConvertTypeDefinition(declaration.Type);
                    var variable = LLVMApi.BuildAlloca(_builder, type, declaration.Name);
                    _allocationQueue.Enqueue(variable);

                    if (declaration.Type.Name == "List")
                    {
                        if (declaration.Type.Count is ConstantAst constant)
                        {
                            var listType = declaration.Type.Generics[0];
                            var targetType = ConvertTypeDefinition(listType);
                            var arrayType = LLVMTypeRef.ArrayType(targetType, uint.Parse(constant.Value));
                            var listData = LLVMApi.BuildAlloca(_builder, arrayType, "listdata");
                            _allocationQueue.Enqueue(listData);
                        }
                        else if (declaration.Type.Count != null)
                        {
                            BuildStackPointer();
                        }
                    }
                    else if (type.TypeKind == LLVMTypeKind.LLVMStructTypeKind)
                    {
                        BuildStructAllocations(declaration.Type.GenericName);
                    }
                    break;
                case ScopeAst:
                    return BuildAllocations(ast.Children);
                case ConditionalAst conditional:
                    BuildAllocations(conditional.Condition);
                    var ifReturned = BuildAllocations(conditional.Children);

                    if (conditional.Else.Any())
                    {
                        var elseReturned = BuildAllocations(conditional.Else);
                        return ifReturned && elseReturned;
                    }
                    break;
                case WhileAst:
                    return BuildAllocations(ast.Children);
                case EachAst each:
                    var indexVariable = LLVMApi.BuildAlloca(_builder, LLVMTypeRef.Int32Type(), each.IterationVariable);
                    _allocationQueue.Enqueue(indexVariable);

                    switch (each.Iteration)
                    {
                        case CallAst call:
                            var function = _functions[call.Function];
                            var iterationValue = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(function.ReturnType), "iterval");
                            _allocationQueue.Enqueue(iterationValue);
                            break;
                    }

                    return BuildAllocations(each.Children);
                case ExpressionAst:
                    BuildAllocations(ast.Children);
                    break;
            }
            return false;
        }

        private bool BuildAllocations(List<IAst> children)
        {
            foreach (var ast in children)
            {
                if (BuildAllocations(ast))
                {
                    return true;
                }
            }
            return false;
        }

        private void BuildStructAllocations(string name)
        {
            var structDef = _types[name] as StructAst;
            foreach (var field in structDef!.Fields)
            {
                if (field.Type.Name == "List")
                {
                    var listType = field.Type.Generics[0];
                    var targetType = ConvertTypeDefinition(listType);

                    var count = (ConstantAst)field.Type.Count;
                    var arrayType = LLVMTypeRef.ArrayType(targetType, uint.Parse(count.Value));
                    var listData = LLVMApi.BuildAlloca(_builder, arrayType, "listdata");
                    _allocationQueue.Enqueue(listData);
                    continue;
                }

                if (ConvertTypeDefinition(field.Type).TypeKind != LLVMTypeKind.LLVMStructTypeKind) continue;

                BuildStructAllocations(field.Type.GenericName);
            }
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
            // 1. Return void if the value is null
            if (returnAst.Value == null)
            {
                LLVMApi.BuildRetVoid(_builder);
                return;
            }

            // 2. Get the return value
            var returnExpression = WriteExpression(returnAst.Value, localVariables);

            // 3. Write expression as return value
            var returnValue = CastValue(returnExpression, _currentFunction.ReturnType);

            // 4. Restore the stack pointer if necessary and return
            BuildStackRestore();
            LLVMApi.BuildRet(_builder, returnValue);
        }

        private void WriteDeclaration(DeclarationAst declaration, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Declare variable on the stack
            var type = ConvertTypeDefinition(declaration.Type);
            var variable = _allocationQueue.Dequeue();
            localVariables.Add(declaration.Name, (declaration.Type, variable));

            // 2. Set value if it exists
            if (declaration.Value != null)
            {
                var expression = WriteExpression(declaration.Value, localVariables);
                var value = CastValue(expression, declaration.Type);

                LLVMApi.BuildStore(_builder, value, variable);
            }
            // 3. Initialize lists
            else if (declaration.Type.Name == "List")
            {
                var listType = declaration.Type.Generics[0];
                if (declaration.Type.Count is ConstantAst constant)
                {
                    InitializeConstList(variable, int.Parse(constant.Value), listType);
                }
                else if (declaration.Type.Count != null)
                {
                    BuildStackSave();
                    var (_, count) = WriteExpression(declaration.Type.Count, localVariables);

                    var countPointer = LLVMApi.BuildStructGEP(_builder, variable, 0, "countptr");
                    LLVMApi.BuildStore(_builder, count, countPointer);

                    var targetType = ConvertTypeDefinition(listType);
                    var listData = LLVMApi.BuildArrayAlloca(_builder, targetType, count, "listdata");
                    var dataPointer = LLVMApi.BuildStructGEP(_builder, variable, 1, "dataptr");
                    LLVMApi.BuildStore(_builder, listData, dataPointer);
                }
            }
            // 4. Initialize struct field default values
            else if (type.TypeKind == LLVMTypeKind.LLVMStructTypeKind)
            {
                InitializeStruct(declaration.Type, variable, localVariables, declaration.Assignments);
            }
            // 5. Or initialize to 0
            else if (type.TypeKind != LLVMTypeKind.LLVMArrayTypeKind)
            {
                var zero = GetConstZero(type);
                LLVMApi.BuildStore(_builder, zero, variable);
            }
        }

        private void InitializeStruct(TypeDefinition typeDef, LLVMValueRef variable,
            IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables = null, List<AssignmentAst> values = null)
        {
            var assignments = values == null ? new Dictionary<string, AssignmentAst>() : values.ToDictionary(_ => (_.Variable as VariableAst)!.Name);
            var structDef = _types[typeDef.GenericName] as StructAst;
            for (var i = 0; i < structDef!.Fields.Count; i++)
            {
                var structField = structDef.Fields[i];
                var type = ConvertTypeDefinition(structField.Type);
                if (type.TypeKind == LLVMTypeKind.LLVMArrayTypeKind)
                    continue;

                var field = LLVMApi.BuildStructGEP(_builder, variable, (uint) i, structField.Name);

                if (assignments.TryGetValue(structField.Name, out var assignment))
                {
                    var expression = WriteExpression(assignment.Value, localVariables);
                    var value = CastValue(expression, structField.Type);

                    LLVMApi.BuildStore(_builder, value, field);
                }
                else if (structField.Type.Name == "List")
                {
                    var count = (ConstantAst)structField.Type.Count;
                    InitializeConstList(field, int.Parse(count.Value), structField.Type.Generics[0]);
                }
                else switch (type.TypeKind)
                {
                    case LLVMTypeKind.LLVMPointerTypeKind:
                        LLVMApi.BuildStore(_builder, LLVMApi.ConstNull(type), field);
                        break;
                    case LLVMTypeKind.LLVMStructTypeKind:
                        InitializeStruct(structField.Type, field);
                        break;
                    default:
                        var defaultValue = structField.DefaultValue == null ? GetConstZero(type) : BuildConstant(type, structField.DefaultValue);
                        LLVMApi.BuildStore(_builder, defaultValue, field);
                        break;
                }
            }
        }

        private void InitializeConstList(LLVMValueRef list, int length, TypeDefinition listType)
        {
            // 1. Set the count field
            var countValue = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)length, false);
            var countPointer = LLVMApi.BuildStructGEP(_builder, list, 0, "countptr");
            LLVMApi.BuildStore(_builder, countValue, countPointer);

            // 2. Initialize the list data array
            var targetType = ConvertTypeDefinition(listType);
            var listData = _allocationQueue.Dequeue();
            var listDataPointer = LLVMApi.BuildBitCast(_builder, listData, LLVMTypeRef.PointerType(targetType, 0), "tmpdata");
            var dataPointer = LLVMApi.BuildStructGEP(_builder, list, 1, "dataptr");
            LLVMApi.BuildStore(_builder, listDataPointer, dataPointer);
        }

        private static LLVMValueRef GetConstZero(LLVMTypeRef type)
        {
            return type.TypeKind switch
            {
                LLVMTypeKind.LLVMIntegerTypeKind => LLVMApi.ConstInt(type, 0, false),
                LLVMTypeKind.LLVMFloatTypeKind => LLVMApi.ConstReal(type, 0),
                LLVMTypeKind.LLVMDoubleTypeKind => LLVMApi.ConstReal(type, 0),
                _ => LLVMApi.ConstInt(type, 0, false)
            };
        }

        private void WriteAssignment(AssignmentAst assignment, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Get the variable on the stack
            var (type, variable) = assignment.Variable switch
            {
                VariableAst var => localVariables[var.Name],
                StructFieldRefAst structField => BuildStructField(structField, localVariables[structField.Name].value),
                IndexAst index => GetListPointer(index, localVariables),
                // @Cleanup This branch should never be hit
                _ => (null, new LLVMValueRef())
            };

            // 2. Evaluate the expression value
            var expression = WriteExpression(assignment.Value, localVariables);
            if (assignment.Operator != Operator.None)
            {
                // 2a. Build expression with variable value as the LHS
                var value = LLVMApi.BuildLoad(_builder, variable, "tmpvalue");
                expression.value = BuildExpression((type, value), expression, assignment.Operator, type);
                expression.type = type; // The type should now be the type of the variable
            }

            // 3. Reallocate the value of the variable
            var assignmentValue = CastValue(expression, type);
            LLVMApi.BuildStore(_builder, assignmentValue, variable);
        }

        private bool WriteScope(List<IAst> scopeChildren, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(localVariables);

            // 2. Write function lines
            foreach (var ast in scopeChildren)
            {
                if (WriteFunctionLine(ast, scopeVariables, function))
                {
                    return true;
                }
            }
            return false;
        }

        private bool WriteConditional(ConditionalAst conditional, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            // 1. Write out the condition
            var condition = BuildConditionExpression(conditional.Condition, localVariables);

            // 2. Write out the condition jump and blocks
            var thenBlock = LLVMApi.AppendBasicBlock(function, "then");
            var elseBlock = LLVMApi.AppendBasicBlock(function, "else");
            var endBlock = new LLVMBasicBlockRef();
            LLVMApi.BuildCondBr(_builder, condition, thenBlock, elseBlock);

            // 3. Write out if body
            LLVMApi.PositionBuilderAtEnd(_builder, thenBlock);
            var ifReturned = WriteScope(conditional.Children, localVariables, function);

            if (!ifReturned)
            {
                if (!conditional.Else.Any())
                {
                    LLVMApi.BuildBr(_builder, elseBlock);
                    LLVMApi.PositionBuilderAtEnd(_builder, elseBlock);
                    return false;
                }
                endBlock = LLVMApi.AppendBasicBlock(function, "ifcont");
                LLVMApi.BuildBr(_builder, endBlock);
            }

            LLVMApi.PositionBuilderAtEnd(_builder, elseBlock);

            if (!conditional.Else.Any())
            {
                return false;
            }

            // 4. Write out the else if necessary
            var elseReturned = WriteScope(conditional.Else, localVariables, function);

            // 5. Return if both branches return
            if (ifReturned && elseReturned)
            {
                return true;
            }

            // 6. Jump to end block if necessary and position builder at end block
            if (ifReturned)
            {
                endBlock = LLVMApi.AppendBasicBlock(function, "ifcont");
            }
            if (!elseReturned)
            {
                LLVMApi.BuildBr(_builder, endBlock);
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
            var condition = BuildConditionExpression(whileAst.Condition, localVariables);
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

        private LLVMValueRef BuildConditionExpression(IAst expression, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            var (type, conditionExpression) = WriteExpression(expression, localVariables);
            return type.PrimitiveType switch
            {
                IntegerType => LLVMApi.BuildICmp(_builder, LLVMIntPredicate.LLVMIntNE,
                    conditionExpression, LLVMApi.ConstInt(conditionExpression.TypeOf(), 0, false), "ifcond"),
                FloatType => LLVMApi.BuildFCmp(_builder, LLVMRealPredicate.LLVMRealONE,
                    conditionExpression, LLVMApi.ConstReal(conditionExpression.TypeOf(), 0), "ifcond"),
                _ when type.Name == "*" => LLVMApi.BuildIsNotNull(_builder, conditionExpression, "whilecond"),
                _ => conditionExpression
            };
        }

        private bool WriteEach(EachAst each, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            var eachVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(localVariables);

            // 1. Initialize each values
            var indexVariable = _allocationQueue.Dequeue();
            var listData = new LLVMValueRef();
            var compareTarget = new LLVMValueRef();
            TypeDefinition iterationType = null;
            var iterationValue = new LLVMValueRef();
            switch (each.Iteration)
            {
                case VariableAst var:
                    (iterationType, iterationValue) = localVariables[var.Name];
                    break;
                case StructFieldRefAst structField:
                    (iterationType, iterationValue) = BuildStructField(structField, localVariables[structField.Name].value);
                    break;
                case IndexAst index:
                    (iterationType, iterationValue) = GetListPointer(index, localVariables);
                    break;
                case null:
                    break;
                default:
                    var (type, value) = WriteExpression(each.Iteration, localVariables);
                    iterationType = type;
                    iterationValue = _allocationQueue.Dequeue();
                    LLVMApi.BuildStore(_builder, value, iterationValue);
                    break;
            }

            // 2. Initialize the first variable in the loop and the compare target
            if (each.Iteration != null)
            {
                LLVMApi.BuildStore(_builder, GetConstZero(LLVMTypeRef.Int32Type()), indexVariable);

                switch (iterationType!.Name)
                {
                    case "List":
                    case "Params":
                        // Load the List data
                        var dataPointer = LLVMApi.BuildStructGEP(_builder, iterationValue, 1, "dataptr");
                        listData = LLVMApi.BuildLoad(_builder, dataPointer, "data");

                        // Set the compareTarget to the list count
                        var lengthPointer= LLVMApi.BuildStructGEP(_builder, iterationValue, 0, "lengthptr");
                        compareTarget = LLVMApi.BuildLoad(_builder, lengthPointer, "length");
                        break;
                }
            }
            else
            {
                // Begin the loop at the beginning of the range
                var (type, value) = WriteExpression(each.RangeBegin, localVariables);
                LLVMApi.BuildStore(_builder, value, indexVariable);
                eachVariables.Add(each.IterationVariable, (type, indexVariable));

                // Get the end of the range
                (_, compareTarget) = WriteExpression(each.RangeEnd, localVariables);
            }

            // 3. Break to the each condition loop
            var eachCondition = LLVMApi.AppendBasicBlock(function, "eachcond");
            LLVMApi.BuildBr(_builder, eachCondition);

            // 4. Check condition of each loop and break if condition is not met
            LLVMApi.PositionBuilderAtEnd(_builder, eachCondition);
            var indexValue = LLVMApi.BuildLoad(_builder, indexVariable, "curr");
            var condition = LLVMApi.BuildICmp(_builder, each.Iteration == null ? LLVMIntPredicate.LLVMIntSLE : LLVMIntPredicate.LLVMIntSLT,
                indexValue, compareTarget, "listcmp");
            if (each.Iteration != null)
            {
                switch (iterationType!.Name)
                {
                    case "List":
                    case "Params":
                        var iterationVariable = LLVMApi.BuildGEP(_builder, listData, new []{indexValue}, each.IterationVariable);
                        eachVariables.TryAdd(each.IterationVariable, (each.IteratorType, iterationVariable));
                        break;
                }
            }

            var eachBody = LLVMApi.AppendBasicBlock(function, "eachbody");
            var afterEach = LLVMApi.AppendBasicBlock(function, "aftereach");
            LLVMApi.BuildCondBr(_builder, condition, eachBody, afterEach);

            // 5. Write out each loop body
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

            // 6. Increment and/or move the iteration variable
            var nextValue = LLVMApi.BuildAdd(_builder, indexValue, LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), 1, false), "inc");
            LLVMApi.BuildStore(_builder, nextValue, indexVariable);

            // 7. Write jump to the loop
            LLVMApi.BuildBr(_builder, eachCondition);

            // 8. Position builder to after block
            LLVMApi.PositionBuilderAtEnd(_builder, afterEach);
            return false;
        }

        private (TypeDefinition type, LLVMValueRef value) WriteExpression(IAst ast,
            IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            switch (ast)
            {
                case ConstantAst constant:
                {
                    var type = ConvertTypeDefinition(constant.Type);
                    return (constant.Type, BuildConstant(type, constant));
                }
                case NullAst nullAst:
                {
                    var type = ConvertTypeDefinition(nullAst.TargetType);
                    return (nullAst.TargetType, LLVMApi.ConstNull(type));
                }
                case VariableAst variable:
                {
                    var (type, value) = localVariables[variable.Name];
                    return (type, LLVMApi.BuildLoad(_builder, value, variable.Name));
                }
                case StructFieldRefAst structField:
                {
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)_types[structField.Name];
                        var value = enumDef.Values[structField.ValueIndex].Value;
                        var typeDef = new TypeDefinition {Name = "int", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};
                        return (typeDef, LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)value, false));
                    }
                    var (type, field) = BuildStructField(structField, localVariables[structField.Name].value);
                    return (type, LLVMApi.BuildLoad(_builder, field, structField.Name));
                }
                case CallAst call:
                    var function = LLVMApi.GetNamedFunction(_module, call.Function);
                    var functionDef = _functions[call.Function];

                    if (functionDef.Params)
                    {
                        var callArguments = new LLVMValueRef[functionDef.Arguments.Count];
                        for (var i = 0; i < functionDef.Arguments.Count - 1; i++)
                        {
                            var value = WriteExpression(call.Arguments[i], localVariables);
                            callArguments[i] = value.value;
                        }

                        // Rollup the rest of the arguments into a list
                        var paramsType = functionDef.Arguments[^1].Type.Generics[0];
                        var paramsPointer = _allocationQueue.Dequeue();
                        InitializeConstList(paramsPointer, call.Arguments.Count - functionDef.Arguments.Count + 1, paramsType);

                        var listData = LLVMApi.BuildStructGEP(_builder, paramsPointer, 1, "listdata");
                        var dataPointer = LLVMApi.BuildLoad(_builder, listData, "dataptr");

                        uint paramsIndex = 0;
                        for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                        {
                            var pointer = LLVMApi.BuildGEP(_builder, dataPointer, new [] {LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), paramsIndex, false)}, "indexptr");
                            var (_, value) = WriteExpression(call.Arguments[i], localVariables);
                            LLVMApi.BuildStore(_builder, value, pointer);
                        }

                        var paramsValue = LLVMApi.BuildLoad(_builder, paramsPointer, "params");
                        callArguments[functionDef.Arguments.Count - 1] = paramsValue;
                        return (functionDef.ReturnType, LLVMApi.BuildCall(_builder, function, callArguments, string.Empty));
                    }
                    else if (functionDef.Varargs)
                    {
                        var callArguments = new LLVMValueRef[call.Arguments.Count];
                        for (var i = 0; i < functionDef.Arguments.Count - 1; i++)
                        {
                            var (_, value) = WriteExpression(call.Arguments[i], localVariables);
                            callArguments[i] = value;
                        }

                        // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                        // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                        for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++)
                        {
                            var (type, value) = WriteExpression(call.Arguments[i], localVariables);
                            if (type.Name == "float")
                            {
                                value = LLVMApi.BuildFPExt(_builder, value, LLVMTypeRef.DoubleType(), "tmpdouble");
                            }
                            callArguments[i] = value;
                        }
                        return (functionDef.ReturnType, LLVMApi.BuildCall(_builder, function, callArguments, string.Empty));
                    }
                    else
                    {
                        var callArguments = new LLVMValueRef[call.Arguments.Count];
                        for (var i = 0; i < call.Arguments.Count; i++)
                        {
                            var (_, value) = WriteExpression(call.Arguments[i], localVariables);
                            callArguments[i] = value;
                        }
                        return (functionDef.ReturnType, LLVMApi.BuildCall(_builder, function, callArguments, string.Empty));
                    }
                case ChangeByOneAst changeByOne:
                {
                    var (variableType, pointer) = changeByOne.Variable switch
                    {
                        VariableAst variableAst => localVariables[variableAst.Name],
                        StructFieldRefAst structField => BuildStructField(structField, localVariables[structField.Name].value),
                        IndexAst index => GetListPointer(index, localVariables),
                        // @Cleanup This branch should never be hit
                        _ => (null, new LLVMValueRef())
                    };

                    var value = LLVMApi.BuildLoad(_builder, pointer, "tmpvalue");
                    var type = ConvertTypeDefinition(variableType);

                    LLVMValueRef newValue;
                    if (variableType.PrimitiveType is IntegerType)
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

                    LLVMApi.BuildStore(_builder, newValue, pointer);
                    return changeByOne.Prefix ? (variableType, newValue) : (variableType, value);
                }
                case UnaryAst unary:
                {
                    if (unary.Operator == UnaryOperator.Reference)
                    {
                        var (valueType, pointer) = unary.Value switch
                        {
                            VariableAst variable => localVariables[variable.Name],
                            StructFieldRefAst structField => BuildStructField(structField, localVariables[structField.Name].value),
                            IndexAst index => GetListPointer(index, localVariables),
                            // @Cleanup this branch should not be hit
                            _ => (null, new LLVMValueRef())
                        };
                        var pointerType = new TypeDefinition {Name = "*"};
                        pointerType.Generics.Add(valueType);
                        return (pointerType, pointer);
                    }

                    var (type, value) = WriteExpression(unary.Value, localVariables);
                    return unary.Operator switch
                    {
                        UnaryOperator.Not => (type, LLVMApi.BuildNot(_builder, value, "not")),
                        UnaryOperator.Negate => type.PrimitiveType switch
                        {
                            IntegerType => (type, LLVMApi.BuildNeg(_builder, value, "neg")),
                            FloatType => (type, LLVMApi.BuildFNeg(_builder, value, "fneg")),
                            // @Cleanup This branch should not be hit
                            _ => (null, new LLVMValueRef())
                        },
                        UnaryOperator.Dereference => (type.Generics[0], LLVMApi.BuildLoad(_builder, value, "tmpderef")),
                        // @Cleanup This branch should not be hit
                        _ => (null, new LLVMValueRef())
                    };
                }
                case IndexAst index:
                {
                    var (elementType, elementValue) = GetListPointer(index, localVariables);
                    return (elementType, LLVMApi.BuildLoad(_builder, elementValue, "tmpindex"));
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
                    Console.WriteLine($"Unexpected syntax tree");
                    Environment.Exit(ErrorCodes.BuildError);
                    return (null, new LLVMValueRef()); // Return never happens
            }
        }

        private readonly TypeDefinition _stackPointerType = new()
        {
            Name = "*", Generics = {new TypeDefinition {Name = "u8", PrimitiveType = new IntegerType {Bytes = 1}}}
        };

        private void BuildStackPointer()
        {
            if (_stackPointerExists) return;

            _stackPointer = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(_stackPointerType), "stackPtr");
            _stackPointerExists = true;
        }

        private void BuildStackSave()
        {
            if (_stackSaved) return;

            var function = LLVMApi.GetNamedFunction(_module, "llvm.stacksave");
            if (function.Pointer == IntPtr.Zero)
            {
                function = WriteFunctionDefinition("llvm.stacksave", new List<Argument>(), _stackPointerType);
            }

            var stackPointer = LLVMApi.BuildCall(_builder, function, Array.Empty<LLVMValueRef>(), "stackPointer");
            LLVMApi.BuildStore(_builder, stackPointer, _stackPointer);
            _stackSaved = true;
        }

        private void BuildStackRestore()
        {
            if (!_stackSaved) return;

            var function = LLVMApi.GetNamedFunction(_module, "llvm.stackrestore");
            if (function.Pointer == IntPtr.Zero)
            {
                function = WriteFunctionDefinition("llvm.stackrestore", new List<Argument> { new()
                {
                    Name = "ptr", Type = _stackPointerType
                }}, new TypeDefinition {Name = "void"});
            }

            var stackPointer = LLVMApi.BuildLoad(_builder, _stackPointer, "stackPointer");
            LLVMApi.BuildCall(_builder, function, new []{stackPointer}, "");
        }

        private LLVMValueRef BuildConstant(LLVMTypeRef type, ConstantAst constant)
        {
            switch (constant.Type.PrimitiveType)
            {
                case IntegerType:
                    return LLVMApi.ConstInt(type, (ulong)long.Parse(constant.Value), false);
                case FloatType:
                    return LLVMApi.ConstRealOfStringAndSize(type, constant.Value, (uint)constant.Value.Length);
            }

            switch (constant.Type.Name)
            {
                case "bool":
                    return LLVMApi.ConstInt(type, constant.Value == "true" ? 1 : 0, false);
                case "string":
                    return LLVMApi.BuildGlobalStringPtr(_builder, constant.Value, "str");
                default:
                    return LLVMApi.ConstInt(LLVMApi.Int32Type(), 0, true);
            }
        }

        private (TypeDefinition type, LLVMValueRef value) BuildStructField(StructFieldRefAst structField, LLVMValueRef variable)
        {
            if (structField.IsPointer)
            {
                variable = LLVMApi.BuildLoad(_builder, variable, "pointerval");
            }
            var value = structField.Value;
            var field = LLVMApi.BuildStructGEP(_builder, variable, (uint)structField.ValueIndex, value.Name);

            if (value.Value == null)
            {
                var type = ((StructAst)_types[structField.StructName]).Fields[structField.ValueIndex].Type;
                return (type, field);
            }

            return BuildStructField(value, field);
        }

        private (TypeDefinition type, LLVMValueRef value) GetListPointer(IndexAst index,
            IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Get the variable pointer
            var (type, variable) = index.Variable switch
            {
                VariableAst var => localVariables[var.Name],
                StructFieldRefAst structField => BuildStructField(structField, localVariables[structField.Name].value),
                // @Cleanup This branch should never be hit
                _ => (null, new LLVMValueRef())
            };

            // 2. Determine the index
            var (_, indexValue) = WriteExpression(index.Index, localVariables);

            // 3. Build the pointer with the first index of 0
            var elementType = type.Generics[0];
            var listData = LLVMApi.BuildStructGEP(_builder, variable, 1, "listdata");
            var dataPointer = LLVMApi.BuildLoad(_builder, listData, "dataptr");
            return (elementType, LLVMApi.BuildGEP(_builder, dataPointer, new [] {indexValue}, "indexptr"));
        }

        private LLVMValueRef BuildExpression((TypeDefinition type, LLVMValueRef value) lhs,
            (TypeDefinition type, LLVMValueRef value) rhs, Operator op, TypeDefinition targetType)
        {
            // 1. Handle pointer math 
            if (lhs.type.Name == "*")
            {
                return BuildPointerOperation(lhs.value, rhs.value, op);
            }
            if (rhs.type.Name == "*")
            {
                return BuildPointerOperation(rhs.value, lhs.value, op);
            }

            // 2. Handle simple operators like && and ||
            switch (op)
            {
                case Operator.And:
                    return LLVMApi.BuildAnd(_builder, lhs.value, rhs.value, "tmpand");
                case Operator.Or:
                    return LLVMApi.BuildOr(_builder, lhs.value, rhs.value, "tmpor");
            }

            // 3. Handle compares, since the lhs and rhs should not be cast to the target type 
            switch (op)
            {
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return BuildCompare(lhs, rhs, op);
            }

            // 4. Cast lhs and rhs to the target types
            lhs.value = CastValue(lhs, targetType);
            rhs.value = CastValue(rhs, targetType);

            // 5. Handle the rest of the simple operators
            switch (op)
            {
                case Operator.BitwiseAnd:
                    return LLVMApi.BuildAnd(_builder, lhs.value, rhs.value, "tmpband");
                case Operator.BitwiseOr:
                    return LLVMApi.BuildOr(_builder, lhs.value, rhs.value, "tmpbor");
                case Operator.Xor:
                    return LLVMApi.BuildXor(_builder, lhs.value, rhs.value, "tmpxor");
            }

            // 6. Handle binary operations
            var signed = lhs.type.PrimitiveType.Signed || rhs.type.PrimitiveType.Signed;
            return BuildBinaryOperation(targetType, lhs.value, rhs.value, op, signed);
        }

        private LLVMValueRef BuildPointerOperation(LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            if (op == Operator.Equality)
            {
                if (rhs.IsNull())
                {
                    return LLVMApi.BuildIsNull(_builder, lhs, "isnull");
                }
                var diff = LLVMApi.BuildPtrDiff(_builder, lhs, rhs, "ptrdiff");
                return LLVMApi.BuildICmp(_builder, LLVMIntPredicate.LLVMIntEQ, diff, LLVMApi.ConstInt(diff.TypeOf(), 0, false), "ptreq");
            }
            if (op == Operator.NotEqual)
            {
                if (rhs.IsNull())
                {
                    return LLVMApi.BuildIsNotNull(_builder, lhs, "notnull");
                }
                var diff = LLVMApi.BuildPtrDiff(_builder, lhs, rhs, "ptrdiff");
                return LLVMApi.BuildICmp(_builder, LLVMIntPredicate.LLVMIntNE, diff, LLVMApi.ConstInt(diff.TypeOf(), 0, false), "ptreq");
            }
            if (op == Operator.Subtract)
            {
                rhs = LLVMApi.BuildNeg(_builder, rhs, "tmpneg");
            }
            return LLVMApi.BuildGEP(_builder, lhs, new []{rhs}, "tmpptr");
        }

        private LLVMValueRef BuildCompare((TypeDefinition type, LLVMValueRef value) lhs,
            (TypeDefinition type, LLVMValueRef value) rhs, Operator op)
        {
            switch (lhs.type.PrimitiveType)
            {
                case IntegerType lhsInt:
                    switch (rhs.type.PrimitiveType)
                    {
                        case IntegerType rhsInt:
                        {
                            var signed = lhsInt.Signed || rhsInt.Signed;
                            if (lhsInt.Bytes > rhsInt.Bytes)
                            {
                                var type = ConvertTypeDefinition(lhs.type);
                                rhs.value = signed ? LLVMApi.BuildSExt(_builder, rhs.value, type, "tmpint") :
                                    LLVMApi.BuildZExt(_builder, rhs.value, type, "tmpint");
                            }
                            else if (lhsInt.Bytes < rhsInt.Bytes)
                            {
                                var type = ConvertTypeDefinition(rhs.type);
                                lhs.value = signed ? LLVMApi.BuildSExt(_builder, lhs.value, type, "tmpint") :
                                    LLVMApi.BuildZExt(_builder, lhs.value, type, "tmpint");
                            }
                            var (predicate, name) = ConvertIntOperator(op, signed);
                            return LLVMApi.BuildICmp(_builder, predicate, lhs.value, rhs.value, name);
                        }
                        case FloatType:
                        {
                            var lhsValue = CastValue(lhs, rhs.type, false);
                            var (predicate, name) = ConvertRealOperator(op);
                            return LLVMApi.BuildFCmp(_builder, predicate, lhsValue, rhs.value, name);
                        }
                    }
                    break;
                case FloatType lhsFloat:
                    switch (rhs.type.PrimitiveType)
                    {
                        case IntegerType:
                        {
                            var rhsValue = CastValue(rhs, lhs.type, false);
                            var (predicate, name) = ConvertRealOperator(op);
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs.value, rhsValue, name);
                        }
                        case FloatType rhsFloat:
                        {
                            if (lhsFloat.Bytes > rhsFloat.Bytes)
                            {
                                rhs.value = LLVMApi.BuildFPCast(_builder, rhs.value, LLVMTypeRef.DoubleType(), "tmpfp");
                            }
                            else if (lhsFloat.Bytes < rhsFloat.Bytes)
                            {
                                lhs.value = LLVMApi.BuildFPCast(_builder, lhs.value, LLVMTypeRef.DoubleType(), "tmpfp");
                            }
                            var (predicate, name) = ConvertRealOperator(op);
                            return LLVMApi.BuildFCmp(_builder, predicate, lhs.value, rhs.value, name);
                        }
                    }
                    break;
                case EnumType:
                {
                    var (predicate, name) = ConvertIntOperator(op);
                    return LLVMApi.BuildICmp(_builder, predicate, lhs.value, rhs.value, name);
                }
            }

            // @Future Operator overloading
            throw new NotImplementedException($"{op} not compatible with types '{lhs.value.TypeOf().TypeKind}' and '{rhs.value.TypeOf().TypeKind}'");
        }

        private LLVMValueRef BuildBinaryOperation(TypeDefinition type, LLVMValueRef lhs, LLVMValueRef rhs, Operator op, bool signed = true)
        {
            switch (type.PrimitiveType)
            {
                case IntegerType:
                    return BuildIntOperation(lhs, rhs, op, signed);
                case FloatType:
                    return BuildRealOperation(lhs, rhs, op);
            }

            // @Future Operator overloading
            throw new NotImplementedException($"{op} not compatible with types '{lhs.TypeOf().TypeKind}' and '{rhs.TypeOf().TypeKind}'");
        }

        private LLVMValueRef CastValue((TypeDefinition type, LLVMValueRef value) typeValue, TypeDefinition targetType,
            bool checkType = true)
        {
            var (type, value) = typeValue;

            if (checkType && TypeEquals(type, targetType)) return value;

            var target = ConvertTypeDefinition(targetType);
            switch (type.PrimitiveType)
            {
                case IntegerType intType:
                    switch (targetType.PrimitiveType)
                    {
                        case IntegerType intTarget:
                            if (intTarget.Bytes >= intType.Bytes)
                            {
                                return intTarget.Signed ? LLVMApi.BuildSExtOrBitCast(_builder, value, target, "tmpint") :
                                    LLVMApi.BuildZExtOrBitCast(_builder, value, target, "tmpint");
                            }
                            else
                            {
                                return LLVMApi.BuildTrunc(_builder, value, target, "tmpint");
                            }
                        case FloatType:
                            return intType.Signed ? LLVMApi.BuildSIToFP(_builder, value, target, "tmpfp") :
                                LLVMApi.BuildUIToFP(_builder, value, target, "tmpfp");
                    }
                    break;
                case FloatType:
                    switch (targetType.PrimitiveType)
                    {
                        case IntegerType intTarget:
                            return intTarget.Signed ? LLVMApi.BuildFPToSI(_builder, value, target, "tmpfp") :
                                LLVMApi.BuildFPToUI(_builder, value, target, "tmpfp");
                        case FloatType:
                            return LLVMApi.BuildFPCast(_builder, value, target, "tmpfp");
                    }
                    break;
            }

            // @Future Polymorphic type casting
            return value;
        }

        private static bool TypeEquals(TypeDefinition a, TypeDefinition b)
        {
            // Check by primitive type
            switch (a.PrimitiveType)
            {
                case IntegerType aInt:
                    if (b.PrimitiveType is IntegerType bInt)
                    {
                        return aInt.Bytes == bInt.Bytes && aInt.Signed == bInt.Signed;
                    }
                    return false;
                case FloatType aFloat:
                    if (b.PrimitiveType is FloatType bFloat)
                    {
                        return aFloat.Bytes == bFloat.Bytes && aFloat.Signed == bFloat.Signed;
                    }
                    return false;
                default:
                    if (b.PrimitiveType != null) return false;
                    break;
            }

            // Check by name
            if (a.Name != b.Name) return false;
            if (a.Generics.Count != b.Generics.Count) return false;
            for (var i = 0; i < a.Generics.Count; i++)
            {
                var ai = a.Generics[i];
                var bi = b.Generics[i];
                if (!TypeEquals(ai, bi)) return false;
            }
            return true;
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

        private LLVMTypeRef ConvertTypeDefinition(TypeDefinition type, bool pointer = false)
        {
            if (type.Name == "*")
            {
                return LLVMTypeRef.PointerType(ConvertTypeDefinition(type.Generics[0], true), 0);
            }

            return type.PrimitiveType switch
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
                _ => type.Name switch
                {
                    "bool" => LLVMTypeRef.Int1Type(),
                    "void" => pointer ? LLVMTypeRef.Int8Type() : LLVMTypeRef.VoidType(),
                    "List" => GetListType(type),
                    "Params" => GetListType(type),
                    "string" => LLVMTypeRef.PointerType(LLVMTypeRef.Int8Type(), 0),
                    _ => GetStructType(type)
                }
            };
        }

        private LLVMTypeRef GetListType(TypeDefinition type)
        {
            var listType = type.Generics[0];
            return LLVMApi.GetTypeByName(_module, $"List.{listType.GenericName}");
        }

        private LLVMTypeRef GetStructType(TypeDefinition type)
        {
            if (_types.TryGetValue(type.Name, out var typeDef) && typeDef is EnumAst)
            {
                return LLVMTypeRef.Int32Type();
            }

            return LLVMApi.GetTypeByName(_module, type.GenericName);
        }
    }
}
