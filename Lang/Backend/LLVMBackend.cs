using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private Dictionary<string, LLVMMetadataRef> _debugTypes;
        private Dictionary<string, LLVMMetadataRef> _debugFunctions;

        private readonly Queue<LLVMValueRef> _allocationQueue = new();
        private readonly LLVMValueRef _zeroInt = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), 0, false);
        private readonly TypeDefinition _s32Type = new() {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};

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
                    WriteFunction(functionAst, argumentCount, globals, function, functionName);
                }
            }
            foreach (var (type, overloads) in _programGraph.OperatorOverloads)
            {
                foreach (var (op, overload) in overloads)
                {
                    var overloadName = GetOperatorOverloadName(type, op);
                    var function = _module.GetNamedFunction(overloadName);
                    WriteFunction(overload, 2, globals, function, overloadName);
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
                LLVM.AddBasicAliasAnalysisPass(_passManager);
                LLVM.AddPromoteMemoryToRegisterPass(_passManager);
                LLVM.AddInstructionCombiningPass(_passManager);
                LLVM.AddReassociatePass(_passManager);
                LLVM.AddGVNPass(_passManager);
                LLVM.AddCFGSimplificationPass(_passManager);

                LLVM.InitializeFunctionPassManager(_passManager);
            }
            else
            {
                _emitDebug = true;
                _debugBuilder = _module.CreateDIBuilder();
                _debugFiles = project.SourceFiles.Select(file => _debugBuilder.CreateFile(Path.GetFileName(file), Path.GetDirectoryName(file))).ToList();
                _debugCompilationUnit = _debugBuilder.CreateCompileUnit(LLVMDWARFSourceLanguage.LLVMDWARFSourceLanguageC, _debugFiles[0], "ol", 0, string.Empty, 0, string.Empty, LLVMDWARFEmissionKind.LLVMDWARFEmissionFull, 0, 0, 0, string.Empty, string.Empty);

                AddModuleFlag("Dwarf Version", 4);
                AddModuleFlag("Debug Info Version", LLVM.DebugMetadataVersion());
                AddModuleFlag("PIE Level", 2);

                _debugTypes = new Dictionary<string, LLVMMetadataRef>();
                _debugFunctions = new Dictionary<string, LLVMMetadataRef>();
            }
        }

        private void AddModuleFlag(string flagName, uint flagValue)
        {
            using var name = new MarshaledString(flagName);
            var value = LLVM.ValueAsMetadata(LLVM.ConstInt(LLVM.Int32Type(), flagValue, 0));
            LLVM.AddModuleFlag(_module, LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, name.Value, (UIntPtr)name.Length, value);
        }

        private IDictionary<string, (TypeDefinition type, LLVMValueRef value)> WriteData()
        {
            // 1. Declare structs and enums
            var structs = new Dictionary<string, LLVMTypeRef>();
            if (_emitDebug)
            {
                foreach (var (name, type) in _programGraph.Types)
                {
                    switch (type)
                    {
                        case StructAst structAst:
                            structs[name] = _context.CreateNamedStruct(name);

                            if (structAst.Fields.Any())
                            {
                                using var structName = new MarshaledString(structAst.Name);

                                var file = _debugFiles[structAst.FileIndex];
                                _debugTypes[name] = LLVM.DIBuilderCreateForwardDecl(_debugBuilder, (uint)DwarfTag.Structure_type, structName.Value, (UIntPtr)structName.Length, null, file, structAst.Line, 0, structAst.Size * 8, 0, null, (UIntPtr)0);
                            }
                            else
                            {
                                CreateDebugStructType(structAst, name);
                            }
                            break;
                        case EnumAst enumAst:
                            CreateDebugEnumType(enumAst);
                            break;
                        case PrimitiveAst primitive:
                            CreateDebugBasicType(primitive, name);
                            break;
                    }
                }
                foreach (var (name, type) in _programGraph.Types)
                {
                    if (type is StructAst structAst && structAst.Fields.Any())
                    {
                        var fields = structAst.Fields.Select(field => ConvertTypeDefinition(field.Type)).ToArray();
                        structs[name].StructSetBody(fields, false);

                        CreateDebugStructType(structAst, name);
                    }
                }
            }
            else
            {
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
                    else if (typeDef.TypeKind == TypeKind.Pointer)
                    {
                        LLVM.SetInitializer(global, LLVM.ConstNull(type));
                    }

                    if (_emitDebug)
                    {
                        using var name = new MarshaledString(globalVariable.Name);

                        var file = _debugFiles[globalVariable.FileIndex];
                        var debugType = GetDebugType(globalVariable.Type);
                        var globalDebug = LLVM.DIBuilderCreateGlobalVariableExpression(_debugBuilder, _debugCompilationUnit, name.Value, (UIntPtr)name.Length, null, (UIntPtr)0, file, globalVariable.Line, debugType, 0, null, null, 0);
                        LLVM.GlobalSetMetadata(global, 0, globalDebug);
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
            var typeFieldArrayType = _module.GetTypeByName("Array.TypeField");
            var enumValueType = _module.GetTypeByName("EnumValue");
            var enumValueArrayType = _module.GetTypeByName("Array.EnumValue");
            var argumentType = _module.GetTypeByName("ArgumentType");
            var argumentArrayType = _module.GetTypeByName("Array.ArgumentType");
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

                        var typeField = LLVMValueRef.CreateConstNamedStruct(typeFieldType, new LLVMValueRef[] {fieldNameString, fieldOffset, typePointers[field.Type.GenericName].typeInfo});

                        typeFields[i] = typeField;
                    }

                    var typeFieldArray = LLVMValueRef.CreateConstArray(typeInfoType, typeFields);
                    var typeFieldArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typeFieldArray), "____type_fields");
                    SetPrivateConstant(typeFieldArrayGlobal);
                    LLVM.SetInitializer(typeFieldArrayGlobal, typeFieldArray);

                    fields = LLVMValueRef.CreateConstNamedStruct(typeFieldArrayType, new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)structAst.Fields.Count, 0),
                        typeFieldArrayGlobal
                    });
                }
                else
                {
                    fields = LLVMValueRef.CreateConstNamedStruct(typeFieldArrayType, new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(typeFieldType, 0))});
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

                        enumValueRefs[i] = LLVMValueRef.CreateConstNamedStruct(enumValueType, new LLVMValueRef[] {enumValueNameString, enumValue});
                    }

                    var enumValuesArray = LLVMValueRef.CreateConstArray(typeInfoType, enumValueRefs);
                    var enumValuesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(enumValuesArray), "____enum_values");
                    SetPrivateConstant(enumValuesArrayGlobal);
                    LLVM.SetInitializer(enumValuesArrayGlobal, enumValuesArray);

                    enumValues = LLVMValueRef.CreateConstNamedStruct(enumValueArrayType, new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)enumAst.Values.Count, 0),
                        enumValuesArrayGlobal
                    });
                }
                else
                {
                    enumValues = LLVMValueRef.CreateConstNamedStruct(enumValueArrayType, new LLVMValueRef[] {_zeroInt, LLVM.ConstNull(LLVM.PointerType(enumValueType, 0))});
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
                            "Params" => typePointers[$"Array.{argument.Type.Generics[0].GenericName}"].typeInfo,
                            _ => typePointers[argument.Type.GenericName].typeInfo
                        };

                        var argumentValue = LLVMValueRef.CreateConstNamedStruct(argumentType, new LLVMValueRef[] {argNameString, argumentTypeInfo});

                        argumentValues[i] = argumentValue;
                    }

                    var argumentArray = LLVMValueRef.CreateConstArray(typeInfoType, argumentValues);
                    var argumentArrayGlobal = _module.AddGlobal(LLVM.TypeOf(argumentArray), "____type_fields");
                    SetPrivateConstant(argumentArrayGlobal);
                    LLVM.SetInitializer(argumentArrayGlobal, argumentArray);

                    arguments = LLVMValueRef.CreateConstNamedStruct(argumentArrayType, new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)function.Arguments.Count, 0),
                        argumentArrayGlobal
                    });
                }
                else
                {
                    returnType = LLVM.ConstNull(LLVM.PointerType(typeFieldType, 0));
                    arguments = LLVMValueRef.CreateConstNamedStruct(argumentArrayType, new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(argumentType, 0))});
                }

                LLVM.SetInitializer(typeInfo, LLVMValueRef.CreateConstNamedStruct(typeInfoType, new LLVMValueRef[] {typeNameString, typeKind, typeSize, fields, enumValues, returnType, arguments}));
            }

            var typeArray = LLVMValueRef.CreateConstArray(LLVM.PointerType(typeInfoType, 0), types);
            var typeArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typeArray), "____type_array");
            SetPrivateConstant(typeArrayGlobal);
            LLVM.SetInitializer(typeArrayGlobal, typeArray);

            var typeCount = LLVM.ConstInt(LLVM.Int32Type(), (ulong)types.Length, 0);
            var typeInfoArrayType = structs["Array.*.TypeInfo"];
            LLVM.SetInitializer(typeTable, LLVMValueRef.CreateConstNamedStruct(typeInfoArrayType, new LLVMValueRef[] {typeCount, typeArrayGlobal}));

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

            if (_emitDebug && !externFunction)
            {
                // Get the argument types and create debug symbols
                var debugArgumentTypes = new LLVMMetadataRef[argumentCount + 1];
                debugArgumentTypes[0] = GetDebugType(functionAst.ReturnType);

                for (var i = 0; i < argumentCount; i++)
                {
                    var argumentType = functionAst.Arguments[i].Type;
                    argumentTypes[i] = ConvertTypeDefinition(argumentType);
                    debugArgumentTypes[i + 1] = GetDebugType(argumentType);
                }

                var file = _debugFiles[functionAst.FileIndex];
                var functionType = _debugBuilder.CreateSubroutineType(file, debugArgumentTypes, LLVMDIFlags.LLVMDIFlagZero);
                var debugFunction = _debugFunctions[name] = _debugBuilder.CreateFunction(file, debugName, name, file, functionAst.Line, functionType, 0, 1, functionAst.Line, LLVMDIFlags.LLVMDIFlagPrototyped, 0);

                // Declare the function
                var function = _module.AddFunction(name, LLVMTypeRef.CreateFunction(ConvertTypeDefinition(functionAst.ReturnType), argumentTypes.ToArray(), varargs));
                LLVM.SetSubprogram(function, debugFunction);
            }
            else
            {
                // Get the argument types and declare the function
                for (var i = 0; i < argumentCount; i++)
                {
                    argumentTypes[i] = ConvertTypeDefinition(functionAst.Arguments[i].Type, externFunction);
                }
                _module.AddFunction(name, LLVMTypeRef.CreateFunction(ConvertTypeDefinition(functionAst.ReturnType), argumentTypes.ToArray(), varargs));
            }
        }

        private void WriteFunction(IFunction functionAst, int argumentCount, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> globals, LLVMValueRef function, string functionName)
        {
            _currentFunction = functionAst;
            // 1. Get function definition
            var entryBlock = function.AppendBasicBlock("entry");
            LLVM.PositionBuilderAtEnd(_builder, entryBlock);
            var localVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(globals);

            // 2. Allocate arguments on the stack
            if (_emitDebug)
            {
                _builder.CurrentDebugLocation = null;
            }
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
            LLVMMetadataRef block = null;
            if (_emitDebug)
            {
                block = _debugFunctions[functionName];
                var file = _debugFiles[functionAst.FileIndex];
                for (var i = 0; i < argumentCount; i++)
                {
                    var arg = functionAst.Arguments[i];
                    var argument = LLVM.GetParam(function, (uint) i);
                    var variable = localVariables[arg.Name].value;
                    LLVM.BuildStore(_builder, argument, variable);

                    using var argName = new MarshaledString(arg.Name);

                    var debugType = GetDebugType(arg.Type);
                    var debugVariable = LLVM.DIBuilderCreateParameterVariable(_debugBuilder, block, argName.Value, (UIntPtr)argName.Length, (uint)i+1, file, arg.Line, debugType, 0, LLVMDIFlags.LLVMDIFlagZero);
                    var expression = LLVM.DIBuilderCreateExpression(_debugBuilder, null, (UIntPtr)0);
                    var location = LLVM.DIBuilderCreateDebugLocation(_context, arg.Line, arg.Column, block, null);

                    LLVM.DIBuilderInsertDeclareAtEnd(_debugBuilder, variable, debugVariable, expression, location, entryBlock);
                }
            }
            else
            {
                for (var i = 0; i < argumentCount; i++)
                {
                    var arg = functionAst.Arguments[i];
                    var argument = LLVM.GetParam(function, (uint) i);
                    var variable = localVariables[arg.Name].value;
                    LLVM.BuildStore(_builder, argument, variable);
                }
            }

            // 5. Loop through function body
            var returned = false;
            foreach (var ast in functionAst.Children)
            {
                // 5a. Recursively write out lines
                if (WriteFunctionLine(ast, localVariables, function, block))
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
        }

        private void Compile(string objectFile, bool outputIntermediate)
        {
            if (_emitDebug)
            {
                LLVM.DIBuilderFinalize(_debugBuilder);
            }

            #if DEBUG
            LLVM.VerifyModule(_module, LLVMVerifierFailureAction.LLVMPrintMessageAction, null);
            #endif

            LLVM.InitializeX86TargetInfo();
            LLVM.InitializeX86Target();
            LLVM.InitializeX86TargetMC();
            LLVM.InitializeX86AsmParser();
            LLVM.InitializeX86AsmPrinter();

            var target = LLVMTargetRef.Targets.FirstOrDefault(t => t.Name == "x86-64");
            var defaultTriple = LLVMTargetRef.DefaultTriple;
            _module.Target = defaultTriple;

            var targetMachine = target.CreateTargetMachine(defaultTriple, "generic", string.Empty, LLVMCodeGenOptLevel.LLVMCodeGenLevelAggressive, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);
            _module.DataLayout = Marshal.PtrToStringAnsi(targetMachine.CreateTargetDataLayout().Handle);

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
                    else if (declaration.Type.TypeKind == TypeKind.Array && !declaration.Type.CArray)
                    {
                        if (declaration.Type.ConstCount != null)
                        {
                            var elementType = declaration.Type.Generics[0];
                            var targetType = ConvertTypeDefinition(elementType);
                            var arrayType = LLVM.ArrayType(targetType, declaration.Type.ConstCount.Value);
                            var arrayData = _builder.BuildAlloca(arrayType, "arraydata");
                            _allocationQueue.Enqueue(arrayData);
                        }
                        else if (declaration.Type.Count != null)
                        {
                            BuildStackPointer();
                        }
                    }
                    else if (declaration.Type.TypeKind == TypeKind.Struct)
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
                case ScopeAst scope:
                    return BuildAllocations(scope.Children);
                case ConditionalAst conditional:
                    BuildAllocations(conditional.Condition);
                    var ifReturned = BuildAllocations(conditional.IfBlock.Children);

                    if (conditional.ElseBlock != null)
                    {
                        var elseReturned = BuildAllocations(conditional.ElseBlock.Children);
                        return ifReturned && elseReturned;
                    }
                    break;
                case WhileAst whileAst:
                    BuildAllocations(whileAst.Condition);
                    return BuildAllocations(whileAst.Block.Children);
                case EachAst each:
                    var indexVariable = _builder.BuildAlloca(LLVM.Int32Type(), each.IterationVariable);
                    _allocationQueue.Enqueue(indexVariable);

                    switch (each.Iteration)
                    {
                        // To get the field of a call, the value needs to be stored on the stack to use GetElementPtr
                        // @PotentialBug I can't really think of other cases that would fall under here, but this may
                        // become an issue if there are some new ways to create arrays
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
                case ExpressionAst expression:
                    BuildAllocations(expression.Children);
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
                var arrayData = _builder.BuildAlloca(arrayType, "arraydata");
                _allocationQueue.Enqueue(arrayData);
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
                else if (field.Type.TypeKind == TypeKind.Array && !field.Type.CArray && field.Type.Count != null)
                {
                    var elementType = field.Type.Generics[0];
                    var targetType = ConvertTypeDefinition(elementType);

                    var count = field.Type.ConstCount.Value;
                    var arrayType = LLVM.ArrayType(targetType, count);
                    var arrayData = _builder.BuildAlloca(arrayType, "arraydata");
                    _allocationQueue.Enqueue(arrayData);
                }
                else if (field.Type.TypeKind == TypeKind.Struct)
                {
                    BuildStructAllocations(field.Type.GenericName);
                }
            }
        }

        private bool WriteFunctionLine(IAst ast, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function, LLVMMetadataRef block)
        {
            if (_emitDebug)
            {
                LLVM.SetCurrentDebugLocation2(_builder, LLVM.DIBuilderCreateDebugLocation(_context, ast.Line, ast.Column, block, null));
            }

            switch (ast)
            {
                case ReturnAst returnAst:
                    WriteReturnStatement(returnAst, localVariables);
                    return true;
                case DeclarationAst declaration:
                    WriteDeclaration(declaration, localVariables, block);
                    break;
                case AssignmentAst assignment:
                    WriteAssignment(assignment, localVariables);
                    break;
                case ScopeAst scope:
                    return WriteScope(scope, localVariables, function, block);
                case ConditionalAst conditional:
                    return WriteConditional(conditional, localVariables, function, block);
                case WhileAst whileAst:
                    return WriteWhile(whileAst, localVariables, function, block);
                case EachAst each:
                    return WriteEach(each, localVariables, function, block);
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

        private void WriteDeclaration(DeclarationAst declaration, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMMetadataRef block)
        {
            // 1. Declare variable on the stack
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
            if (_emitDebug)
            {
                DeclareDebugVariable(declaration.Name, declaration.Type, declaration, variable, block);
            }

            // 2. Set value if it exists
            if (declaration.Value != null)
            {
                var expression = WriteExpression(declaration.Value, localVariables);
                var value = CastValue(expression, declaration.Type);

                LLVM.BuildStore(_builder, value, variable);
                return;
            }

            switch (declaration.Type.TypeKind)
            {
                // Initialize arrays
                case TypeKind.Array:
                    var elementType = declaration.Type.Generics[0];
                    if (declaration.Type.CArray)
                    {
                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(variable, elementType, declaration.ArrayValues, localVariables);
                        }
                        return;
                    }
                    if (declaration.Type.ConstCount != null)
                    {
                        var arrayPointer = InitializeConstArray(variable, declaration.Type.ConstCount.Value, elementType);

                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(arrayPointer, elementType, declaration.ArrayValues, localVariables);
                        }
                    }
                    else if (declaration.Type.Count != null)
                    {
                        BuildStackSave();
                        var (_, count) = WriteExpression(declaration.Type.Count, localVariables);

                        var countPointer = _builder.BuildStructGEP(variable, 0, "countptr");
                        LLVM.BuildStore(_builder, count, countPointer);

                        var targetType = ConvertTypeDefinition(elementType);
                        var arrayData = _builder.BuildArrayAlloca(targetType, count, "arraydata");
                        var dataPointer = _builder.BuildStructGEP(variable, 1, "dataptr");
                        LLVM.BuildStore(_builder, arrayData, dataPointer);
                    }
                    else
                    {
                        var countPointer = _builder.BuildStructGEP(variable, 0, "countptr");
                        LLVM.BuildStore(_builder, _zeroInt, countPointer);
                    }
                    break;
                // Initialize struct field default values
                case TypeKind.Struct:
                case TypeKind.String:
                    InitializeStruct(declaration.Type, variable, localVariables, declaration.Assignments);
                    break;
                // Initialize pointers to null
                case TypeKind.Pointer:
                    var nullValue = LLVM.ConstNull(ConvertTypeDefinition(declaration.Type));
                    LLVM.BuildStore(_builder, nullValue, variable);
                    break;
                // Or initialize to 0
                default:
                    var zero = GetConstZero(ConvertTypeDefinition(declaration.Type));
                    LLVM.BuildStore(_builder, zero, variable);
                    break;
            }
        }

        private void InitializeArrayValues(LLVMValueRef arrayPointer, TypeDefinition elementType, List<IAst> arrayValues, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
        {
            for (var i = 0; i < arrayValues.Count; i++)
            {
                var value = WriteExpression(arrayValues[i], localVariables);

                var index = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)i, false);
                var pointer = _builder.BuildGEP(arrayPointer, new []{_zeroInt, index}, "dataptr");

                LLVM.BuildStore(_builder, CastValue(value, elementType), pointer);
            }
        }

        private void InitializeStruct(TypeDefinition typeDef, LLVMValueRef variable, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, List<AssignmentAst> values)
        {
            var assignments = values?.ToDictionary(_ => (_.Reference as IdentifierAst)!.Name);
            var structDef = _programGraph.Types[typeDef.GenericName] as StructAst;
            for (var i = 0; i < structDef!.Fields.Count; i++)
            {
                var field = structDef.Fields[i];

                var fieldPointer = _builder.BuildStructGEP(variable, (uint)i, field.Name);

                if (assignments != null && assignments.TryGetValue(field.Name, out var assignment))
                {
                    var expression = WriteExpression(assignment.Value, localVariables);
                    var value = CastValue(expression, field.Type);

                    LLVM.BuildStore(_builder, value, fieldPointer);
                }
                else
                {
                    switch (field.Type.TypeKind)
                    {
                        case TypeKind.Array:
                            if (field.Type.CArray) continue;
                            if (field.Type.ConstCount != null)
                            {
                                InitializeConstArray(fieldPointer, field.Type.ConstCount.Value, field.Type.Generics[0]);
                            }
                            else
                            {
                                var countPointer = _builder.BuildStructGEP(fieldPointer, 0, "countptr");
                                LLVM.BuildStore(_builder, _zeroInt, countPointer);
                            }
                            break;
                        case TypeKind.Pointer:
                            var type = ConvertTypeDefinition(field.Type);
                            LLVM.BuildStore(_builder, LLVM.ConstNull(type), fieldPointer);
                            break;
                        case TypeKind.Struct:
                            InitializeStruct(field.Type, fieldPointer, localVariables, field.Assignments);
                            break;
                        default:
                            LLVMValueRef value;
                            if (field.Value != null)
                            {
                                (_, value) = WriteExpression(field.Value, localVariables);
                            }
                            else
                            {
                                var fieldType = ConvertTypeDefinition(field.Type);
                                value = GetConstZero(fieldType);
                            }
                            LLVM.BuildStore(_builder, value, fieldPointer);
                            break;
                    }
                }
            }
        }

        private LLVMValueRef InitializeConstArray(LLVMValueRef array, uint length, TypeDefinition arrayType)
        {
            // 1. Set the count field
            var countValue = LLVM.ConstInt(LLVM.Int32Type(), (ulong)length, 0);
            var countPointer = _builder.BuildStructGEP(array, 0, "countptr");
            LLVM.BuildStore(_builder, countValue, countPointer);

            // 2. Initialize the array data
            var targetType = ConvertTypeDefinition(arrayType);
            var arrayData = _allocationQueue.Dequeue();
            var arrayDataPointer = _builder.BuildBitCast(arrayData, LLVM.PointerType(targetType, 0), "tmpdata");
            var dataPointer = _builder.BuildStructGEP(array, 1, "dataptr");
            LLVM.BuildStore(_builder, arrayDataPointer, dataPointer);

            return arrayData;
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
                IndexAst index => GetIndexPointer(index, localVariables, out loaded),
                UnaryAst unary => WriteExpression(unary.Value, localVariables),
                // @Cleanup This branch should never be hit
                _ => (null, new LLVMValueRef())
            };
            if (loaded && type.TypeKind == TypeKind.Pointer)
            {
                type = type.Generics[0];
            }
            else if (assignment.Reference is UnaryAst)
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

        private bool WriteScope(ScopeAst scope, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function, LLVMMetadataRef block)
        {
            if (!scope.Children.Any()) return false;

            // 1. Create scope variables
            var scopeVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(localVariables);

            // 2. Write function lines
            if (_emitDebug)
            {
                var file = _debugFiles[scope.FileIndex];
                block = LLVM.DIBuilderCreateLexicalBlock(_debugBuilder, block, file, scope.Line, scope.Column);
            }
            foreach (var ast in scope.Children)
            {
                if (WriteFunctionLine(ast, scopeVariables, function, block))
                {
                    return true;
                }
            }
            return false;
        }

        private bool WriteConditional(ConditionalAst conditional, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function, LLVMMetadataRef block)
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
            var ifReturned = WriteScope(conditional.IfBlock, localVariables, function, block);

            if (!ifReturned)
            {
                if (conditional.ElseBlock == null)
                {
                    LLVM.BuildBr(_builder, elseBlock);
                    LLVM.PositionBuilderAtEnd(_builder, elseBlock);
                    return false;
                }
                endBlock = function.AppendBasicBlock("ifcont");
                LLVM.BuildBr(_builder, endBlock);
            }

            LLVM.PositionBuilderAtEnd(_builder, elseBlock);

            if (conditional.ElseBlock == null)
            {
                return false;
            }

            // 4. Write out the else if necessary
            var elseReturned = WriteScope(conditional.ElseBlock, localVariables, function, block);

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

        private bool WriteWhile(WhileAst whileAst, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function, LLVMMetadataRef block)
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
            if (WriteScope(whileAst.Block, localVariables, function, block))
            {
                LLVM.DeleteBasicBlock(afterWhile);
                return true;
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

        private bool WriteEach(EachAst each, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, LLVMValueRef function, LLVMMetadataRef block)
        {
            var eachVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(localVariables);
            if (_emitDebug)
            {
                var file = _debugFiles[each.FileIndex];
                block = LLVM.DIBuilderCreateLexicalBlock(_debugBuilder, block, file, each.Line, each.Column);
                LLVM.SetCurrentDebugLocation2(_builder, LLVM.DIBuilderCreateDebugLocation(_context, each.Line, each.Column, block, null));
            }

            // 1. Initialize each values
            var indexVariable = _allocationQueue.Dequeue();
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
                    (iterationType, iterationValue) = GetIndexPointer(index, localVariables, out _);
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
            var arrayData = new LLVMValueRef();
            var compareTarget = new LLVMValueRef();
            if (each.Iteration != null)
            {
                if (each.IndexVariable != null)
                {
                    eachVariables.Add(each.IndexVariable, (_s32Type, indexVariable));
                    if (_emitDebug)
                    {
                        DeclareDebugVariable(each.IndexVariable, _s32Type, each, indexVariable, block);
                    }
                }
                LLVM.BuildStore(_builder, _zeroInt, indexVariable);

                // Load the array data and set the compareTarget to the array count
                if (iterationType.CArray)
                {
                    arrayData = iterationValue;
                    compareTarget = WriteExpression(iterationType.Count, localVariables).value;
                }
                else
                {
                    var dataPointer = _builder.BuildStructGEP(iterationValue, 1, "dataptr");
                    arrayData = _builder.BuildLoad(dataPointer, "data");

                    var lengthPointer= _builder.BuildStructGEP(iterationValue, 0, "lengthptr");
                    compareTarget = _builder.BuildLoad(lengthPointer, "length");
                }
            }
            else
            {
                // Begin the loop at the beginning of the range
                var (type, value) = WriteExpression(each.RangeBegin, localVariables);
                if (_emitDebug)
                {
                    DeclareDebugVariable(each.IterationVariable, type, each, indexVariable, block);
                }

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
            var condition = _builder.BuildICmp(each.Iteration == null ? LLVMIntPredicate.LLVMIntSLE : LLVMIntPredicate.LLVMIntSLT, indexValue, compareTarget, "arraycmp");
            if (each.Iteration != null)
            {
                var pointerIndices = iterationType.CArray ? new []{_zeroInt, indexValue} : new []{indexValue};
                var iterationVariable = _builder.BuildGEP(arrayData, pointerIndices, each.IterationVariable);
                eachVariables.TryAdd(each.IterationVariable, (each.IteratorType, iterationVariable));

                if (_emitDebug)
                {
                    DeclareDebugVariable(each.IterationVariable, each.IteratorType, each, iterationVariable, block);
                }
            }

            var eachBody = function.AppendBasicBlock("eachbody");
            var afterEach = function.AppendBasicBlock("aftereach");
            LLVM.BuildCondBr(_builder, condition, eachBody, afterEach);

            // 5. Write out each loop body
            LLVM.PositionBuilderAtEnd(_builder, eachBody);
            foreach (var ast in each.Children)
            {
                if (WriteFunctionLine(ast, eachVariables, function, block))
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

        private void DeclareDebugVariable(string variableName, TypeDefinition type, IAst ast, LLVMValueRef variable, LLVMMetadataRef block)
        {
            using var name = new MarshaledString(variableName);

            var file = _debugFiles[ast.FileIndex];
            var debugVariable = LLVM.DIBuilderCreateAutoVariable(_debugBuilder, block, name.Value, (UIntPtr)name.Length, file, ast.Line, GetDebugType(type), 0, LLVMDIFlags.LLVMDIFlagZero, 0);
            var expression = LLVM.DIBuilderCreateExpression(_debugBuilder, null, (UIntPtr)0);
            var location = LLVM.GetCurrentDebugLocation2(_builder);

            LLVM.DIBuilderInsertDeclareAtEnd(_debugBuilder, variable, debugVariable, expression, location, _builder.InsertBlock);
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
                        return (_s32Type, LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)typeDef.TypeIndex, false));
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
                            callArguments[i] = CastValue(value, functionDef.Arguments[i].Type);
                        }

                        // Rollup the rest of the arguments into an array
                        var paramsType = functionDef.Arguments[^1].Type.Generics[0];
                        var paramsPointer = _allocationQueue.Dequeue();
                        InitializeConstArray(paramsPointer, (uint)(call.Arguments.Count - functionDef.Arguments.Count + 1), paramsType);

                        var arrayData = _builder.BuildStructGEP(paramsPointer, 1, "arraydata");
                        var dataPointer = _builder.BuildLoad(arrayData, "dataptr");

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
                            var value = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                            callArguments[i] = CastValue(value, functionDef.Arguments[i].Type);
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
                            var value = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                            callArguments[i] = CastValue(value, functionDef.Arguments[i].Type);
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
                        IndexAst index => GetIndexPointer(index, localVariables, out _),
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
                            IndexAst index => GetIndexPointer(index, localVariables, out _),
                            // @Cleanup this branch should not be hit
                            _ => (null, new LLVMValueRef())
                        };
                        var pointerType = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer};
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
                    var (elementType, elementValue) = GetIndexPointer(index, localVariables, out var loaded);
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
                    return (_s32Type, LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)type.TypeIndex, false));
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
            _builder.BuildCall(function, new []{stackPointer}, string.Empty);
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

            switch (constant.Type.TypeKind)
            {
                case TypeKind.Boolean:
                    return LLVMValueRef.CreateConstInt(type, constant.Value == "true" ? (ulong)1 : 0, false);
                case TypeKind.String:
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
                    var (indexType, indexValue) = GetIndexPointer(index, localVariables, out _);
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
                                type = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer, Generics = {type.Generics[0]}};
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
                        (type, value) = GetIndexPointer(index, localVariables, out _, type, value);

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

        private (TypeDefinition type, LLVMValueRef value) GetIndexPointer(IndexAst index, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, out bool loaded, TypeDefinition type = null, LLVMValueRef variable = default)
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
            LLVMValueRef indexPointer;
            if (type.TypeKind == TypeKind.Pointer)
            {
                var dataPointer = _builder.BuildLoad(variable, "dataptr");
                indexPointer = _builder.BuildGEP(dataPointer, new []{indexValue}, "indexptr");
            }
            else if (type.CArray)
            {
                indexPointer = _builder.BuildGEP(variable, new []{_zeroInt, indexValue}, "dataptr");
            }
            else
            {
                var arrayData = _builder.BuildStructGEP(variable, 1, "arraydata");
                var dataPointer = _builder.BuildLoad(arrayData, "dataptr");
                indexPointer = _builder.BuildGEP(dataPointer, new [] {indexValue}, "indexptr");
            }
            loaded = false;
            return (elementType, indexPointer);
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

        private LLVMValueRef CastValue((TypeDefinition type, LLVMValueRef value) typeValue, TypeDefinition targetType, bool checkType = true)
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

            if (targetType.TypeKind == TypeKind.Pointer)
            {
                var pointerType = ConvertTypeDefinition(targetType);
                return _builder.BuildBitCast(value, pointerType, "ptr");
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
            if (type.TypeKind == TypeKind.Pointer)
            {
                return LLVM.PointerType(ConvertTypeDefinition(type.Generics[0], externFunction, true), 0);
            }

            return type.PrimitiveType switch
            {
                IntegerType integerType => GetIntegerType(integerType),
                FloatType floatType => floatType.Bytes == 8 ? LLVM.DoubleType() : LLVM.FloatType(),
                EnumType enumType => GetIntegerType(enumType),
                _ => type.TypeKind switch
                {
                    TypeKind.Boolean => LLVM.Int1Type(),
                    TypeKind.Void => pointer ? LLVM.Int8Type() : LLVM.VoidType(),
                    TypeKind.Array or TypeKind.Params => GetArrayType(type),
                    TypeKind.String => externFunction ? LLVM.PointerType(LLVM.Int8Type(), 0) : _module.GetTypeByName("string"),
                    TypeKind.Type => LLVM.Int32Type(),
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

        private LLVMTypeRef GetArrayType(TypeDefinition type)
        {
            var elementTypeDef = type.Generics[0];

            if (type.CArray)
            {
                var elementType = ConvertTypeDefinition(elementTypeDef);
                return LLVM.ArrayType(elementType, type.ConstCount.Value);
            }

            return _module.GetTypeByName($"Array.{elementTypeDef.GenericName}");
        }

        private LLVMTypeRef GetStructType(TypeDefinition type)
        {
            if (_programGraph.Types.TryGetValue(type.Name, out var typeDef) && typeDef is EnumAst)
            {
                return LLVM.Int32Type();
            }

            return _module.GetTypeByName(type.GenericName);
        }


        private LLVMMetadataRef GetDebugType(TypeDefinition type)
        {
            return type.TypeKind switch
            {
                TypeKind.Params => _debugTypes[$"Array.{type.Generics[0].GenericName}"],
                TypeKind.Type => _debugTypes["s32"],
                _ => _debugTypes[type.GenericName]
            };
        }

        private void CreateDebugStructType(StructAst structAst, string name)
        {
            using var structName = new MarshaledString(structAst.Name);

            var file = _debugFiles[structAst.FileIndex];
            var fields = new LLVMMetadataRef[structAst.Fields.Count];

            if (fields.Length > 0)
            {
                var structDecl = _debugTypes[name];
                for (var i = 0; i < fields.Length; i++)
                {
                    var structField = structAst.Fields[i];
                    using var fieldName = new MarshaledString(structField.Name);

                    fields[i] = LLVM.DIBuilderCreateMemberType(_debugBuilder, structDecl, fieldName.Value, (UIntPtr)fieldName.Length, file, structField.Line, structField.Size * 8, 0, structField.Offset * 8, LLVMDIFlags.LLVMDIFlagZero, GetDebugType(structField.Type));
                }
            }

            fixed (LLVMMetadataRef* fieldsPointer = fields)
            {
                _debugTypes[name] = LLVM.DIBuilderCreateStructType(_debugBuilder, null, structName.Value, (UIntPtr)structName.Length, file, structAst.Line, structAst.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)fieldsPointer, (uint)fields.Length, 0, null, null, (UIntPtr)0);
            }
        }

        private void CreateDebugEnumType(EnumAst enumAst)
        {
            using var enumName = new MarshaledString(enumAst.Name);

            var file = _debugFiles[enumAst.FileIndex];
            var enumValues = new LLVMMetadataRef[enumAst.Values.Count];
            var isUnsigned = enumAst.BaseType.PrimitiveType.Signed ? 0 : 1;

            for (var i = 0; i < enumValues.Length; i++)
            {
                var enumValue = enumAst.Values[i];
                using var valueName = new MarshaledString(enumValue.Name);

                enumValues[i] = LLVM.DIBuilderCreateEnumerator(_debugBuilder, valueName.Value, (UIntPtr)valueName.Length, enumValue.Value, isUnsigned);
            }

            fixed (LLVMMetadataRef* enumValuesPointer = enumValues)
            {
                _debugTypes[enumAst.Name] = LLVM.DIBuilderCreateEnumerationType(_debugBuilder, null, enumName.Value, (UIntPtr)enumName.Length, file, enumAst.Line, (uint)enumAst.BaseType.PrimitiveType.Bytes * 8, 0, (LLVMOpaqueMetadata**)enumValuesPointer, (uint)enumValues.Length, GetDebugType(enumAst.BaseType));
            }
        }

        private void CreateDebugBasicType(PrimitiveAst type, string typeName)
        {
            using var name = new MarshaledString(type.Name);
            switch (type.TypeKind)
            {
                case TypeKind.Void:
                    _debugTypes[type.Name] = null;
                    break;
                case TypeKind.Boolean:
                    _debugTypes[type.Name] = LLVM.DIBuilderCreateBasicType(_debugBuilder, name.Value, (UIntPtr)name.Length, 8, (uint)DwarfTypeEncoding.Boolean, LLVMDIFlags.LLVMDIFlagZero);
                    break;
                case TypeKind.Integer:
                    var encoding = type.Primitive.Signed ? DwarfTypeEncoding.Signed : DwarfTypeEncoding.Unsigned;
                    _debugTypes[type.Name] = LLVM.DIBuilderCreateBasicType(_debugBuilder, name.Value, (UIntPtr)name.Length, (uint)type.Primitive.Bytes * 8, (uint)encoding, LLVMDIFlags.LLVMDIFlagZero);
                    break;
                case TypeKind.Float:
                    _debugTypes[type.Name] = LLVM.DIBuilderCreateBasicType(_debugBuilder, name.Value, (UIntPtr)name.Length, (uint)type.Primitive.Bytes * 8, (uint)DwarfTypeEncoding.Float, LLVMDIFlags.LLVMDIFlagZero);
                    break;
                case TypeKind.Pointer:
                    var pointerType = _debugTypes[type.PointerType.GenericName];
                    _debugTypes[typeName] = LLVM.DIBuilderCreatePointerType(_debugBuilder, pointerType, 64, 0, 0, name.Value, (UIntPtr)name.Length);
                    break;
            }
        }

        private enum DwarfTag : uint
        {
            Lexical_block = 0x0b,
            Compile_unit = 0x11,
            Variable = 0x34,
            Base_type = 0x24,
            Pointer_type = 0x0F,
            Structure_type = 0x13,
            Subroutine_type = 0x15,
            File_type = 0x29,
            Subprogram = 0x2E,
            Auto_variable = 0x100,
            Arg_variable = 0x101
        }

        private enum DwarfTypeEncoding : uint
        {
            Address = 0x01,
            Boolean = 0x02,
            Complex_float = 0x03,
            Float = 0x04,
            Signed = 0x05,
            Signed_char = 0x06,
            Unsigned = 0x07,
            Unsigned_char = 0x08,
            Imaginary_float = 0x09,
            Packed_decimal = 0x0a,
            Numeric_string = 0x0b,
            Edited = 0x0c,
            Signed_fixed = 0x0d,
            Unsigned_fixed = 0x0e,
            Decimal_float = 0x0f,
            UTF = 0x10,
            Lo_user = 0x80,
            Hi_user = 0xff
        }
    }
}
