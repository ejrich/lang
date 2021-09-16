using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LLVMSharp.Interop;

namespace Lang.Backend
{
    public unsafe class LLVMBackend : IBackend
    {
        private const string ObjectDirectory = "obj";

        private ProgramGraph _programGraph;
        private LLVMModuleRef _module;
        private LLVMContextRef _context;
        private LLVMBuilderRef _builder;
        private LLVMPassManagerRef _passManager;
        private IFunction _currentFunction;
        private LLVMValueRef _stackPointer;
        private bool _stackPointerExists;
        private bool _stackSaved;
        private LLVMTypeRef _stringType;
        private LLVMTypeRef _u8PointerType;

        private bool _emitDebug;
        private LLVMDIBuilderRef _debugBuilder;
        private LLVMMetadataRef _debugCompilationUnit;
        private List<LLVMMetadataRef> _debugFiles;

        private readonly Queue<LLVMValueRef> _allocationQueue = new();
        private readonly LLVMValueRef _zeroInt = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), 0, false);
        private readonly TypeDefinition _intTypeDefinition = new() {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};

        public string Build(ProjectFile project, ProgramGraph programGraph, BuildSettings buildSettings)
        {
            _programGraph = programGraph;

            // 1. Verify obj directory exists
            var objectPath = Path.Combine(project.Path, ObjectDirectory);
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);

            // 2. Initialize the LLVM module and builder
            InitLLVM(project, buildSettings.Release, objectPath);

            // 3. Write Data section
            var globals = WriteData();

            // 4. Write Function and Operator overload definitions
            foreach (var (name, functions) in _programGraph.Functions)
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    if (function.Compiler || function.CallsCompiler) continue;

                    var functionName = name switch
                    {
                        "main" => "__main",
                        "__start" => "main",
                        _ => GetFunctionName(name, i, functions.Count)
                    };
                    WriteFunctionDefinition(functionName, function, name, function.Varargs, function.Extern);
                }
            }
            foreach (var (type, overloads) in _programGraph.OperatorOverloads)
            {
                foreach (var (op, overload) in overloads)
                {
                    var overloadName = GetOperatorOverloadName(type, op);
                    WriteFunctionDefinition(overloadName, overload, overloadName);
                }
            }

            // 5. Write Function and Operator overload bodies
            foreach (var (name, functions) in _programGraph.Functions)
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var functionAst = functions[i];
                    if (functionAst.Extern || functionAst.Compiler || functionAst.CallsCompiler) continue;

                    var functionName = name switch
                    {
                        "main" => "__main",
                        "__start" => "main",
                        _ => GetFunctionName(name, i, functions.Count)
                    };
                    var function = _module.GetNamedFunction(functionName);
                    var argumentCount = functionAst.Varargs ? functionAst.Arguments.Count - 1 : functionAst.Arguments.Count;
                    WriteFunction(functionAst, argumentCount, globals, function);
                }
            }
            foreach (var (type, overloads) in _programGraph.OperatorOverloads)
            {
                foreach (var (op, overload) in overloads)
                {
                    var overloadName = GetOperatorOverloadName(type, op);
                    var function = _module.GetNamedFunction(overloadName);
                    WriteFunction(overload, 2, globals, function);
                }
            }

            // 6. Compile to object file
            var objectFile = Path.Combine(objectPath, $"{project.Name}.o");
            Compile(objectFile, buildSettings.OutputAssembly);

            return objectFile;
        }

        private void InitLLVM(ProjectFile project, bool optimize, string objectPath)
        {
            _module = LLVMModuleRef.CreateWithName(project.Name);
            _context = _module.Context;
            _builder = LLVMBuilderRef.Create(_context);
            _passManager = _module.CreateFunctionPassManager();
            if (optimize)
            {
                _passManager.AddBasicAliasAnalysisPass();
                _passManager.AddPromoteMemoryToRegisterPass();
                _passManager.AddInstructionCombiningPass();
                _passManager.AddReassociatePass();
                _passManager.AddGVNPass();
                _passManager.AddCFGSimplificationPass();

                _passManager.InitializeFunctionPassManager();
            }
            else
            {
                _emitDebug = true;
                _debugBuilder = _module.CreateDIBuilder();
                var compileUnitFile = _debugBuilder.CreateFile($"{project.Name}.ll", objectPath);
                _debugCompilationUnit = _debugBuilder.CreateCompileUnit(LLVMDWARFSourceLanguage.LLVMDWARFSourceLanguageC, compileUnitFile, "ol", 0, string.Empty, 0, string.Empty, LLVMDWARFEmissionKind.LLVMDWARFEmissionNone, 0, 0, 0, string.Empty, string.Empty);
                _debugFiles = project.SourceFiles.Select(file => _debugBuilder.CreateFile(Path.GetFileName(file), Path.GetDirectoryName(file))).ToList();
            }
        }

        private IDictionary<string, (TypeDefinition type, LLVMValueRef value)> WriteData()
        {
            // 1. Declare structs and enums
            var structs = new Dictionary<string, LLVMTypeRef>();
            foreach (var (name, type) in _programGraph.Types)
            {
                if (type is StructAst structAst)
                {
                    structs[name] = _context.CreateNamedStruct(name);
                }
            }
            foreach (var (name, type) in _programGraph.Types)
            {
                if (type is StructAst structAst && structAst.Fields.Any())
                {
                    var fields = structAst.Fields.Select(field => ConvertTypeDefinition(field.Type)).ToArray();
                    structs[name].StructSetBody(fields, false);
                }
            }
            _stringType = _module.GetTypeByName("string");
            _u8PointerType = LLVM.PointerType(LLVM.Int8Type(), 0);

            // 2. Declare variables
            var globals = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>();
            foreach (var globalVariable in _programGraph.Variables)
            {
                if (globalVariable.Constant && globalVariable.Type.TypeKind != TypeKind.String)
                {
                    var (_, constant) = WriteExpression(globalVariable.Value, null);
                    globals.Add(globalVariable.Name, (globalVariable.Type, constant));
                }
                else
                {
                    var typeDef = globalVariable.Type;
                    var type = ConvertTypeDefinition(typeDef);
                    var global = _module.AddGlobal(type, globalVariable.Name);
                    LLVM.SetLinkage(global, LLVMLinkage.LLVMPrivateLinkage);
                    if (globalVariable.Value != null)
                    {
                        LLVM.SetInitializer(global, WriteExpression(globalVariable.Value, null).value);
                    }
                    else if (typeDef.TypeKind == TypeKind.Integer || typeDef.TypeKind == TypeKind.Float)
                    {
                        LLVM.SetInitializer(global, GetConstZero(type));
                    }
                    globals.Add(globalVariable.Name, (globalVariable.Type, global));
                }
            }

            // 3. Write type table
            var typeTable = globals["__type_table"].value;
            SetPrivateConstant(typeTable);
            var typeInfoType = structs["TypeInfo"];

            var types = new LLVMValueRef[_programGraph.TypeCount];
            var typePointers = new Dictionary<string, (IType type, LLVMValueRef typeInfo)>();
            foreach (var (name, type) in _programGraph.Types)
            {
                var typeInfo = _module.AddGlobal(typeInfoType, "____type_info");
                SetPrivateConstant(typeInfo);
                types[type.TypeIndex] = typeInfo;
                typePointers[name] = (type, typeInfo);
            }
            foreach (var (name, functions) in _programGraph.Functions)
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    var typeInfo = _module.AddGlobal(typeInfoType, "____type_info");
                    SetPrivateConstant(typeInfo);
                    types[function.TypeIndex] = typeInfo;
                    var functionName = GetFunctionName(name, i, functions.Count);
                    typePointers[functionName] = (function, typeInfo);
                }
            }

            var typeFieldType = _module.GetTypeByName("TypeField");
            var enumValueType = _module.GetTypeByName("EnumValue");
            var argumentType = _module.GetTypeByName("ArgumentType");
            foreach (var (_, (type, typeInfo)) in typePointers)
            {
                var typeNameString = BuildString(type.Name);

                var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)type.TypeKind, 0);
                var typeSize = LLVM.ConstInt(LLVM.Int32Type(), type.Size, 0);

                LLVMValueRef fields;
                if (type is StructAst structAst)
                {
                    var typeFields = new LLVMValueRef[structAst.Fields.Count];

                    for (var i = 0; i < structAst.Fields.Count; i++)
                    {
                        var field = structAst.Fields[i];

                        var fieldNameString = BuildString(field.Name);
                        var fieldOffset = LLVM.ConstInt(LLVM.Int32Type(), field.Offset, 0);

                        var typeField = LLVMValueRef.CreateConstStruct(new LLVMValueRef[] {fieldNameString, fieldOffset, typePointers[field.Type.GenericName].typeInfo}, false);

                        typeFields[i] = typeField;
                    }

                    var typeFieldArray = LLVMValueRef.CreateConstArray(typeInfoType, typeFields);
                    var typeFieldArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typeFieldArray), "____type_fields");
                    SetPrivateConstant(typeFieldArrayGlobal);
                    LLVM.SetInitializer(typeFieldArrayGlobal, typeFieldArray);

                    fields = LLVMValueRef.CreateConstStruct(new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)structAst.Fields.Count, 0),
                        typeFieldArrayGlobal
                    }, false);
                }
                else
                {
                    fields = LLVMValueRef.CreateConstStruct(new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(typeFieldType, 0))}, false);
                }

                LLVMValueRef enumValues;
                if (type is EnumAst enumAst)
                {
                    var enumValueRefs = new LLVMValueRef[enumAst.Values.Count];

                    for (var i = 0; i < enumAst.Values.Count; i++)
                    {
                        var value = enumAst.Values[i];

                        var enumValueNameString = BuildString(value.Name);
                        var enumValue = LLVM.ConstInt(LLVM.Int32Type(), (uint)value.Value, 0);

                        enumValueRefs[i] = LLVMValueRef.CreateConstStruct(new LLVMValueRef[] {enumValueNameString, enumValue}, false);
                    }

                    var enumValuesArray = LLVMValueRef.CreateConstArray(typeInfoType, enumValueRefs);
                    var enumValuesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(enumValuesArray), "____enum_values");
                    SetPrivateConstant(enumValuesArrayGlobal);
                    LLVM.SetInitializer(enumValuesArrayGlobal, enumValuesArray);

                    enumValues = LLVMValueRef.CreateConstStruct(new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)enumAst.Values.Count, 0),
                        enumValuesArrayGlobal
                    }, false);
                }
                else
                {
                    enumValues = LLVMValueRef.CreateConstStruct(new LLVMValueRef[] {_zeroInt, LLVM.ConstNull(LLVM.PointerType(enumValueType, 0))}, false);
                }

                LLVMValueRef returnType;
                LLVMValueRef arguments;
                if (type is FunctionAst function)
                {
                    returnType = typePointers[function.ReturnType.GenericName].typeInfo;

                    var argumentCount = function.Varargs ? function.Arguments.Count - 1 : function.Arguments.Count;
                    var argumentValues = new LLVMValueRef[argumentCount];
                    for (var i = 0; i < argumentCount; i++)
                    {
                        var argument = function.Arguments[i];

                        var argNameString = BuildString(argument.Name);
                        var argumentTypeInfo = argument.Type.Name switch
                        {
                            "Type" => typePointers["s32"].typeInfo,
                            "Params" => typePointers[$"List.{argument.Type.Generics[0].GenericName}"].typeInfo,
                            _ => typePointers[argument.Type.GenericName].typeInfo
                        };

                        var argumentValue = LLVMValueRef.CreateConstStruct(new LLVMValueRef[] {argNameString, argumentTypeInfo}, false);

                        argumentValues[i] = argumentValue;
                    }

                    var argumentArray = LLVMValueRef.CreateConstArray(typeInfoType, argumentValues);
                    var argumentArrayGlobal = _module.AddGlobal(LLVM.TypeOf(argumentArray), "____type_fields");
                    SetPrivateConstant(argumentArrayGlobal);
                    LLVM.SetInitializer(argumentArrayGlobal, argumentArray);

                    arguments = LLVMValueRef.CreateConstStruct(new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)function.Arguments.Count, 0),
                        argumentArrayGlobal
                    }, false);
                }
                else
                {
                    returnType = LLVM.ConstNull(LLVM.PointerType(typeFieldType, 0));
                    arguments = LLVMValueRef.CreateConstStruct(new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(argumentType, 0))}, false);
                }

                LLVM.SetInitializer(typeInfo, LLVMValueRef.CreateConstStruct(new LLVMValueRef[] {typeNameString, typeKind, typeSize, fields, enumValues, returnType, arguments}, false));
            }

            var typeArray = LLVMValueRef.CreateConstArray(LLVM.PointerType(typeInfoType, 0), types);
            var typeArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typeArray), "____type_array");
            SetPrivateConstant(typeArrayGlobal);
            LLVM.SetInitializer(typeArrayGlobal, typeArray);

            var typeCount = LLVM.ConstInt(LLVM.Int32Type(), (ulong)types.Length, 0);
            LLVM.SetInitializer(typeTable, LLVMValueRef.CreateConstStruct(new LLVMValueRef[] {typeCount, typeArrayGlobal}, false));

            return globals;
        }

        private void SetPrivateConstant(LLVMValueRef variable)
        {
            LLVM.SetLinkage(variable, LLVMLinkage.LLVMPrivateLinkage);
            LLVM.SetGlobalConstant(variable, 1);
            LLVM.SetUnnamedAddr(variable, 1);
        }

        private void WriteFunctionDefinition(string name, IFunction functionAst, string debugName, bool varargs = false, bool externFunction = false)
        {
            var argumentCount = varargs ? functionAst.Arguments.Count - 1 : functionAst.Arguments.Count;
            var argumentTypes = new LLVMTypeRef[argumentCount];

            // 1. Determine argument types and varargs

            // 3. Create debug function
            if (_emitDebug && !externFunction)
            {
                var debugArgumentTypes = new LLVMMetadataRef[argumentCount];

                for (var i = 0; i < argumentCount; i++)
                {
                    var argument = functionAst.Arguments[i];
                    argumentTypes[i] = ConvertTypeDefinition(argument.Type);
                    // debugArgumentTypes[i] = GetDebugType(argument.Type);
                }
                var file = _debugFiles[functionAst.FileIndex];

                // var functionType = LLVM.DIBuilderGetOrCreateTypeArray(_debugBuilder, );
                // var debugFunction = LLVM.DIBuilderCreateFunction(_debugBuilder, debugName, (uint)debugName.Length, "private", 7, file, functionAst.Line, functionType, false, false, functionAst.Line, 0, false);
                // TODO Implement me
            }
            else
            {
                for (var i = 0; i < argumentCount; i++)
                {
                    argumentTypes[i] = ConvertTypeDefinition(functionAst.Arguments[i].Type, externFunction);
                }
            }

            // 3. Declare function
            var function = _module.AddFunction(name, LLVMTypeRef.CreateFunction(ConvertTypeDefinition(functionAst.ReturnType), argumentTypes.ToArray(), varargs));
        }

        private void WriteFunction(IFunction functionAst, int argumentCount, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> globals, LLVMValueRef function)
        {
            _currentFunction = functionAst;
            // 1. Get function definition
            LLVM.PositionBuilderAtEnd(_builder, function.AppendBasicBlock("entry"));
            var localVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(globals);

            // 2. Allocate arguments on the stack
            for (var i = 0; i < argumentCount; i++)
            {
                var arg = functionAst.Arguments[i];
                var allocation = _builder.BuildAlloca(ConvertTypeDefinition(arg.Type), arg.Name);
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
                var argument = LLVM.GetParam(function, (uint) i);
                var variable = localVariables[arg.Name].value;
                LLVM.BuildStore(_builder, argument, variable);
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
                LLVM.BuildRetVoid(_builder);
            }
            _stackPointerExists = false;
            _stackSaved = false;

            // 7. Verify the function
            LLVM.RunFunctionPassManager(_passManager, function);
            #if DEBUG
            LLVM.VerifyFunction(function, LLVMVerifierFailureAction.LLVMPrintMessageAction);
            #endif
        }

        private void Compile(string objectFile, bool outputIntermediate)
        {
            LLVM.InitializeX86TargetInfo();
            LLVM.InitializeX86Target();
            LLVM.InitializeX86TargetMC();
            LLVM.InitializeX86AsmParser();
            LLVM.InitializeX86AsmPrinter();

            if (_emitDebug)
            {
                LLVM.DIBuilderFinalize(_debugBuilder);
            }

            var target = LLVMTargetRef.GetTargetFromTriple("x86-64");
            // TODO Figure this out
            // var targetTriple = Marshal.PtrToStringAnsi(LLVM.GetDefaultTargetTriple());
            // LLVM.SetTarget(_module, targetTriple);

            var targetMachine = target.CreateTargetMachine(LLVMTargetRef.DefaultTriple, "generic", "", LLVMCodeGenOptLevel.LLVMCodeGenLevelNone, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);
            // _module.DataLayout = targetMachine.CreateTargetDataLayout(); // TODO Figure this out

            if (outputIntermediate)
            {
                var llvmIrFile = objectFile[..^1] + "ll";
                _module.PrintToFile(llvmIrFile);

                var assemblyFile = objectFile[..^1] + "s";
                targetMachine.TryEmitToFile(_module, assemblyFile, LLVMCodeGenFileType.LLVMAssemblyFile, out _);
            }

            if (!targetMachine.TryEmitToFile(_module, objectFile, LLVMCodeGenFileType.LLVMObjectFile, out var errorMessage))
            {
                Console.WriteLine($"LLVM Build error: {errorMessage}");
                Environment.Exit(ErrorCodes.BuildError);
            }
        }

        private bool BuildAllocations(IAst ast)
        {
            switch (ast)
            {
                case ReturnAst returnAst:
                    BuildAllocations(returnAst.Value);
                    return true;
                case CallAst call:
                    BuildCallAllocations(call);
                    break;
                case DeclarationAst declaration:
                    if (declaration.Constant && declaration.Type.TypeKind != TypeKind.String) break;

                    var type = ConvertTypeDefinition(declaration.Type);
                    var variable = _builder.BuildAlloca(type, declaration.Name);
                    _allocationQueue.Enqueue(variable);

                    if (declaration.Value != null)
                    {
                        BuildAllocations(declaration.Value);
                    }
                    else if (declaration.Type.Name == "List" && !declaration.Type.CArray)
                    {
                        if (declaration.Type.ConstCount != null)
                        {
                            var listType = declaration.Type.Generics[0];
                            var targetType = ConvertTypeDefinition(listType);
                            var arrayType = LLVM.ArrayType(targetType, declaration.Type.ConstCount.Value);
                            var listData = _builder.BuildAlloca(arrayType, "listdata");
                            _allocationQueue.Enqueue(listData);
                        }
                        else if (declaration.Type.Count != null)
                        {
                            BuildStackPointer();
                        }
                    }
                    else if (LLVM.GetTypeKind(type) == LLVMTypeKind.LLVMStructTypeKind)
                    {
                        BuildStructAllocations(declaration.Type.GenericName, declaration.Assignments);
                    }
                    break;
                case AssignmentAst assignment:
                    BuildAllocations(assignment.Value);
                    break;
                case StructFieldRefAst structField:
                    for (var i = 0; i < structField.Children.Count - 1; i++)
                    {
                        switch (structField.Children[i])
                        {
                            // To get the field of a call, the value needs to be stored on the stack to use GetElementPtr
                            case CallAst call:
                                BuildCallAllocations(call);
                                if (!structField.Pointers[i])
                                {
                                    var function = _programGraph.Functions[call.Function][call.FunctionIndex];
                                    var iterationValue = _builder.BuildAlloca(ConvertTypeDefinition(function.ReturnType), function.Name);
                                    _allocationQueue.Enqueue(iterationValue);
                                }
                                break;
                            case IndexAst index:
                                BuildAllocations(index.Index);
                                if (index.CallsOverload && !structField.Pointers[i])
                                {
                                    var overload = _programGraph.OperatorOverloads[index.OverloadType.GenericName][Operator.Subscript];
                                    var iterationValue = _builder.BuildAlloca(ConvertTypeDefinition(overload.ReturnType), overload.ReturnType.GenericName);
                                    _allocationQueue.Enqueue(iterationValue);
                                }
                                break;
                        }
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
                    var indexVariable = _builder.BuildAlloca(LLVM.Int32Type(), each.IterationVariable);
                    _allocationQueue.Enqueue(indexVariable);

                    switch (each.Iteration)
                    {
                        // To get the field of a call, the value needs to be stored on the stack to use GetElementPtr
                        // @PotentialBug I can't really think of other cases that would fall under here, but this may
                        // become an issue if there are some new ways to creates lists
                        case CallAst call:
                        {
                            BuildCallAllocations(call);
                            var function = _programGraph.Functions[call.Function][call.FunctionIndex];
                            var iterationValue = _builder.BuildAlloca(ConvertTypeDefinition(function.ReturnType), function.Name);
                            _allocationQueue.Enqueue(iterationValue);
                            break;
                        }
                        case StructFieldRefAst structField:
                            switch (structField.Children[0])
                            {
                                // To get the field of a call, the value needs to be stored on the stack to use GetElementPtr
                                case CallAst call:
                                    BuildCallAllocations(call);
                                    if (!structField.Pointers[0])
                                    {
                                        var function = _programGraph.Functions[call.Function][call.FunctionIndex];
                                        var iterationValue = _builder.BuildAlloca(ConvertTypeDefinition(function.ReturnType), function.Name);
                                        _allocationQueue.Enqueue(iterationValue);
                                    }
                                    break;
                            }
                            break;
                    }

                    return BuildAllocations(each.Children);
                case ExpressionAst:
                    BuildAllocations(ast.Children);
                    break;
                case IndexAst index:
                    BuildAllocations(index.Index);
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

        private void BuildCallAllocations(CallAst call)
        {
            if (call.Params)
            {
                var functionDef = _programGraph.Functions[call.Function][call.FunctionIndex];

                var paramsTypeDef = functionDef.Arguments[^1].Type;
                var paramsType = ConvertTypeDefinition(paramsTypeDef);

                var paramsVariable = _builder.BuildAlloca(paramsType, "params");
                _allocationQueue.Enqueue(paramsVariable);

                var targetType = ConvertTypeDefinition(paramsTypeDef);
                var arrayType = LLVM.ArrayType(targetType, (uint)(call.Arguments.Count - functionDef.Arguments.Count + 1));
                var listData = _builder.BuildAlloca(arrayType, "listdata");
                _allocationQueue.Enqueue(listData);
            }

            foreach (var argument in call.Arguments)
            {
                BuildAllocations(argument);
            }
        }

        private void BuildStructAllocations(string name, List<AssignmentAst> values = null)
        {
            var assignments = values?.ToDictionary(_ => (_.Reference as IdentifierAst)!.Name);
            var structDef = _programGraph.Types[name] as StructAst;
            foreach (var field in structDef!.Fields)
            {
                if (assignments != null && assignments.TryGetValue(field.Name, out var assignment))
                {
                    BuildAllocations(assignment.Value);
                }
                else if (field.Type.Name == "List")
                {
                    if (field.Type.CArray) continue;
                    var listType = field.Type.Generics[0];
                    var targetType = ConvertTypeDefinition(listType);

                    var count = (ConstantAst)field.Type.Count;
                    var arrayType = LLVM.ArrayType(targetType, uint.Parse(count.Value));
                    var listData = _builder.BuildAlloca(arrayType, "listdata");
                    _allocationQueue.Enqueue(listData);
                }
                else if (field.Type.TypeKind == TypeKind.Struct)
                {
                    BuildStructAllocations(field.Type.GenericName);
                }
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
                LLVM.BuildRetVoid(_builder);
                return;
            }

            // 2. Get the return value
            var returnExpression = WriteExpression(returnAst.Value, localVariables);

            // 3. Write expression as return value
            var returnValue = CastValue(returnExpression, _currentFunction.ReturnType);

            // 4. Restore the stack pointer if necessary and return
            BuildStackRestore();
            LLVM.BuildRet(_builder, returnValue);
        }

        private void WriteDeclaration(DeclarationAst declaration, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Declare variable on the stack
            var type = ConvertTypeDefinition(declaration.Type);

            if (declaration.Constant)
            {
                var (_, value) = WriteExpression(declaration.Value, localVariables);

                if (declaration.Type.TypeKind == TypeKind.String)
                {
                    var stringVariable = _allocationQueue.Dequeue();
                    LLVM.BuildStore(_builder, value, stringVariable);
                    value = stringVariable;
                }

                localVariables.Add(declaration.Name, (declaration.Type, value));
                return;
            }

            var variable = _allocationQueue.Dequeue();
            localVariables.Add(declaration.Name, (declaration.Type, variable));

            // 2. Set value if it exists
            if (declaration.Value != null)
            {
                var expression = WriteExpression(declaration.Value, localVariables);
                var value = CastValue(expression, declaration.Type);

                LLVM.BuildStore(_builder, value, variable);
            }
            // 3. Initialize lists
            else if (declaration.Type.Name == "List" && !declaration.Type.CArray)
            {
                var listType = declaration.Type.Generics[0];
                if (declaration.Type.ConstCount != null)
                {
                    InitializeConstList(variable, declaration.Type.ConstCount.Value, listType);
                }
                else if (declaration.Type.Count != null)
                {
                    BuildStackSave();
                    var (_, count) = WriteExpression(declaration.Type.Count, localVariables);

                    var countPointer = _builder.BuildStructGEP(variable, 0, "countptr");
                    LLVM.BuildStore(_builder, count, countPointer);

                    var targetType = ConvertTypeDefinition(listType);
                    var listData = _builder.BuildArrayAlloca(targetType, count, "listdata");
                    var dataPointer = _builder.BuildStructGEP(variable, 1, "dataptr");
                    LLVM.BuildStore(_builder, listData, dataPointer);
                }
            }
            // 4. Initialize struct field default values
            else if (LLVM.GetTypeKind(type) == LLVMTypeKind.LLVMStructTypeKind)
            {
                InitializeStruct(declaration.Type, variable, localVariables, declaration.Assignments);
            }
            // 5. Or initialize to 0
            else if (LLVM.GetTypeKind(type) != LLVMTypeKind.LLVMArrayTypeKind)
            {
                var zero = GetConstZero(type);
                LLVM.BuildStore(_builder, zero, variable);
            }
        }

        private void InitializeStruct(TypeDefinition typeDef, LLVMValueRef variable, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, List<AssignmentAst> values = null)
        {
            var assignments = values?.ToDictionary(_ => (_.Reference as IdentifierAst)!.Name);
            var structDef = _programGraph.Types[typeDef.GenericName] as StructAst;
            for (var i = 0; i < structDef!.Fields.Count; i++)
            {
                var structField = structDef.Fields[i];
                var type = ConvertTypeDefinition(structField.Type);
                var typeKind = LLVM.GetTypeKind(type);
                if (typeKind == LLVMTypeKind.LLVMArrayTypeKind)
                    continue;

                var field = _builder.BuildStructGEP(variable, (uint) i, structField.Name);

                if (assignments != null && assignments.TryGetValue(structField.Name, out var assignment))
                {
                    var expression = WriteExpression(assignment.Value, localVariables);
                    var value = CastValue(expression, structField.Type);

                    LLVM.BuildStore(_builder, value, field);
                }
                else if (structField.Type.Name == "List")
                {
                    if (structField.Type.CArray) continue;
                    InitializeConstList(field, structField.Type.ConstCount.Value, structField.Type.Generics[0]);
                }
                else switch (typeKind)
                {
                    case LLVMTypeKind.LLVMPointerTypeKind:
                        LLVM.BuildStore(_builder, LLVM.ConstNull(type), field);
                        break;
                    case LLVMTypeKind.LLVMStructTypeKind:
                        InitializeStruct(structField.Type, field, localVariables);
                        break;
                    default:
                        switch (structField.DefaultValue)
                        {
                            case ConstantAst constant:
                                var constantValue = BuildConstant(type, constant);
                                LLVM.BuildStore(_builder, constantValue, field);
                                break;
                            case StructFieldRefAst structFieldRef:
                                var enumName = structFieldRef.TypeNames[0];
                                var enumDef = (EnumAst)_programGraph.Types[enumName];
                                var value = enumDef.Values[structFieldRef.ValueIndices[0]].Value;
                                var enumValue = LLVM.ConstInt(LLVM.Int32Type(), (ulong)value, 0);
                                LLVM.BuildStore(_builder, enumValue, field);
                                break;
                            case null:
                                LLVM.BuildStore(_builder, GetConstZero(type), field);
                                break;
                        }
                        break;
                }
            }
        }

        private void InitializeConstList(LLVMValueRef list, uint length, TypeDefinition listType)
        {
            // 1. Set the count field
            var countValue = LLVM.ConstInt(LLVM.Int32Type(), (ulong)length, 0);
            var countPointer = _builder.BuildStructGEP(list, 0, "countptr");
            LLVM.BuildStore(_builder, countValue, countPointer);

            // 2. Initialize the list data array
            var targetType = ConvertTypeDefinition(listType);
            var listData = _allocationQueue.Dequeue();
            var listDataPointer = _builder.BuildBitCast(listData, LLVM.PointerType(targetType, 0), "tmpdata");
            var dataPointer = _builder.BuildStructGEP(list, 1, "dataptr");
            LLVM.BuildStore(_builder, listDataPointer, dataPointer);
        }

        private static LLVMValueRef GetConstZero(LLVMTypeRef type)
        {
            return LLVM.GetTypeKind(type) switch
            {
                LLVMTypeKind.LLVMIntegerTypeKind => LLVM.ConstInt(type, 0, 0),
                LLVMTypeKind.LLVMFloatTypeKind => LLVM.ConstReal(type, 0),
                LLVMTypeKind.LLVMDoubleTypeKind => LLVM.ConstReal(type, 0),
                _ => LLVM.ConstInt(type, 0, 0)
            };
        }

        private void WriteAssignment(AssignmentAst assignment, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            // 1. Get the variable on the stack
            var loaded = false;
            var constant = false;
            var (type, variable) = assignment.Reference switch
            {
                IdentifierAst identifier => localVariables[identifier.Name],
                StructFieldRefAst structField => BuildStructField(structField, localVariables, out loaded, out constant),
                IndexAst index => GetListPointer(index, localVariables, out loaded),
                UnaryAst unary => WriteExpression(unary.Value, localVariables),
                // @Cleanup This branch should never be hit
                _ => (null, new LLVMValueRef())
            };
            if (loaded && type.TypeKind == TypeKind.Pointer)
            {
                type = type.Generics[0];
            }

            // 2. Evaluate the expression value
            var expression = WriteExpression(assignment.Value, localVariables);
            if (assignment.Operator != Operator.None && !constant)
            {
                // 2a. Build expression with variable value as the LHS
                var value = _builder.BuildLoad(variable, "tmpvalue");
                expression.value = BuildExpression((type, value), expression, assignment.Operator, type);
                expression.type = type; // The type should now be the type of the variable
            }

            // 3. Reallocate the value of the variable
            var assignmentValue = CastValue(expression, type);
            if (!constant) // Values are either readonly or constants, so don't store
            {
                LLVM.BuildStore(_builder, assignmentValue, variable);
            }
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
            var thenBlock = function.AppendBasicBlock("then");
            var elseBlock = function.AppendBasicBlock("else");
            var endBlock = new LLVMBasicBlockRef();
            LLVM.BuildCondBr(_builder, condition, thenBlock, elseBlock);

            // 3. Write out if body
            LLVM.PositionBuilderAtEnd(_builder, thenBlock);
            var ifReturned = WriteScope(conditional.Children, localVariables, function);

            if (!ifReturned)
            {
                if (!conditional.Else.Any())
                {
                    LLVM.BuildBr(_builder, elseBlock);
                    LLVM.PositionBuilderAtEnd(_builder, elseBlock);
                    return false;
                }
                endBlock = function.AppendBasicBlock("ifcont");
                LLVM.BuildBr(_builder, endBlock);
            }

            LLVM.PositionBuilderAtEnd(_builder, elseBlock);

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
                endBlock = function.AppendBasicBlock("ifcont");
            }
            if (!elseReturned)
            {
                LLVM.BuildBr(_builder, endBlock);
            }

            LLVM.PositionBuilderAtEnd(_builder, endBlock);
            return false;
        }

        private bool WriteWhile(WhileAst whileAst, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function)
        {
            // 1. Break to the while loop
            var whileCondition = function.AppendBasicBlock("whilecondblock");
            LLVM.BuildBr(_builder, whileCondition);

            // 2. Check condition of while loop and break if condition is not met
            LLVM.PositionBuilderAtEnd(_builder, whileCondition);
            var condition = BuildConditionExpression(whileAst.Condition, localVariables);
            var whileBody = function.AppendBasicBlock("whilebody");
            var afterWhile = function.AppendBasicBlock("afterwhile");
            LLVM.BuildCondBr(_builder, condition, whileBody, afterWhile);

            // 3. Write out while body
            LLVM.PositionBuilderAtEnd(_builder, whileBody);
            foreach (var ast in whileAst.Children)
            {
                var returned = WriteFunctionLine(ast, localVariables, function);
                if (returned)
                {
                    LLVM.DeleteBasicBlock(afterWhile);
                    return true;
                }
            }

            // 4. Jump back to the loop
            LLVM.BuildBr(_builder, whileCondition);

            // 5. Position builder to after block
            LLVM.PositionBuilderAtEnd(_builder, afterWhile);
            return false;
        }

        private LLVMValueRef BuildConditionExpression(IAst expression, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            var (type, conditionExpression) = WriteExpression(expression, localVariables);
            return type.PrimitiveType switch
            {
                IntegerType => _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, conditionExpression, LLVM.ConstInt(LLVM.TypeOf(conditionExpression), 0, 0), "ifcond"),
                FloatType => _builder.BuildFCmp(LLVMRealPredicate.LLVMRealONE, conditionExpression, LLVM.ConstReal(LLVM.TypeOf(conditionExpression), 0), "ifcond"),
                _ when type.Name == "*" => _builder.BuildIsNotNull(conditionExpression, "whilecond"),
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
                case IdentifierAst identifier:
                    (iterationType, iterationValue) = localVariables[identifier.Name];
                    break;
                case StructFieldRefAst structField:
                    (iterationType, iterationValue) = BuildStructField(structField, localVariables, out _, out _);
                    break;
                case IndexAst index:
                    (iterationType, iterationValue) = GetListPointer(index, localVariables, out _);
                    break;
                case null:
                    break;
                default:
                    var (type, value) = WriteExpression(each.Iteration, localVariables);
                    iterationType = type;
                    iterationValue = _allocationQueue.Dequeue();
                    LLVM.BuildStore(_builder, value, iterationValue);
                    break;
            }

            // 2. Initialize the first variable in the loop and the compare target
            if (each.Iteration != null)
            {
                LLVM.BuildStore(_builder, GetConstZero(LLVM.Int32Type()), indexVariable);

                switch (iterationType!.Name)
                {
                    case "List":
                    case "Params":
                        // Load the List data and set the compareTarget to the list count
                        if (iterationType.CArray)
                        {
                            listData = iterationValue;
                            compareTarget = WriteExpression(iterationType.Count, localVariables).value;
                        }
                        else
                        {
                            var dataPointer = _builder.BuildStructGEP(iterationValue, 1, "dataptr");
                            listData = _builder.BuildLoad(dataPointer, "data");

                            var lengthPointer= _builder.BuildStructGEP(iterationValue, 0, "lengthptr");
                            compareTarget = _builder.BuildLoad(lengthPointer, "length");
                        }
                        break;
                }
            }
            else
            {
                // Begin the loop at the beginning of the range
                var (type, value) = WriteExpression(each.RangeBegin, localVariables);
                LLVM.BuildStore(_builder, value, indexVariable);
                eachVariables.Add(each.IterationVariable, (type, indexVariable));

                // Get the end of the range
                (_, compareTarget) = WriteExpression(each.RangeEnd, localVariables);
            }

            // 3. Break to the each condition loop
            var eachCondition = function.AppendBasicBlock("eachcond");
            LLVM.BuildBr(_builder, eachCondition);

            // 4. Check condition of each loop and break if condition is not met
            LLVM.PositionBuilderAtEnd(_builder, eachCondition);
            var indexValue = _builder.BuildLoad(indexVariable, "curr");
            var condition = _builder.BuildICmp(each.Iteration == null ? LLVMIntPredicate.LLVMIntSLE : LLVMIntPredicate.LLVMIntSLT, indexValue, compareTarget, "listcmp");
            if (each.Iteration != null)
            {
                switch (iterationType!.Name)
                {
                    case "List":
                    case "Params":
                        var pointerIndices = iterationType.CArray ? new []{_zeroInt, indexValue} : new []{indexValue};
                        var iterationVariable = _builder.BuildGEP(listData, pointerIndices, each.IterationVariable);
                        eachVariables.TryAdd(each.IterationVariable, (each.IteratorType, iterationVariable));
                        break;
                }
            }

            var eachBody = function.AppendBasicBlock("eachbody");
            var afterEach = function.AppendBasicBlock("aftereach");
            LLVM.BuildCondBr(_builder, condition, eachBody, afterEach);

            // 5. Write out each loop body
            LLVM.PositionBuilderAtEnd(_builder, eachBody);
            foreach (var ast in each.Children)
            {
                var returned = WriteFunctionLine(ast, eachVariables, function);
                if (returned)
                {
                    LLVM.DeleteBasicBlock(afterEach);
                    return true;
                }
            }

            // 6. Increment and/or move the iteration variable
            var nextValue = _builder.BuildAdd(indexValue, LLVM.ConstInt(LLVM.Int32Type(), 1, 0), "inc");
            LLVM.BuildStore(_builder, nextValue, indexVariable);

            // 7. Write jump to the loop
            LLVM.BuildBr(_builder, eachCondition);

            // 8. Position builder to after block
            LLVM.PositionBuilderAtEnd(_builder, afterEach);
            return false;
        }

        private (TypeDefinition type, LLVMValueRef value) WriteExpression(IAst ast, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, bool getStringPointer = false)
        {
            switch (ast)
            {
                case ConstantAst constant:
                {
                    var type = ConvertTypeDefinition(constant.Type);
                    return (constant.Type, BuildConstant(type, constant, getStringPointer));
                }
                case NullAst nullAst:
                {
                    var type = ConvertTypeDefinition(nullAst.TargetType);
                    return (nullAst.TargetType, LLVMValueRef.CreateConstNull(type));
                }
                case IdentifierAst identifier:
                {
                    if (!localVariables.TryGetValue(identifier.Name, out var typeValue))
                    {
                        var typeDef = _programGraph.Types[identifier.Name];
                        return (_intTypeDefinition, LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)typeDef.TypeIndex, false));
                    }
                    var (type, value) = typeValue;
                    if (type.TypeKind == TypeKind.String)
                    {
                        if (getStringPointer)
                        {
                            value = _builder.BuildStructGEP(value, 1, "stringdata");
                        }
                        value = _builder.BuildLoad(value, identifier.Name);
                    }
                    else if (!type.Constant)
                    {
                        value = _builder.BuildLoad(value, identifier.Name);
                    }
                    return (type, value);
                }
                case StructFieldRefAst structField:
                {
                    if (structField.IsEnum)
                    {
                        var enumName = structField.TypeNames[0];
                        var enumDef = (EnumAst)_programGraph.Types[enumName];
                        var value = enumDef.Values[structField.ValueIndices[0]].Value;
                        return (enumDef.BaseType, LLVMValueRef.CreateConstInt(GetIntegerType(enumDef.BaseType.PrimitiveType), (ulong)value, false));
                    }
                    var (type, field) = BuildStructField(structField, localVariables, out var loaded, out var constant);
                    if (!loaded && !constant)
                    {
                        if (getStringPointer && type.TypeKind == TypeKind.String)
                        {
                            field = _builder.BuildStructGEP(field, 1, "stringdata");
                        }
                        field = _builder.BuildLoad(field, "field");
                    }
                    return (type, field);
                }
                case CallAst call:
                    var functions = _programGraph.Functions[call.Function];
                    LLVMValueRef function;
                    if (call.Function == "main")
                    {
                        function = _module.GetNamedFunction("__main");
                    }
                    else
                    {
                        var functionName = GetFunctionName(call.Function, call.FunctionIndex, functions.Count);
                        function = _module.GetNamedFunction(functionName);
                    }
                    var functionDef = functions[call.FunctionIndex];

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
                        InitializeConstList(paramsPointer, (uint)(call.Arguments.Count - functionDef.Arguments.Count + 1), paramsType);

                        var listData = _builder.BuildStructGEP(paramsPointer, 1, "listdata");
                        var dataPointer = _builder.BuildLoad(listData, "dataptr");

                        uint paramsIndex = 0;
                        for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                        {
                            var pointer = _builder.BuildGEP(dataPointer, new [] {LLVMValueRef.CreateConstInt(LLVM.Int32Type(), paramsIndex, false)}, "indexptr");
                            var (_, value) = WriteExpression(call.Arguments[i], localVariables);
                            LLVM.BuildStore(_builder, value, pointer);
                        }

                        var paramsValue = _builder.BuildLoad(paramsPointer, "params");
                        callArguments[functionDef.Arguments.Count - 1] = paramsValue;
                        return (functionDef.ReturnType, _builder.BuildCall(function, callArguments, string.Empty));
                    }
                    else if (functionDef.Varargs)
                    {
                        var callArguments = new LLVMValueRef[call.Arguments.Count];
                        for (var i = 0; i < functionDef.Arguments.Count - 1; i++)
                        {
                            var (_, value) = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                            callArguments[i] = value;
                        }

                        // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                        // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                        for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++)
                        {
                            var (type, value) = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                            if (type.Name == "float")
                            {
                                value = _builder.BuildFPExt(value, LLVM.DoubleType(), "tmpdouble");
                            }
                            callArguments[i] = value;
                        }

                        return (functionDef.ReturnType, _builder.BuildCall(function, callArguments, string.Empty));
                    }
                    else
                    {
                        var callArguments = new LLVMValueRef[call.Arguments.Count];
                        for (var i = 0; i < call.Arguments.Count; i++)
                        {
                            var (_, value) = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                            callArguments[i] = value;
                        }
                        return (functionDef.ReturnType, _builder.BuildCall(function, callArguments, string.Empty));
                    }
                case ChangeByOneAst changeByOne:
                {
                    var constant = false;
                    var (variableType, pointer) = changeByOne.Value switch
                    {
                        IdentifierAst identifier => localVariables[identifier.Name],
                        StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out constant),
                        IndexAst index => GetListPointer(index, localVariables, out _),
                        // @Cleanup This branch should never be hit
                        _ => (null, new LLVMValueRef())
                    };

                    var value = constant ? pointer : _builder.BuildLoad(pointer, "tmpvalue");
                    if (variableType.TypeKind == TypeKind.Pointer)
                    {
                        variableType = variableType.Generics[0];
                    }
                    var type = ConvertTypeDefinition(variableType);

                    LLVMValueRef newValue;
                    if (variableType.PrimitiveType is IntegerType)
                    {
                        newValue = changeByOne.Positive
                            ? _builder.BuildAdd(value, LLVMValueRef.CreateConstInt(type, 1, false), "inc")
                            : _builder.BuildSub(value, LLVMValueRef.CreateConstInt(type, 1, false), "dec");
                    }
                    else
                    {
                        newValue = changeByOne.Positive
                            ? _builder.BuildFAdd(value, LLVM.ConstReal(type, 1), "incf")
                            : _builder.BuildFSub(value, LLVM.ConstReal(type, 1), "decf");
                    }

                    if (!constant) // Values are either readonly or constants, so don't store
                    {
                        LLVM.BuildStore(_builder, newValue, pointer);
                    }
                    return changeByOne.Prefix ? (variableType, newValue) : (variableType, value);
                }
                case UnaryAst unary:
                {
                    if (unary.Operator == UnaryOperator.Reference)
                    {
                        var (valueType, pointer) = unary.Value switch
                        {
                            IdentifierAst identifier => localVariables[identifier.Name],
                            StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out _),
                            IndexAst index => GetListPointer(index, localVariables, out _),
                            // @Cleanup this branch should not be hit
                            _ => (null, new LLVMValueRef())
                        };
                        var pointerType = new TypeDefinition {Name = "*"};
                        if (valueType.CArray)
                        {
                            pointerType.Generics.Add(valueType.Generics[0]);
                        }
                        else
                        {
                            pointerType.Generics.Add(valueType);
                        }
                        return (pointerType, pointer);
                    }

                    var (type, value) = WriteExpression(unary.Value, localVariables);
                    return unary.Operator switch
                    {
                        UnaryOperator.Not => (type, _builder.BuildNot(value, "not")),
                        UnaryOperator.Negate => type.PrimitiveType switch
                        {
                            IntegerType => (type, _builder.BuildNeg(value, "neg")),
                            FloatType => (type, _builder.BuildFNeg(value, "fneg")),
                            // @Cleanup This branch should not be hit
                            _ => (null, new LLVMValueRef())
                        },
                        UnaryOperator.Dereference => (type.Generics[0], _builder.BuildLoad(value, "tmpderef")),
                        // @Cleanup This branch should not be hit
                        _ => (null, new LLVMValueRef())
                    };
                }
                case IndexAst index:
                {
                    var (elementType, elementValue) = GetListPointer(index, localVariables, out var loaded);
                    if (!loaded)
                    {
                        elementValue = _builder .BuildLoad(elementValue, "tmpindex");
                    }
                    return (elementType, elementValue);
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
                case TypeDefinition typeDef:
                {
                    var type = _programGraph.Types[typeDef.GenericName];
                    return (_intTypeDefinition, LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)type.TypeIndex, false));
                }
                case CastAst cast:
                {
                    var value = WriteExpression(cast.Value, localVariables);
                    return (cast.TargetType, CastValue(value, cast.TargetType));
                }
                default:
                    // @Cleanup This branch should not be hit since we've already verified that these ASTs are handled,
                    // but notify the user and exit just in case
                    Console.WriteLine($"Unexpected syntax tree");
                    Environment.Exit(ErrorCodes.BuildError);
                    return (null, new LLVMValueRef()); // Return never happens
            }
        }

        private string GetFunctionName(string name, int functionIndex, int functionCount)
        {
            return functionCount == 1 ? name : $"{name}.{functionIndex}";
        }

        private string GetOperatorOverloadName(TypeDefinition type, Operator op)
        {
            return GetOperatorOverloadName(type.GenericName, op);
        }

        private string GetOperatorOverloadName(string typeName, Operator op)
        {
            return $"operator.{op}.{typeName}";
        }

        private readonly LLVMTypeRef _stackPointerType = LLVM.PointerType(LLVM.Int8Type(), 0);

        private void BuildStackPointer()
        {
            if (_stackPointerExists) return;

            _stackPointer = _builder.BuildAlloca(_stackPointerType, "stackPtr");
            _stackPointerExists = true;
        }

        private void BuildStackSave()
        {
            if (_stackSaved) return;
            const string stackSaveIntrinsic = "llvm.stacksave";

            var function = _module.GetNamedFunction(stackSaveIntrinsic);
            if (function.Handle == IntPtr.Zero)
            {
                function = _module.AddFunction(stackSaveIntrinsic, LLVMTypeRef.CreateFunction(_stackPointerType, Array.Empty<LLVMTypeRef>()));
            }

            var stackPointer = _builder.BuildCall(function, Array.Empty<LLVMValueRef>(), "stackPointer");
            LLVM.BuildStore(_builder, stackPointer, _stackPointer);
            _stackSaved = true;
        }

        private void BuildStackRestore()
        {
            if (!_stackSaved) return;
            const string stackRestoreIntrinsic = "llvm.stackrestore";

            var function = _module.GetNamedFunction(stackRestoreIntrinsic);
            if (function.Handle == IntPtr.Zero)
            {
                function = _module.AddFunction(stackRestoreIntrinsic, LLVMTypeRef.CreateFunction(LLVM.VoidType(), new [] {_stackPointerType}));
            }

            var stackPointer = _builder.BuildLoad(_stackPointer, "stackPointer");
            _builder.BuildCall(function, new []{stackPointer}, "");
        }

        private LLVMValueRef BuildConstant(LLVMTypeRef type, ConstantAst constant, bool getStringPointer = false)
        {
            switch (constant.Type.PrimitiveType)
            {
                case IntegerType integerType:
                    if (constant.Type.Character)
                    {
                        return LLVMValueRef.CreateConstInt(type, (byte)constant.Value[0], false);
                    }
                    if (integerType.Bytes == 8 && !integerType.Signed)
                    {
                        return LLVMValueRef.CreateConstInt(type, ulong.Parse(constant.Value), false);
                    }
                    return LLVMValueRef.CreateConstInt(type, (ulong)long.Parse(constant.Value), false);
                case FloatType:
                    return LLVMValueRef.CreateConstRealOfStringAndSize(type, constant.Value, (uint)constant.Value.Length);
            }

            switch (constant.Type.Name)
            {
                case "bool":
                    return LLVMValueRef.CreateConstInt(type, constant.Value == "true" ? (ulong)1 : 0, false);
                case "string":
                    return BuildString(constant.Value, getStringPointer, constant.Type.Constant);
                default:
                    return _zeroInt;
            }
        }

        private LLVMValueRef BuildString(string value, bool getStringPointer = false, bool constant = true)
        {
            var stringValue = _context.GetConstString(value, false);
            var stringGlobal = _module.AddGlobal(LLVM.TypeOf(stringValue), "str");
            if (constant)
            {
                SetPrivateConstant(stringGlobal);
            }
            LLVM.SetInitializer(stringGlobal, stringValue);
            var stringPointer = LLVMValueRef.CreateConstBitCast(stringGlobal, _u8PointerType);

            if (getStringPointer)
            {
                return stringPointer;
            }

            var length = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)value.Length, false);
            return LLVMValueRef.CreateConstNamedStruct(_stringType, new [] {length, stringPointer});
        }

        private (TypeDefinition type, LLVMValueRef value) BuildStructField(StructFieldRefAst structField, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, out bool loaded, out bool constant)
        {
            loaded = false;
            constant = false;
            TypeDefinition type = null;
            LLVMValueRef value = new LLVMValueRef();

            switch (structField.Children[0])
            {
                case IdentifierAst identifier:
                    (type, value) = localVariables[identifier.Name];
                    break;
                case IndexAst index:
                    var (indexType, indexValue) = GetListPointer(index, localVariables, out _);
                    type = indexType;
                    if (index.CallsOverload && !structField.Pointers[0])
                    {
                        value = _allocationQueue.Dequeue();
                        LLVM.BuildStore(_builder, indexValue, value);
                    }
                    else
                    {
                        value = indexValue;
                    }
                    break;
                case CallAst call:
                    var (callType, callValue) = WriteExpression(call, localVariables);
                    type = callType;
                    if (structField.Pointers[0])
                    {
                        value = callValue;
                    }
                    else
                    {
                        value = _allocationQueue.Dequeue();
                        LLVM.BuildStore(_builder, callValue, value);
                    }
                    break;
                default:
                    // @Cleanup this branch shouldn't be hit
                    Console.WriteLine("Unexpected syntax tree in struct field ref");
                    Environment.Exit(ErrorCodes.BuildError);
                    break;
            }

            var skipPointer = false;
            for (var i = 1; i < structField.Children.Count; i++)
            {
                if (structField.Pointers[i-1])
                {
                    if (!skipPointer)
                    {
                        value = _builder.BuildLoad(value, "pointerval");
                    }
                    type = type.Generics[0];
                }
                skipPointer = false;

                if (type.CArray)
                {
                    switch (structField.Children[i])
                    {
                        case IdentifierAst identifier:
                            constant = true;
                            if (identifier.Name == "length")
                            {
                                (type, value) = WriteExpression(type.Count, localVariables);
                            }
                            else
                            {
                                type = new TypeDefinition {Name = "*", Generics = {type.Generics[0]}};
                                value = _builder.BuildGEP(value, new []{_zeroInt, _zeroInt}, "dataPtr");
                            }
                            break;
                        case IndexAst index:
                            var (_, indexValue) = WriteExpression(index.Index, localVariables);
                            value = _builder.BuildGEP(value, new []{_zeroInt, indexValue}, "indexptr");
                            type = type.Generics[0];
                            break;
                    }
                    continue;
                }

                var structName = structField.TypeNames[i-1];
                var structDefinition = (StructAst) _programGraph.Types[structName];
                type = structDefinition.Fields[structField.ValueIndices[i-1]].Type;

                switch (structField.Children[i])
                {
                    case IdentifierAst identifier:
                        value = _builder.BuildStructGEP(value, (uint)structField.ValueIndices[i-1], identifier.Name);
                        break;
                    case IndexAst index:
                        value = _builder.BuildStructGEP(value, (uint)structField.ValueIndices[i-1], index.Name);
                        (type, value) = GetListPointer(index, localVariables, out _, type, value);

                        if (index.CallsOverload)
                        {
                            skipPointer = true;
                            if (i < structField.Pointers.Length && !structField.Pointers[i])
                            {
                                var pointer = _allocationQueue.Dequeue();
                                LLVM.BuildStore(_builder, value, pointer);
                                value = pointer;
                            }
                            else if (i == structField.Pointers.Length)
                            {
                                loaded = true;
                            }
                        }
                        break;
                }
            }

            return (type, value);
        }

        private StructAst _stringStruct;

        private (TypeDefinition type, LLVMValueRef value) GetListPointer(IndexAst index, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, out bool loaded, TypeDefinition type = null, LLVMValueRef variable = default)
        {
            // 1. Get the variable pointer
            if (type == null)
            {
                (type, variable) = localVariables[index.Name];
            }

            // 2. Determine the index
            var (_, indexValue) = WriteExpression(index.Index, localVariables);

            // 3. Call the overload if needed
            if (index.CallsOverload)
            {
                var overloadName = GetOperatorOverloadName(type, Operator.Subscript);
                var overload = _module.GetNamedFunction(overloadName);
                var overloadDef = _programGraph.OperatorOverloads[type.GenericName][Operator.Subscript];

                loaded = true;
                return (overloadDef.ReturnType, _builder.BuildCall(overload, new []{_builder.BuildLoad(variable, index.Name), indexValue}, string.Empty));
            }

            // 4. Build the pointer with the first index of 0
            TypeDefinition elementType;
            if (type.TypeKind == TypeKind.String)
            {
                _stringStruct ??= (StructAst)_programGraph.Types["string"];
                elementType = _stringStruct.Fields[1].Type.Generics[0];
            }
            else
            {
                elementType = type.Generics[0];
            }
            LLVMValueRef listPointer;
            if (type.TypeKind == TypeKind.Pointer)
            {
                var dataPointer = _builder.BuildLoad(variable, "dataptr");
                listPointer = _builder.BuildGEP(dataPointer, new []{indexValue}, "indexptr");
            }
            else if (type.CArray)
            {
                listPointer = _builder.BuildGEP(variable, new []{_zeroInt, indexValue}, "dataptr");
            }
            else
            {
                var listData = _builder.BuildStructGEP(variable, 1, "listdata");
                var dataPointer = _builder.BuildLoad(listData, "dataptr");
                listPointer = _builder.BuildGEP(dataPointer, new [] {indexValue}, "indexptr");
            }
            loaded = false;
            return (elementType, listPointer);
        }

        private LLVMValueRef BuildExpression((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, Operator op, TypeDefinition targetType)
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

            // 2. Handle compares and shifts, since the lhs and rhs should not be cast to the target type
            switch (op)
            {
                case Operator.And:
                    return lhs.type.Name == "bool" ? _builder.BuildAnd(lhs.value, rhs.value, "tmpand")
                        : BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, Operator.And);
                case Operator.Or:
                    return lhs.type.Name == "bool" ? _builder.BuildOr(lhs.value, rhs.value, "tmpor")
                        : BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, Operator.Or);
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return BuildCompare(lhs, rhs, op);
                case Operator.ShiftLeft:
                    return BuildShift(lhs, rhs);
                case Operator.ShiftRight:
                    return BuildShift(lhs, rhs, true);
                case Operator.RotateLeft:
                    return BuildRotate(lhs, rhs);
                case Operator.RotateRight:
                    return BuildRotate(lhs, rhs, true);
            }

            // 3. Handle overloaded operators
            if (lhs.type.PrimitiveType == null && lhs.type.Name != "bool")
            {
                return BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, op);
            }

            // 4. Cast lhs and rhs to the target types
            lhs.value = CastValue(lhs, targetType);
            rhs.value = CastValue(rhs, targetType);

            // 5. Handle the rest of the simple operators
            switch (op)
            {
                case Operator.BitwiseAnd:
                    return _builder.BuildAnd(lhs.value, rhs.value, "tmpband");
                case Operator.BitwiseOr:
                    return _builder.BuildOr(lhs.value, rhs.value, "tmpbor");
                case Operator.Xor:
                    return _builder.BuildXor(lhs.value, rhs.value, "tmpxor");
            }

            // 6. Handle binary operations
            var signed = lhs.type.PrimitiveType.Signed || rhs.type.PrimitiveType.Signed;
            return BuildBinaryOperation(targetType, lhs.value, rhs.value, op, signed);
        }

        private LLVMValueRef BuildPointerOperation(LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            if (op == Operator.Equality)
            {
                if (rhs.IsNull)
                {
                    return _builder.BuildIsNull(lhs, "isnull");
                }
                var diff = _builder.BuildPtrDiff(lhs, rhs, "ptrdiff");
                return _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, diff, LLVMValueRef.CreateConstInt(LLVM.TypeOf(diff), 0, false), "ptreq");
            }
            if (op == Operator.NotEqual)
            {
                if (rhs.IsNull)
                {
                    return _builder.BuildIsNotNull(lhs, "notnull");
                }
                var diff = _builder.BuildPtrDiff(lhs, rhs, "ptrdiff");
                return _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, diff, LLVMValueRef.CreateConstInt(LLVM.TypeOf(diff), 0, false), "ptreq");
            }
            if (op == Operator.Subtract)
            {
                rhs = _builder.BuildNeg(rhs, "tmpneg");
            }
            return _builder.BuildGEP(lhs, new []{rhs}, "tmpptr");
        }

        private LLVMValueRef BuildCompare((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, Operator op)
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
                                rhs.value = signed ? _builder.BuildSExt(rhs.value, type, "tmpint") :
                                    _builder.BuildZExt(rhs.value, type, "tmpint");
                            }
                            else if (lhsInt.Bytes < rhsInt.Bytes)
                            {
                                var type = ConvertTypeDefinition(rhs.type);
                                lhs.value = signed ? _builder.BuildSExt(lhs.value, type, "tmpint") :
                                    _builder.BuildZExt(lhs.value, type, "tmpint");
                            }
                            var (predicate, name) = ConvertIntOperator(op, signed);
                            return _builder.BuildICmp(predicate, lhs.value, rhs.value, name);
                        }
                        case FloatType:
                        {
                            var lhsValue = CastValue(lhs, rhs.type, false);
                            var (predicate, name) = ConvertRealOperator(op);
                            return _builder.BuildFCmp(predicate, lhsValue, rhs.value, name);
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
                            return _builder.BuildFCmp(predicate, lhs.value, rhsValue, name);
                        }
                        case FloatType rhsFloat:
                        {
                            if (lhsFloat.Bytes > rhsFloat.Bytes)
                            {
                                rhs.value = _builder.BuildFPCast(rhs.value, LLVM.DoubleType(), "tmpfp");
                            }
                            else if (lhsFloat.Bytes < rhsFloat.Bytes)
                            {
                                lhs.value = _builder.BuildFPCast(lhs.value, LLVM.DoubleType(), "tmpfp");
                            }
                            var (predicate, name) = ConvertRealOperator(op);
                            return _builder.BuildFCmp(predicate, lhs.value, rhs.value, name);
                        }
                    }
                    break;
                case EnumType:
                {
                    var (predicate, name) = ConvertIntOperator(op);
                    return _builder.BuildICmp(predicate, lhs.value, rhs.value, name);
                }
            }
            return BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, op);
        }

        private LLVMValueRef BuildShift((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, bool right = false)
        {
            if (lhs.type.PrimitiveType is IntegerType)
            {
                var result = right ? _builder.BuildAShr(lhs.value, rhs.value, "tmpshr")
                    : _builder.BuildShl(lhs.value, rhs.value, "tmpshl");

                return result;
            }

            return BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, right ? Operator.ShiftRight : Operator.ShiftLeft);
        }

        private LLVMValueRef BuildRotate((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, bool right = false)
        {
            if (lhs.type.PrimitiveType is IntegerType)
            {
                var result = BuildShift(lhs, rhs, right);

                var maskSize = LLVMValueRef.CreateConstInt(ConvertTypeDefinition(lhs.type), (uint)(lhs.type.PrimitiveType?.Bytes * 8 ?? 32), false);
                var maskShift = _builder.BuildSub(maskSize, rhs.value, "mask");

                var mask = right ? _builder.BuildShl(lhs.value, maskShift, "tmpshl")
                    : _builder.BuildAShr(lhs.value, maskShift, "tmpshr");

                return result.IsUndef ? mask : _builder.BuildOr(result, mask, "tmpmask");
            }

            return BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, right ? Operator.RotateRight : Operator.RotateLeft);
        }

        private LLVMValueRef BuildBinaryOperation(TypeDefinition type, LLVMValueRef lhs, LLVMValueRef rhs, Operator op, bool signed = true)
        {
            switch (type.PrimitiveType)
            {
                case IntegerType:
                    return BuildIntOperation(lhs, rhs, op, signed);
                case FloatType:
                    return BuildRealOperation(lhs, rhs, op);
                default:
                    // @Cleanup this shouldn't be hit
                    Console.WriteLine("Operator not compatible");
                    Environment.Exit(ErrorCodes.BuildError);
                    return new LLVMValueRef(); // Return never happens
            }
        }

        private LLVMValueRef BuildOperatorOverloadCall(TypeDefinition type, LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            var overloadName = GetOperatorOverloadName(type, op);
            var overload = _module.GetNamedFunction(overloadName);

            return _builder.BuildCall(overload, new []{lhs, rhs}, string.Empty);
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
                                return intTarget.Signed ? _builder.BuildSExtOrBitCast(value, target, "tmpint") :
                                    _builder.BuildZExtOrBitCast(value, target, "tmpint");
                            }
                            else
                            {
                                return _builder.BuildTrunc(value, target, "tmpint");
                            }
                        case FloatType:
                            return intType.Signed ? _builder.BuildSIToFP(value, target, "tmpfp") :
                                _builder.BuildUIToFP(value, target, "tmpfp");
                    }
                    break;
                case FloatType:
                    switch (targetType.PrimitiveType)
                    {
                        case IntegerType intTarget:
                            return intTarget.Signed ? _builder.BuildFPToSI(value, target, "tmpfp") :
                                _builder.BuildFPToUI(value, target, "tmpfp");
                        case FloatType:
                            return _builder.BuildFPCast(value, target, "tmpfp");
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
                Operator.Add => _builder.BuildAdd(lhs, rhs, "tmpadd"),
                Operator.Subtract => _builder.BuildSub(lhs, rhs, "tmpsub"),
                Operator.Multiply => _builder.BuildMul(lhs, rhs, "tmpmul"),
                Operator.Divide => signed ? _builder.BuildSDiv(lhs, rhs, "tmpdiv") :
                    _builder.BuildUDiv(lhs, rhs, "tmpdiv"),
                Operator.Modulus => signed ? _builder.BuildSRem(lhs, rhs, "tmpmod") :
                    _builder.BuildURem(lhs, rhs, "tmpmod"),
                // @Cleanup This branch should never be hit
                _ => new LLVMValueRef()
            };
        }

        private LLVMValueRef BuildRealOperation(LLVMValueRef lhs, LLVMValueRef rhs, Operator op)
        {
            switch (op)
            {
                case Operator.Add: return _builder.BuildFAdd(lhs, rhs, "tmpadd");
                case Operator.Subtract: return _builder.BuildFSub(lhs, rhs, "tmpsub");
                case Operator.Multiply: return _builder.BuildFMul(lhs, rhs, "tmpmul");
                case Operator.Divide: return _builder.BuildFDiv(lhs, rhs, "tmpdiv");
                case Operator.Modulus:
                    _programGraph.Dependencies.Add("m");
                    return _builder.BuildFRem(lhs, rhs, "tmpmod");
                // @Cleanup This branch should never be hit
                default: return new LLVMValueRef();
            };
        }

        private LLVMTypeRef ConvertTypeDefinition(TypeDefinition type, bool externFunction = false, bool pointer = false)
        {
            if (type.Name == "*")
            {
                return LLVM.PointerType(ConvertTypeDefinition(type.Generics[0], externFunction, true), 0);
            }

            return type.PrimitiveType switch
            {
                IntegerType integerType => GetIntegerType(integerType),
                FloatType floatType => floatType.Bytes == 8 ? LLVM.DoubleType() : LLVM.FloatType(),
                EnumType enumType => GetIntegerType(enumType),
                _ => type.Name switch
                {
                    "bool" => LLVM.Int1Type(),
                    "void" => pointer ? LLVM.Int8Type() : LLVM.VoidType(),
                    "List" => GetListType(type),
                    "Params" => GetListType(type),
                    "string" => externFunction ? LLVM.PointerType(LLVM.Int8Type(), 0) : _module.GetTypeByName("string"),
                    "Type" => LLVM.Int32Type(),
                    _ => GetStructType(type)
                }
            };
        }

        private LLVMTypeRef GetIntegerType(IPrimitive primitive)
        {
            return primitive.Bytes switch
            {
                1 => LLVM.Int8Type(),
                2 => LLVM.Int16Type(),
                4 => LLVM.Int32Type(),
                8 => LLVM.Int64Type(),
                _ => LLVM.Int32Type()
            };
        }

        private LLVMTypeRef GetListType(TypeDefinition type)
        {
            var listType = type.Generics[0];

            if (type.CArray)
            {
                var elementType = ConvertTypeDefinition(listType);
                return LLVM.ArrayType(elementType, type.ConstCount.Value);
            }

            return _module.GetTypeByName($"List.{listType.GenericName}");
        }

        private LLVMTypeRef GetStructType(TypeDefinition type)
        {
            if (_programGraph.Types.TryGetValue(type.Name, out var typeDef) && typeDef is EnumAst)
            {
                return LLVM.Int32Type();
            }

            return _module.GetTypeByName(type.GenericName);
        }
    }
}
