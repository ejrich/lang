using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LLVMSharp.Interop;

namespace ol;

public static unsafe class LLVMBackend
{
    private const string ObjectDirectory = "obj";

    private static LLVMModuleRef _module;
    private static LLVMContextRef _context;
    private static LLVMBuilderRef _builder;
    private static LLVMPassManagerRef _passManager;
    private static LLVMCodeGenOptLevel _codeGenLevel;

    private static LLVMTypeRef[] _types;
    private static LLVMTypeRef[] _functionTypes;
    private static LLVMValueRef[] _typeInfos;
    private static LLVMValueRef[] _globals;
    private static LLVMValueRef[] _functions;
    private static LLVMValueRef[] _fileNames;
    private static Queue<(LLVMValueRef, FunctionIR)> _functionsToWrite;

    private static LLVMTypeRef _structTypeInfoType;
    private static LLVMTypeRef _typeInfoType;
    private static LLVMTypeRef _typeInfoPointerType;
    private static LLVMTypeRef _typeInfoArrayType;
    private static LLVMTypeRef _integerTypeInfoType;
    private static LLVMTypeRef _pointerTypeInfoType;
    private static LLVMTypeRef _arrayTypeInfoType;
    private static LLVMTypeRef _enumTypeInfoType;
    private static LLVMTypeRef _compoundTypeInfoType;
    private static LLVMTypeRef _stringType;
    private static LLVMTypeRef _stringArrayType;
    private static LLVMTypeRef _enumValueType;
    private static LLVMTypeRef _enumValueArrayType;
    private static LLVMTypeRef _unionTypeInfoType;
    private static LLVMTypeRef _unionFieldType;
    private static LLVMTypeRef _unionFieldArrayType;
    private static LLVMTypeRef _interfaceTypeInfoType;
    private static LLVMTypeRef _typeFieldType;
    private static LLVMTypeRef _typeFieldArrayType;
    private static LLVMTypeRef _functionTypeInfoType;
    private static LLVMTypeRef _argumentType;
    private static LLVMTypeRef _argumentArrayType;

    private static LLVMValueRef _defaultAttributes;
    private static LLVMValueRef _defaultFields;

    private static bool _emitDebug;
    private static LLVMDIBuilderRef _debugBuilder;
    private static LLVMMetadataRef _debugCompilationUnit;
    private static LLVMMetadataRef[] _debugFiles;
    private static LLVMMetadataRef[] _debugTypes;
    private static LLVMMetadataRef[] _debugFunctions;

    private static readonly LLVMTypeRef _u8PointerType = LLVM.PointerType(LLVM.Int8Type(), 0);
    private static readonly LLVMValueRef _zeroInt = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), 0);
    private static readonly LLVMValueRef _interfaceTypeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Interface, 0);
    private static readonly LLVMValueRef _functionTypeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Function, 0);

    public static string Build()
    {
        // 1. Verify obj directory exists
        var objectPath = Path.Combine(BuildSettings.Path, ObjectDirectory);
        if (!Directory.Exists(objectPath))
            Directory.CreateDirectory(objectPath);

        // 2. Initialize the LLVM module and builder
        _module = LLVMModuleRef.CreateWithName(BuildSettings.Name);
        _context = _module.Context;
        _builder = LLVMBuilderRef.Create(_context);
        _passManager = _module.CreateFunctionPassManager();
        if (BuildSettings.Release)
        {
            LLVM.AddBasicAliasAnalysisPass(_passManager);
            LLVM.AddPromoteMemoryToRegisterPass(_passManager);
            LLVM.AddInstructionCombiningPass(_passManager);
            LLVM.AddReassociatePass(_passManager);
            LLVM.AddGVNPass(_passManager);
            LLVM.AddCFGSimplificationPass(_passManager);
            LLVM.AddLoopVectorizePass(_passManager);
            LLVM.AddSLPVectorizePass(_passManager);

            LLVM.InitializeFunctionPassManager(_passManager);
            _codeGenLevel = LLVMCodeGenOptLevel.LLVMCodeGenLevelAggressive;
        }
        else
        {
            _emitDebug = true;
            _debugBuilder = _module.CreateDIBuilder();
            _debugFiles = BuildSettings.Files.Select(file => _debugBuilder.CreateFile(Path.GetFileName(file), Path.GetDirectoryName(file))).ToArray();
            _debugCompilationUnit = _debugBuilder.CreateCompileUnit(LLVMDWARFSourceLanguage.LLVMDWARFSourceLanguageC, _debugFiles[0], "ol", 0, string.Empty, 0, string.Empty, LLVMDWARFEmissionKind.LLVMDWARFEmissionFull, 0, 0, 0, string.Empty, string.Empty);

            #if _LINUX
            AddModuleFlag("Dwarf Version", 4);
            #elif _WINDOWS
            AddModuleFlag("CodeView", 1);
            AddModuleFlag("uwtable", 1);
            #endif
            AddModuleFlag("Debug Info Version", LLVM.DebugMetadataVersion());
            AddModuleFlag("PIE Level", 2);

            _debugTypes = new LLVMMetadataRef[TypeTable.Count];
            _debugFunctions = new LLVMMetadataRef[TypeTable.FunctionCount];
        }

        // 3. Declare types
        _types = new LLVMTypeRef[TypeTable.Count];
        _typeInfos = new LLVMValueRef[TypeTable.Count];

        _structTypeInfoType = CreateStruct("StructTypeInfo");
        _typeInfoType = CreateStruct("TypeInfo");
        _typeInfoArrayType = CreateStruct("Array.*.TypeInfo");
        _integerTypeInfoType = CreateStruct("IntegerTypeInfo");
        _pointerTypeInfoType = CreateStruct("PointerTypeInfo");
        _arrayTypeInfoType = CreateStruct("CArrayTypeInfo");
        _enumTypeInfoType = CreateStruct("EnumTypeInfo");
        _compoundTypeInfoType = CreateStruct("CompoundTypeInfo");
        _stringType = CreateStruct("string");
        _stringArrayType = CreateStruct("Array.string");
        _enumValueType = CreateStruct("EnumValue");
        _enumValueArrayType = CreateStruct("Array.EnumValue");
        _unionTypeInfoType = CreateStruct("UnionTypeInfo");
        _unionFieldType = CreateStruct("UnionField");
        _unionFieldArrayType = CreateStruct("Array.UnionField");
        _interfaceTypeInfoType = CreateStruct("InterfaceTypeInfo");
        _typeFieldType = CreateStruct("TypeField");
        _typeFieldArrayType = CreateStruct("Array.TypeField");
        _functionTypeInfoType = CreateStruct("FunctionTypeInfo");
        _argumentType = CreateStruct("ArgumentType");
        _argumentArrayType = CreateStruct("Array.ArgumentType");

        _typeInfoPointerType = LLVM.PointerType(_typeInfoType, 0);
        _defaultAttributes = LLVMValueRef.CreateConstNamedStruct(_stringArrayType, stackalloc LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(_stringType, 0))});
        _defaultFields = LLVMValueRef.CreateConstNamedStruct(_typeFieldArrayType, stackalloc LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(_typeFieldType, 0))});

        switch (BuildSettings.OutputTypeTable)
        {
            case OutputTypeTableConfiguration.Full:
            {
                // Define types and typeinfos
                DeclareAllTypesAndTypeInfos();
                break;
            }
            case OutputTypeTableConfiguration.Used:
            {
                // Define types and used type infos
                DeclareAllTypesAndUsedTypeInfos();
                // Types that are not marked as used will have type infos written when the type is requested
                break;
            }
            case OutputTypeTableConfiguration.None:
            {
                // Define types
                DeclareAllTypes();
                break;
            }
        }

        // 4. Declare global variables
        LLVMValueRef typeTable = null;
        _globals = new LLVMValueRef[Program.GlobalVariables.Count];
        for (var i = 0; i < Program.GlobalVariables.Count; i++)
        {
            var globalVariable = Program.GlobalVariables[i];
            LLVMValueRef global;
            if (globalVariable.Array)
            {
                var elementType = _types[globalVariable.Type.TypeIndex];
                global = AddGlobal(LLVM.ArrayType(elementType, globalVariable.ArrayLength), globalVariable.Name);

                if (globalVariable.InitialValue == null)
                {
                    var defaultValue = GetDefaultValue(globalVariable.Type);
                    var values = new LLVMValueRef[globalVariable.ArrayLength];
                    for (var j = 0; j < values.Length; j++)
                    {
                        values[j] = defaultValue;
                    }
                    var constArray = LLVMValueRef.CreateConstArray(elementType, values);
                    LLVM.SetInitializer(global, constArray);
                }
            }
            else
            {
                global = AddGlobal(_types[globalVariable.Type.TypeIndex], globalVariable.Name);
            }

            if (globalVariable.InitialValue != null)
            {
                var initialValue = GetConstantValue(globalVariable.InitialValue);
                LLVM.SetInitializer(global, initialValue);
            }

            if (_emitDebug && globalVariable.FileIndex.HasValue)
            {
                DeclareDebugGlobal(globalVariable, global);
            }

            LLVM.SetLinkage(global, LLVMLinkage.LLVMPrivateLinkage);
            _globals[i] = global;

            if (globalVariable.Name.Compare("__type_table"))
            {
                typeTable = global;
                SetPrivateConstant(typeTable);
            }
        }

        // 5. Write the program beginning at the entrypoint
        _functionTypes = new LLVMTypeRef[TypeTable.FunctionCount];
        _functions = new LLVMValueRef[TypeTable.FunctionCount];
        _fileNames = new LLVMValueRef[BuildSettings.Files.Count];
        _functionsToWrite = new();
        WriteFunctionDefinition("__start", Program.EntryPoint);
        while (_functionsToWrite.Any())
        {
            var (functionPointer, function) = _functionsToWrite.Dequeue();
            WriteFunction(functionPointer, function);
        }

        // 6. Write type table
        var typeArray = CreateConstantArray(_typeInfoPointerType, _typeInfoArrayType, _typeInfos, "____type_array");
        LLVM.SetInitializer(typeTable, typeArray);

        // 7. Compile to object file
        var baseFileName = Path.Combine(objectPath, BuildSettings.Name);
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

        if (BuildSettings.OutputAssembly)
        {
            using var test = new MarshaledString("test");
            using var intelString = new MarshaledString("--x86-asm-syntax=intel");
            var args = stackalloc [] {test.Value, intelString.Value};
            LLVM.ParseCommandLineOptions(2, args, null);
        }

        var targetMachine = target.CreateTargetMachine(defaultTriple, "generic", string.Empty, _codeGenLevel, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);

        if (BuildSettings.OutputAssembly)
        {
            var llvmIrFile = $"{baseFileName}.ll";
            _module.PrintToFile(llvmIrFile);

            var assemblyFile = $"{baseFileName}.s";
            targetMachine.TryEmitToFile(_module, assemblyFile, LLVMCodeGenFileType.LLVMAssemblyFile, out _);
        }

        #if _LINUX
        var objectFile = $"{baseFileName}.o";
        #elif _WINDOWS
        var objectFile = $"{baseFileName}.obj";
        #endif

        if (!targetMachine.TryEmitToFile(_module, objectFile, LLVMCodeGenFileType.LLVMObjectFile, out var errorMessage))
        {
            Console.WriteLine($"LLVM Build error: {errorMessage}");
            Environment.Exit(ErrorCodes.BuildError);
        }

        return objectFile;
    }

    private static LLVMValueRef AddGlobal(LLVMTypeRef type, String name)
    {
        var pointer = stackalloc sbyte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            pointer[i] = (sbyte)name[i];
        }
        pointer[name.Length] = 0;
        return LLVM.AddGlobal(_module, type, pointer);
    }

    private static void DeclareDebugGlobal(GlobalVariable globalVariable, LLVMValueRef global)
    {
        var file = _debugFiles[globalVariable.FileIndex.Value];
        var debugType = _debugTypes[globalVariable.Type.TypeIndex];
        var pointer = stackalloc sbyte[globalVariable.Name.Length];
        for (var i = 0; i < globalVariable.Name.Length; i++)
        {
            pointer[i] = (sbyte)globalVariable.Name[i];
        }
        var globalDebug = LLVM.DIBuilderCreateGlobalVariableExpression(_debugBuilder, _debugCompilationUnit, pointer, globalVariable.Name, null, UIntPtr.Zero, file, globalVariable.Line, debugType, 0, null, null, 0);
        LLVM.GlobalSetMetadata(global, 0, globalDebug);
    }

    private static void AddModuleFlag(string flagName, uint flagValue)
    {
        using var name = new MarshaledString(flagName);
        var value = LLVM.ValueAsMetadata(LLVM.ConstInt(LLVM.Int32Type(), flagValue, 0));
        LLVM.AddModuleFlag(_module, LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, name.Value, (UIntPtr)name.Length, value);
    }

    private static LLVMTypeRef CreateStruct(string typeName)
    {
        var typeInfo = TypeChecker.GlobalScope.Types[typeName];
        var typeStruct = _types[typeInfo.TypeIndex] = CreateNamedStruct(typeInfo.Name);

        if (_emitDebug)
        {
            CreateTemporaryDebugStructType((StructAst)typeInfo);
        }
        return typeStruct;
    }

    private static LLVMTypeRef CreateNamedStruct(ReadOnlySpan<char> name)
    {
        var pointer = stackalloc sbyte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            pointer[i] = (sbyte)name[i];
        }
        pointer[name.Length] = 0;
        return LLVM.StructCreateNamed(_context, pointer);
    }

    private static void DeclareAllTypesAndTypeInfos()
    {
        var pointersToResolve = new List<PointerType>();
        var interfaceQueue = new List<InterfaceAst>();
        var structQueue = new List<StructAst>();
        var unionQueue = new List<UnionAst>();

        if (_emitDebug)
        {
            foreach (var type in TypeTable.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        if (_types[structAst.TypeIndex].Handle == IntPtr.Zero)
                        {
                            _types[structAst.TypeIndex] = _context.CreateNamedStruct(structAst.Name);
                        }
                        CreateTypeInfo(_structTypeInfoType, structAst.TypeIndex);
                        structQueue.Add(structAst);

                        if (structAst.Fields.Any())
                        {
                            CreateTemporaryDebugStructType(structAst);
                        }
                        else
                        {
                            CreateDebugStructType(structAst);
                        }
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst);
                        DeclareEnumTypeInfo(enumAst);
                        CreateDebugEnumType(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive);
                        DeclarePrimitiveTypeInfo(primitive);
                        CreateDebugBasicType(primitive);
                        break;
                    case PointerType pointerType:
                        DeclarePointerType(pointerType, pointersToResolve);
                        DeclarePointerTypeInfo(pointerType);
                        CreatePointerDebugType(pointerType);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType);
                        DeclareArrayTypeInfo(arrayType);
                        CreateArrayDebugType(arrayType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union);
                        CreateTypeInfo(_unionTypeInfoType, union.TypeIndex);
                        CreateTemporaryDebugUnionType(union);
                        unionQueue.Add(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundTypeAndTypeInfo(compoundType);
                        DeclareCompoundDebugType(compoundType);
                        break;
                    case InterfaceAst interfaceAst:
                        CreateTypeInfo(_interfaceTypeInfoType, interfaceAst.TypeIndex);
                        DeclareInterfaceDebugType(interfaceAst);
                        interfaceQueue.Add(interfaceAst);
                        break;
                    case FunctionAst function:
                        CreateFunctionTypeInfo(function);
                        break;
                }
            }
        }
        else
        {
            foreach (var type in TypeTable.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        if (_types[structAst.TypeIndex].Handle == IntPtr.Zero)
                        {
                            _types[structAst.TypeIndex] = CreateNamedStruct(structAst.Name);
                        }
                        CreateTypeInfo(_structTypeInfoType, structAst.TypeIndex);
                        structQueue.Add(structAst);
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst);
                        DeclareEnumTypeInfo(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive);
                        DeclarePrimitiveTypeInfo(primitive);
                        break;
                    case PointerType pointerType:
                        DeclarePointerType(pointerType, pointersToResolve);
                        DeclarePointerTypeInfo(pointerType);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType);
                        DeclareArrayTypeInfo(arrayType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union);
                        CreateTypeInfo(_unionTypeInfoType, union.TypeIndex);
                        unionQueue.Add(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundTypeAndTypeInfo(compoundType);
                        break;
                    case InterfaceAst interfaceAst:
                        CreateTypeInfo(_interfaceTypeInfoType, interfaceAst.TypeIndex);
                        interfaceQueue.Add(interfaceAst);
                        break;
                    case FunctionAst function:
                        CreateFunctionTypeInfo(function);
                        break;
                }
            }
        }

        foreach (var interfaceAst in interfaceQueue)
        {
            var argumentCount = interfaceAst.Arguments.Count;
            var argumentTypes = new LLVMTypeRef[argumentCount];
            var argumentValues = new LLVMValueRef[argumentCount];
            for (var arg = 0; arg < argumentCount; arg++)
            {
                var argument = interfaceAst.Arguments[arg];
                argumentTypes[arg] = _types[argument.Type.TypeIndex];
                var argumentTypeInfo = _typeInfos[argument.Type.TypeIndex];

                argumentValues[arg] = CreateArgumentType(argument, argumentTypeInfo);
            }

            var functionType = LLVMTypeRef.CreateFunction(_types[interfaceAst.ReturnType.TypeIndex], argumentTypes, false);
            _types[interfaceAst.TypeIndex] = LLVM.PointerType(functionType, 0);

            var returnType = _typeInfos[interfaceAst.ReturnType.TypeIndex];

            DeclareInterfaceTypeInfo(interfaceAst, argumentValues, returnType);
        }

        foreach (var pointerType in pointersToResolve)
        {
            _types[pointerType.TypeIndex] = LLVM.PointerType(_types[pointerType.PointedType.TypeIndex], 0);
        }

        foreach (var structAst in structQueue)
        {
            SetStructTypeFieldsAndTypeInfo(structAst);
        }

        if (_emitDebug)
        {
            foreach (var union in unionQueue)
            {
                DeclareUnionTypeInfo(union);
                DeclareUnionDebugType(union);
            }
        }
        else
        {
            foreach (var union in unionQueue)
            {
                DeclareUnionTypeInfo(union);
            }
        }
    }

    private static void DeclareAllTypesAndUsedTypeInfos()
    {
        var pointersToResolve = new List<PointerType>();
        var interfaceQueue = new List<InterfaceAst>();
        var structQueue = new List<StructAst>();
        var unionQueue = new List<UnionAst>();

        if (_emitDebug)
        {
            foreach (var type in TypeTable.Types)
            {
                if (type.Used)
                {
                    CreateTypeInfoIfNotExists(type);
                }
                switch (type)
                {
                    case StructAst structAst:
                        if (_types[structAst.TypeIndex].Handle == IntPtr.Zero)
                        {
                            _types[structAst.TypeIndex] = CreateNamedStruct(structAst.Name);
                        }

                        if (structAst.Fields.Any())
                        {
                            structQueue.Add(structAst);
                            CreateTemporaryDebugStructType(structAst);
                        }
                        else
                        {
                            CreateDebugStructType(structAst);
                        }
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst);
                        CreateDebugEnumType(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive);
                        CreateDebugBasicType(primitive);
                        break;
                    case PointerType pointerType:
                        DeclarePointerType(pointerType, pointersToResolve);
                        CreatePointerDebugType(pointerType);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType);
                        CreateArrayDebugType(arrayType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union);
                        CreateTemporaryDebugUnionType(union);
                        unionQueue.Add(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundType(compoundType);
                        DeclareCompoundDebugType(compoundType);
                        break;
                    case InterfaceAst interfaceAst:
                        DeclareInterfaceDebugType(interfaceAst);
                        interfaceQueue.Add(interfaceAst);
                        break;
                }
            }
        }
        else
        {
            foreach (var type in TypeTable.Types)
            {
                if (type.Used)
                {
                    CreateTypeInfoIfNotExists(type);
                }
                switch (type)
                {
                    case StructAst structAst:
                        if (_types[structAst.TypeIndex].Handle == IntPtr.Zero)
                        {
                            _types[structAst.TypeIndex] = CreateNamedStruct(structAst.Name);
                        }

                        if (structAst.Fields.Any())
                        {
                            structQueue.Add(structAst);
                        }
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive);
                        break;
                    case PointerType pointerType:
                        DeclarePointerType(pointerType, pointersToResolve);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundType(compoundType);
                        break;
                    case InterfaceAst interfaceAst:
                        interfaceQueue.Add(interfaceAst);
                        break;
                }
            }
        }

        foreach (var interfaceAst in interfaceQueue)
        {
            var argumentTypes = new LLVMTypeRef[interfaceAst.Arguments.Count];
            for (var arg = 0; arg < argumentTypes.Length; arg++)
            {
                var argument = interfaceAst.Arguments[arg];
                argumentTypes[arg] = _types[argument.Type.TypeIndex];
            }

            var functionType = LLVMTypeRef.CreateFunction(_types[interfaceAst.ReturnType.TypeIndex], argumentTypes, false);
            _types[interfaceAst.TypeIndex] = LLVM.PointerType(functionType, 0);
        }

        foreach (var pointerType in pointersToResolve)
        {
            _types[pointerType.TypeIndex] = LLVM.PointerType(_types[pointerType.PointedType.TypeIndex], 0);
        }

        foreach (var structAst in structQueue)
        {
            SetStructTypeFields(structAst);
        }

        foreach (var union in unionQueue)
        {
            DeclareUnionDebugType(union);
        }

        var nullTypeInfo = LLVM.ConstNull(_typeInfoPointerType);
        for (var i = 0; i < _typeInfos.Length; i++)
        {
            if (_typeInfos[i].Handle == IntPtr.Zero)
            {
                _typeInfos[i] = nullTypeInfo;
            }
        }
    }

    private static void DeclareAllTypes()
    {
        var nullTypeInfo = LLVM.ConstNull(_typeInfoPointerType);
        for (var i = 0; i < _typeInfos.Length; i++)
        {
            _typeInfos[i] = nullTypeInfo;
        }
        var pointersToResolve = new List<PointerType>();
        var interfaceQueue = new List<InterfaceAst>();
        var structQueue = new List<StructAst>();
        var unionQueue = new List<UnionAst>();

        if (_emitDebug)
        {
            foreach (var type in TypeTable.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        if (_types[structAst.TypeIndex].Handle == IntPtr.Zero)
                        {
                            _types[structAst.TypeIndex] = CreateNamedStruct(structAst.Name);
                        }

                        if (structAst.Fields.Any())
                        {
                            structQueue.Add(structAst);
                            CreateTemporaryDebugStructType(structAst);
                        }
                        else
                        {
                            CreateDebugStructType(structAst);
                        }
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst);
                        CreateDebugEnumType(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive);
                        CreateDebugBasicType(primitive);
                        break;
                    case PointerType pointerType:
                        DeclarePointerType(pointerType, pointersToResolve);
                        CreatePointerDebugType(pointerType);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType);
                        CreateArrayDebugType(arrayType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union);
                        CreateTemporaryDebugUnionType(union);
                        unionQueue.Add(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundType(compoundType);
                        DeclareCompoundDebugType(compoundType);
                        break;
                    case InterfaceAst interfaceAst:
                        DeclareInterfaceDebugType(interfaceAst);
                        interfaceQueue.Add(interfaceAst);
                        break;
                }
            }
        }
        else
        {
            foreach (var type in TypeTable.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        if (_types[structAst.TypeIndex].Handle == IntPtr.Zero)
                        {
                            _types[structAst.TypeIndex] = CreateNamedStruct(structAst.Name);
                        }

                        if (structAst.Fields.Any())
                        {
                            structQueue.Add(structAst);
                        }
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive);
                        break;
                    case PointerType pointerType:
                        DeclarePointerType(pointerType, pointersToResolve);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundType(compoundType);
                        break;
                    case InterfaceAst interfaceAst:
                        interfaceQueue.Add(interfaceAst);
                        break;
                }
            }
        }

        foreach (var interfaceAst in interfaceQueue)
        {
            var argumentTypes = new LLVMTypeRef[interfaceAst.Arguments.Count];
            for (var arg = 0; arg < argumentTypes.Length; arg++)
            {
                var argument = interfaceAst.Arguments[arg];
                argumentTypes[arg] = _types[argument.Type.TypeIndex];
            }

            var functionType = LLVMTypeRef.CreateFunction(_types[interfaceAst.ReturnType.TypeIndex], argumentTypes, false);
            _types[interfaceAst.TypeIndex] = LLVM.PointerType(functionType, 0);
        }

        foreach (var pointerType in pointersToResolve)
        {
            _types[pointerType.TypeIndex] = LLVM.PointerType(_types[pointerType.PointedType.TypeIndex], 0);
        }

        foreach (var structAst in structQueue)
        {
            SetStructTypeFields(structAst);
        }

        foreach (var union in unionQueue)
        {
            DeclareUnionDebugType(union);
        }
    }

    private static LLVMValueRef CreateTypeInfoIfNotExists(IType type)
    {
        var typeInfo = _typeInfos[type.TypeIndex];

        if (typeInfo.Handle != IntPtr.Zero) return typeInfo;

        switch (type)
        {
            case StructAst structAst:
                CreateTypeInfo(_structTypeInfoType, structAst.TypeIndex);
                SetStructTypeInfo(structAst);
                break;
            case EnumAst enumAst:
                CreateTypeInfoIfNotExists(enumAst.BaseType);
                DeclareEnumTypeInfo(enumAst);
                break;
            case PrimitiveAst primitive:
                DeclarePrimitiveTypeInfo(primitive);
                break;
            case PointerType pointerType:
                var typeName = GetString(pointerType.Name);
                var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Pointer, 0);
                var typeSize = LLVM.ConstInt(LLVM.Int32Type(), 8, 0);

                typeInfo = CreateTypeInfo(_pointerTypeInfoType, pointerType.TypeIndex);

                var pointerTypeInfo = CreateTypeInfoIfNotExists(pointerType.PointedType);
                Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, pointerTypeInfo};

                var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(_pointerTypeInfoType, fields);
                LLVM.SetInitializer(typeInfo, typeInfoStruct);
                break;
            case ArrayType arrayType:
                CreateTypeInfoIfNotExists(arrayType.ElementType);
                DeclareArrayTypeInfo(arrayType);
                break;
            case UnionAst union:
                CreateTypeInfo(_unionTypeInfoType, union.TypeIndex);

                Span<LLVMValueRef> unionFields = stackalloc LLVMValueRef[union.Fields.Count];
                Span<LLVMValueRef> unionFieldFields = stackalloc LLVMValueRef[2];
                for (var i = 0; i < union.Fields.Count; i++)
                {
                    var field = union.Fields[i];
                    unionFieldFields[0] = GetString(field.Name);
                    unionFieldFields[1] = CreateTypeInfoIfNotExists(field.Type);

                    unionFields[i] = LLVMValueRef.CreateConstNamedStruct(_unionFieldType, unionFieldFields);
                }

                DeclareUnionTypeInfo(union, unionFields);
                break;
            case CompoundType compoundType:
                Span<LLVMValueRef> typeInfos = stackalloc LLVMValueRef[compoundType.Types.Length];
                for (var i = 0; i < typeInfos.Length; i++)
                {
                    typeInfos[i] = CreateTypeInfoIfNotExists(compoundType.Types[i]);
                }

                DeclareCompoundTypeInfo(compoundType, typeInfos);
                break;
            case InterfaceAst interfaceAst:
            {
                CreateTypeInfo(_interfaceTypeInfoType, interfaceAst.TypeIndex);

                Span<LLVMValueRef> argumentValues = stackalloc LLVMValueRef[interfaceAst.Arguments.Count];
                for (var arg = 0; arg < argumentValues.Length; arg++)
                {
                    var argument = interfaceAst.Arguments[arg];
                    var argumentTypeInfo = CreateTypeInfoIfNotExists(argument.Type);

                    argumentValues[arg] = CreateArgumentType(argument, argumentTypeInfo);
                }

                var returnType = CreateTypeInfoIfNotExists(interfaceAst.ReturnType);

                DeclareInterfaceTypeInfo(interfaceAst, argumentValues, returnType);
                break;
            }
            case FunctionAst function:
            {
                var argumentCount = function.Flags.Has(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
                Span<LLVMValueRef> argumentValues = stackalloc LLVMValueRef[argumentCount];
                for (var arg = 0; arg < argumentCount; arg++)
                {
                    var argument = function.Arguments[arg];
                    var argumentTypeInfo = CreateTypeInfoIfNotExists(argument.Type);

                    argumentValues[arg] = CreateArgumentType(argument, argumentTypeInfo);
                }

                var returnType = CreateTypeInfoIfNotExists(function.ReturnType);

                CreateFunctionTypeInfo(function, argumentValues, returnType);
                break;
            }
            default:
                Debug.Assert(false, "Unhandled type info");
                break;
        }

        return _typeInfos[type.TypeIndex];
    }

    private static LLVMValueRef CreateTypeInfo(LLVMTypeRef typeInfoType, int typeIndex)
    {
        var typeInfo = AddGlobal(typeInfoType, "____type_info");
        SetPrivateConstant(typeInfo);
        return _typeInfos[typeIndex] = typeInfo;
    }

    private static LLVMValueRef CreateConstantArray(LLVMTypeRef elementType, LLVMTypeRef arrayType, ReadOnlySpan<LLVMValueRef> data, string name)
    {
        var array = LLVMValueRef.CreateConstArray(elementType, data);
        var arrayGlobal = AddGlobal(LLVM.TypeOf(array), name);
        SetPrivateConstant(arrayGlobal);
        LLVM.SetInitializer(arrayGlobal, array);

        var length = LLVM.ConstInt(LLVM.Int64Type(), (ulong)data.Length, 0);
        return LLVMValueRef.CreateConstNamedStruct(arrayType, stackalloc LLVMValueRef[] {length, arrayGlobal});
    }

    private static void DeclareEnum(EnumAst enumAst)
    {
        _types[enumAst.TypeIndex] = GetIntegerType(enumAst.BaseType.Size);
    }

    private static void DeclareEnumTypeInfo(EnumAst enumAst)
    {
        var typeName = GetString(enumAst.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Enum, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), enumAst.Size, 0);

        Span<LLVMValueRef> enumValueRefs = stackalloc LLVMValueRef[enumAst.Values.Count];
        foreach (var (name, value) in enumAst.Values)
        {
            var enumValueNameString = GetString(name);
            var enumValue = LLVM.ConstInt(LLVM.Int32Type(), (uint)value.Value, 0);

            enumValueRefs[value.Index] = LLVMValueRef.CreateConstNamedStruct(_enumValueType, stackalloc LLVMValueRef[] {enumValueNameString, enumValue});
        }

        var valuesArray = CreateConstantArray(_enumValueType, _enumValueArrayType, enumValueRefs, "____enum_values");

        LLVMValueRef attributes;
        if (enumAst.Attributes != null)
        {
            Span<LLVMValueRef> attributeRefs = stackalloc LLVMValueRef[enumAst.Attributes.Count];
            for (var i = 0; i < attributeRefs.Length; i++)
            {
                attributeRefs[i] = GetString(enumAst.Attributes[i]);
            }

            attributes = CreateConstantArray(_stringType, _stringArrayType, attributeRefs, "____enum_attributes");
        }
        else
        {
            attributes = _defaultAttributes;
        }

        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, _typeInfos[enumAst.BaseType.TypeIndex], valuesArray, attributes};
        CreateAndSetTypeInfo(_enumTypeInfoType, fields, enumAst.TypeIndex);
    }

    private static void DeclarePrimitive(PrimitiveAst primitive)
    {
        _types[primitive.TypeIndex] = primitive.TypeKind switch
        {
            TypeKind.Void => LLVM.VoidType(),
            TypeKind.Boolean => LLVM.Int1Type(),
            TypeKind.Integer => GetIntegerType(primitive.Size),
            TypeKind.Float => primitive.Size == 4 ? LLVM.FloatType() : LLVM.DoubleType(),
            TypeKind.Type => LLVM.Int32Type(),
            _ => null
        };
    }

    private static void DeclarePrimitiveTypeInfo(PrimitiveAst primitive)
    {
        var typeName = GetString(primitive.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)primitive.TypeKind, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), primitive.Size, 0);

        if (primitive.TypeKind == TypeKind.Integer)
        {
            var signed = LLVM.ConstInt(LLVM.Int1Type(), (byte)(primitive.Signed ? 1 : 0), 0);
            Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, signed};
            CreateAndSetTypeInfo(_integerTypeInfoType, fields, primitive.TypeIndex);
        }
        else
        {
            Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize};
            CreateAndSetTypeInfo(_typeInfoType, fields, primitive.TypeIndex);
        }
    }

    private static void DeclarePointerType(PointerType pointerType, List<PointerType> pointersToResolve)
    {
        if (pointerType.PointedType.TypeKind == TypeKind.Void)
        {
            _types[pointerType.TypeIndex] = LLVM.PointerType(LLVM.Int8Type(), 0);
        }
        else
        {
            var pointedType = _types[pointerType.PointedType.TypeIndex];
            if (pointedType.Handle == IntPtr.Zero)
            {
                pointersToResolve.Add(pointerType);
            }
            else
            {
                _types[pointerType.TypeIndex] = LLVM.PointerType(pointedType, 0);
            }
        }
    }

    private static void DeclarePointerTypeInfo(PointerType pointerType)
    {
        var typeName = GetString(pointerType.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Pointer, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), 8, 0);

        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, _typeInfos[pointerType.PointedType.TypeIndex]};
        CreateAndSetTypeInfo(_pointerTypeInfoType, fields, pointerType.TypeIndex);
    }

    private static void DeclareArrayType(ArrayType arrayType)
    {
        _types[arrayType.TypeIndex] = LLVM.ArrayType(_types[arrayType.ElementType.TypeIndex], arrayType.Length);
    }

    private static void DeclareArrayTypeInfo(ArrayType arrayType)
    {
        var typeName = GetString(arrayType.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.CArray, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), arrayType.Size, 0);
        var arrayLength = LLVM.ConstInt(LLVM.Int32Type(), arrayType.Length, 0);

        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, arrayLength, _typeInfos[arrayType.ElementType.TypeIndex]};
        CreateAndSetTypeInfo(_arrayTypeInfoType, fields, arrayType.TypeIndex);
    }

    private static void DeclareUnion(UnionAst union)
    {
        var type = _types[union.TypeIndex] = CreateNamedStruct(union.Name);

        type.StructSetBody(stackalloc []{LLVMTypeRef.CreateArray(LLVM.Int8Type(), union.Size)}, false);
    }

    private static void DeclareUnionTypeInfo(UnionAst union)
    {
        Span<LLVMValueRef> unionFields = stackalloc LLVMValueRef[union.Fields.Count];
        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[2];

        for (var i = 0; i < union.Fields.Count; i++)
        {
            var field = union.Fields[i];

            fields[0] = GetString(field.Name);
            fields[1] = _typeInfos[field.Type.TypeIndex];

            unionFields[i] = LLVMValueRef.CreateConstNamedStruct(_unionFieldType, fields);
        }

        DeclareUnionTypeInfo(union, unionFields);
    }

    private static void DeclareUnionTypeInfo(UnionAst union, ReadOnlySpan<LLVMValueRef> unionFields)
    {
        var typeName = GetString(union.Name);

        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Union, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), union.Size, 0);
        var unionFieldsArray = CreateConstantArray(_unionFieldType, _unionFieldArrayType, unionFields, "____union_fields");

        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, unionFieldsArray};
        var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(_unionTypeInfoType, fields);

        LLVM.SetInitializer(_typeInfos[union.TypeIndex], typeInfoStruct);
    }

    private static void DeclareCompoundType(CompoundType compoundType)
    {
        Span<LLVMTypeRef> types = stackalloc LLVMTypeRef[compoundType.Types.Length];

        for (var i = 0; i < types.Length; i++)
        {
            var type = compoundType.Types[i];
            types[i] = _types[type.TypeIndex];
        }

        _types[compoundType.TypeIndex] = LLVMTypeRef.CreateStruct(types, true);
    }

    private static void DeclareCompoundTypeAndTypeInfo(CompoundType compoundType)
    {
        Span<LLVMTypeRef> types = stackalloc LLVMTypeRef[compoundType.Types.Length];
        Span<LLVMValueRef> typeInfos = stackalloc LLVMValueRef[compoundType.Types.Length];

        for (var i = 0; i < types.Length; i++)
        {
            var type = compoundType.Types[i];
            types[i] = _types[type.TypeIndex];
            typeInfos[i] = _typeInfos[type.TypeIndex];
        }

        _types[compoundType.TypeIndex] = LLVMTypeRef.CreateStruct(types, true);

        DeclareCompoundTypeInfo(compoundType, typeInfos);
    }

    private static void DeclareCompoundTypeInfo(CompoundType compoundType, ReadOnlySpan<LLVMValueRef> typeInfos)
    {
        var typeName = GetString(compoundType.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Compound, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), compoundType.Size, 0);
        var typeInfoArray = CreateConstantArray(_typeInfoType, _typeInfoArrayType, typeInfos, "____compound_types");

        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, typeKind, typeSize, typeInfoArray};
        CreateAndSetTypeInfo(_compoundTypeInfoType, fields, compoundType.TypeIndex);
    }

    private static void DeclareInterfaceTypeInfo(InterfaceAst interfaceAst, ReadOnlySpan<LLVMValueRef> argumentValues, LLVMValueRef returnType)
    {
        var typeName = GetString(interfaceAst.Name);
        var arguments = CreateConstantArray(_argumentType, _argumentArrayType, argumentValues, "____arguments");
        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeName, _interfaceTypeKind, _zeroInt, returnType, arguments};

        var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(_interfaceTypeInfoType, fields);
        LLVM.SetInitializer(_typeInfos[interfaceAst.TypeIndex], typeInfoStruct);
    }

    private static void SetStructTypeFields(StructAst structAst)
    {
        Span<LLVMTypeRef> structFields = stackalloc LLVMTypeRef[structAst.Fields.Count];

        for (var i = 0; i < structAst.Fields.Count; i++)
        {
            var field = structAst.Fields[i];
            structFields[i] = _types[field.Type.TypeIndex];
        }
        _types[structAst.TypeIndex].StructSetBody(structFields, false);

        if (_emitDebug)
        {
            CreateDebugStructType(structAst);
        }
    }

    private static void SetStructTypeFieldsAndTypeInfo(StructAst structAst)
    {
        LLVMValueRef structTypeInfoFields;

        if (structAst.Fields.Any())
        {
            Span<LLVMTypeRef> structFields = stackalloc LLVMTypeRef[structAst.Fields.Count];
            Span<LLVMValueRef> typeFields = stackalloc LLVMValueRef[structAst.Fields.Count];

            for (var i = 0; i < structAst.Fields.Count; i++)
            {
                var field = structAst.Fields[i];
                structFields[i] = _types[field.Type.TypeIndex];
                typeFields[i] = GetStructFieldTypeInfo(field, _typeInfos[field.Type.TypeIndex]);
            }
            _types[structAst.TypeIndex].StructSetBody(structFields, false);

            structTypeInfoFields = CreateConstantArray(_typeFieldType, _typeFieldArrayType, typeFields, "____type_fields");

            if (_emitDebug)
            {
                CreateDebugStructType(structAst);
            }
        }
        else
        {
            structTypeInfoFields = _defaultFields;
        }

        SetStructTypeInfo(structAst, structTypeInfoFields);
    }

    private static void SetStructTypeInfo(StructAst structAst)
    {
        LLVMValueRef structTypeInfoFields;

        if (structAst.Fields.Any())
        {
            Span<LLVMValueRef> typeFields = stackalloc LLVMValueRef[structAst.Fields.Count];

            for (var i = 0; i < structAst.Fields.Count; i++)
            {
                var field = structAst.Fields[i];

                var fieldTypeInfo = CreateTypeInfoIfNotExists(field.Type);

                typeFields[i] = GetStructFieldTypeInfo(field, fieldTypeInfo);
            }

            structTypeInfoFields = CreateConstantArray(_typeFieldType, _typeFieldArrayType, typeFields, "____type_fields");
        }
        else
        {
            structTypeInfoFields = _defaultFields;
        }

        SetStructTypeInfo(structAst, structTypeInfoFields);
    }

    private static LLVMValueRef GetStructFieldTypeInfo(StructFieldAst field, LLVMValueRef fieldTypeInfo)
    {
        var fieldNameString = GetString(field.Name);
        var fieldOffset = LLVM.ConstInt(LLVM.Int32Type(), field.Offset, 0);

        LLVMValueRef fieldAttributes;
        if (field.Attributes != null)
        {
            Span<LLVMValueRef> attributeRefs = stackalloc LLVMValueRef[field.Attributes.Count];

            for (var attributeIndex = 0; attributeIndex < attributeRefs.Length; attributeIndex++)
            {
                attributeRefs[attributeIndex] = GetString(field.Attributes[attributeIndex]);
            }

            fieldAttributes = CreateConstantArray(_stringType, _stringArrayType, attributeRefs, "____field_attributes");
        }
        else
        {
            fieldAttributes = _defaultAttributes;
        }

        return LLVMValueRef.CreateConstNamedStruct(_typeFieldType, stackalloc LLVMValueRef[] {fieldNameString, fieldOffset, fieldTypeInfo, fieldAttributes});
    }

    private static void SetStructTypeInfo(StructAst structAst, LLVMValueRef structTypeInfoFields)
    {
        var typeName = GetString(structAst.Name);

        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)structAst.TypeKind, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), structAst.Size, 0);

        LLVMValueRef attributes;
        if (structAst.Attributes != null)
        {
            Span<LLVMValueRef> attributeRefs = stackalloc LLVMValueRef[structAst.Attributes.Count];

            for (var i = 0; i < attributeRefs.Length; i++)
            {
                attributeRefs[i] = GetString(structAst.Attributes[i]);
            }

            attributes = CreateConstantArray(_stringType, _stringArrayType, attributeRefs, "____struct_attributes");
        }
        else
        {
            attributes = _defaultAttributes;
        }

        var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(_structTypeInfoType, stackalloc  LLVMValueRef[]{typeName, typeKind, typeSize, structTypeInfoFields, attributes});

        LLVM.SetInitializer(_typeInfos[structAst.TypeIndex], typeInfoStruct);
    }

    private static void CreateFunctionTypeInfo(FunctionAst function)
    {
        var argumentCount = function.Flags.Has(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
        Span<LLVMValueRef> argumentValues = stackalloc LLVMValueRef[argumentCount];
        for (var arg = 0; arg < argumentCount; arg++)
        {
            var argument = function.Arguments[arg];
            var argumentTypeInfo = _typeInfos[argument.Type.TypeIndex];

            argumentValues[arg] = CreateArgumentType(argument, argumentTypeInfo);
        }

        var returnType = _typeInfos[function.ReturnType.TypeIndex];

        CreateFunctionTypeInfo(function, argumentValues, returnType);
    }

    private static LLVMValueRef CreateArgumentType(DeclarationAst argument, LLVMValueRef argumentTypeInfo)
    {
        var argNameString = GetString(argument.Name);
        return LLVMValueRef.CreateConstNamedStruct(_argumentType, stackalloc LLVMValueRef[] {argNameString, argumentTypeInfo});
    }

    private static void CreateFunctionTypeInfo(FunctionAst function, ReadOnlySpan<LLVMValueRef> argumentValues, LLVMValueRef returnType)
    {
        var arguments = CreateConstantArray(_argumentType, _argumentArrayType, argumentValues, "____arguments");

        LLVMValueRef attributes;
        if (function.Attributes != null)
        {
            Span<LLVMValueRef> attributeRefs = stackalloc LLVMValueRef[function.Attributes.Count];

            for (var attributeIndex = 0; attributeIndex < attributeRefs.Length; attributeIndex++)
            {
                attributeRefs[attributeIndex] = GetString(function.Attributes[attributeIndex]);
            }

            attributes = CreateConstantArray(_stringType, _stringArrayType, attributeRefs, "____function_attributes");
        }
        else
        {
            attributes = _defaultAttributes;
        }

        var typeNameString = GetString(function.Name);
        Span<LLVMValueRef> fields = stackalloc LLVMValueRef[]{typeNameString, _functionTypeKind, _zeroInt, returnType, arguments, attributes};

        CreateAndSetTypeInfo(_functionTypeInfoType, fields, function.TypeIndex);
    }

    private static void CreateAndSetTypeInfo(LLVMTypeRef typeInfoType, ReadOnlySpan<LLVMValueRef> fields, int typeIndex)
    {
        var typeInfo = CreateTypeInfo(typeInfoType, typeIndex);

        var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(typeInfoType, fields);
        LLVM.SetInitializer(typeInfo, typeInfoStruct);
    }

    private static LLVMTypeRef GetIntegerType(uint size)
    {
        return size switch
        {
            1 => LLVM.Int8Type(),
            2 => LLVM.Int16Type(),
            4 => LLVM.Int32Type(),
            8 => LLVM.Int64Type(),
            _ => LLVM.Int32Type()
        };
    }

    private static void SetPrivateConstant(LLVMValueRef variable)
    {
        LLVM.SetLinkage(variable, LLVMLinkage.LLVMPrivateLinkage);
        LLVM.SetGlobalConstant(variable, 1);
        LLVM.SetUnnamedAddr(variable, 1);
    }

    private static LLVMValueRef WriteFunctionDefinition(String name, FunctionIR function)
    {
        LLVMValueRef functionPointer;
        var linkageNamePointer = stackalloc sbyte[name.Length + 1];
        for (var i = 0; i < name.Length; i++)
        {
            linkageNamePointer[i] = (sbyte)name[i];
        }
        linkageNamePointer[name.Length] = 0;

        if (_emitDebug && function.Instructions != null)
        {
            var varargs = function.Source.Flags.Has(FunctionFlags.Varargs);
            var sourceArguments = function.Source.Arguments;
            var argumentCount = varargs ? sourceArguments.Count - 1 : sourceArguments.Count;

            // Get the argument types and create debug symbols
            Span<LLVMTypeRef> argumentTypes = stackalloc LLVMTypeRef[argumentCount];
            Span<LLVMMetadataRef> debugArgumentTypes = stackalloc LLVMMetadataRef[argumentCount + 1];
            debugArgumentTypes[0] = _debugTypes[function.Source.ReturnType.TypeIndex];

            for (var i = 0; i < argumentCount; i++)
            {
                var argumentType = sourceArguments[i].Type.TypeIndex;
                argumentTypes[i] = _types[argumentType];
                debugArgumentTypes[i + 1] = _debugTypes[argumentType];
            }

            var sourceName = function.Source.Name;
            var namePointer = stackalloc sbyte[sourceName.Length];
            for (var i = 0; i < sourceName.Length; i++)
            {
                namePointer[i] = (sbyte)sourceName[i];
            }

            var file = _debugFiles[function.Source.FileIndex];
            var functionType = _debugBuilder.CreateSubroutineType(file, debugArgumentTypes, LLVMDIFlags.LLVMDIFlagZero);
            var debugFunction = _debugFunctions[function.Source.FunctionIndex] = LLVM.DIBuilderCreateFunction(_debugBuilder, file, namePointer, sourceName, linkageNamePointer, name, file, function.Source.Line, functionType, 0, 1, function.Source.Line, LLVMDIFlags.LLVMDIFlagPrototyped, 0);

            // Declare the function
            functionPointer = LLVM.AddFunction(_module, linkageNamePointer, LLVMTypeRef.CreateFunction(_types[function.Source.ReturnType.TypeIndex], argumentTypes, varargs));
            LLVM.SetSubprogram(functionPointer, debugFunction);
        }
        else
        {
            var functionType = GetFunctionType(function.Source);
            functionPointer = LLVM.AddFunction(_module, linkageNamePointer, functionType);
        }

        if (function.Instructions == null)
        {
            var functionAst = (FunctionAst)function.Source;
            if (functionAst.Library == null)
            {
                BuildSettings.LibraryNames.Add(functionAst.ExternLib);
            }
            else
            {
                BuildSettings.Libraries.Add(functionAst.Library);
            }
        }
        else
        {
            _functionsToWrite.Enqueue((functionPointer, function));
        }

        _functions[function.Source.FunctionIndex] = functionPointer;
        return functionPointer;
    }

    private static LLVMTypeRef GetFunctionType(IFunction function)
    {
        var type = _functionTypes[function.FunctionIndex];
        if (type.Handle != IntPtr.Zero)
        {
            return type;
        }

        var varargs = function.Flags.Has(FunctionFlags.Varargs);
        var sourceArguments = function.Arguments;
        var argumentCount = varargs ? sourceArguments.Count - 1 : sourceArguments.Count;

        Span<LLVMTypeRef> argumentTypes = stackalloc LLVMTypeRef[argumentCount];
        for (var i = 0; i < argumentCount; i++)
        {
            argumentTypes[i] = _types[sourceArguments[i].Type.TypeIndex];
        }
        return _functionTypes[function.FunctionIndex] = LLVMTypeRef.CreateFunction(_types[function.ReturnType.TypeIndex], argumentTypes, varargs);
    }

    private static LLVMValueRef GetOrCreateFunctionDefinition(int functionIndex)
    {
        var functionPointer = _functions[functionIndex];
        if (functionPointer.Handle == IntPtr.Zero)
        {
            var function = Program.Functions[functionIndex];
            functionPointer = WriteFunctionDefinition(function.Source.Name, function);
        }

        return functionPointer;
    }

    private static void WriteFunction(LLVMValueRef functionPointer, FunctionIR function)
    {
        // Declare the basic blocks
        Span<LLVMBasicBlockRef> basicBlocks = stackalloc LLVMBasicBlockRef[function.BasicBlocks.Count];
        var blockNameLength = (int)Math.Log10(function.BasicBlocks.Count) + 2;
        var basicBlockName = stackalloc sbyte[blockNameLength];
        for (var i = 0; i < blockNameLength; i++)
        {
            basicBlockName[i] = 0;
        }

        foreach (var block in function.BasicBlocks)
        {
            var index = block.Index;
            var digits = index == 0 ? 1 : (int)Math.Log10(index);
            while (digits > 0)
            {
                basicBlockName[--digits] = (sbyte)(index % 10 + 48); // 48 = '0'
                index /= 10;
            }
            basicBlocks[block.Index] = LLVM.AppendBasicBlock(functionPointer, basicBlockName);
        }
        LLVM.PositionBuilderAtEnd(_builder, basicBlocks[0]);
        _builder.CurrentDebugLocation = null;

        // Allocate the function stack
        Span<LLVMValueRef> allocations = stackalloc LLVMValueRef[function.Allocations.Count];
        foreach (var allocation in function.Allocations)
        {
            if (allocation.Array)
            {
                allocations[allocation.Index] = _builder.BuildAlloca(LLVM.ArrayType(_types[allocation.Type.TypeIndex], allocation.ArrayLength));
            }
            else
            {
                allocations[allocation.Index] = _builder.BuildAlloca(_types[allocation.Type.TypeIndex]);
            }
        }

        LLVMValueRef stackPointer = null;
        if (function.SaveStack)
        {
            const string stackSaveIntrinsic = "llvm.stacksave";
            stackPointer = _builder.BuildAlloca(_u8PointerType);

            var stackSave = _module.GetNamedFunction(stackSaveIntrinsic);
            if (stackSave.Handle == IntPtr.Zero)
            {
                stackSave = _module.AddFunction(stackSaveIntrinsic, LLVMTypeRef.CreateFunction(_u8PointerType, Array.Empty<LLVMTypeRef>()));
            }

            var stackPointerValue = _builder.BuildCall(stackSave, Array.Empty<LLVMValueRef>(), "stackPointer");
            _builder.BuildStore(stackPointerValue, stackPointer);
        }

        LLVMMetadataRef debugBlock = null;
        LLVMMetadataRef file = null;
        LLVMMetadataRef expression = null;
        IScope currentScope = null;
        Dictionary<IScope, LLVMMetadataRef> lexicalBlocks = null;
        if (_emitDebug)
        {
            debugBlock = _debugFunctions[function.Source.FunctionIndex];
            file = _debugFiles[function.Source.FileIndex];
            expression = LLVM.DIBuilderCreateExpression(_debugBuilder, null, UIntPtr.Zero);
            currentScope = function.Source.Body;
            lexicalBlocks = new() { [function.Source.Body] = debugBlock };
        }

        // Write the instructions
        var blockIndex = 0;
        var instructionIndex = 0;
        Span<LLVMValueRef> values = stackalloc LLVMValueRef[function.ValueCount];
        Span<LLVMValueRef> gepValues = stackalloc LLVMValueRef[2] {_zeroInt, null};
        while (blockIndex < function.BasicBlocks.Count)
        {
            LLVM.PositionBuilderAtEnd(_builder, basicBlocks[blockIndex]); // Redundant for the first pass, not a big deal
            var instructionToStopAt = blockIndex < function.BasicBlocks.Count - 1 ? function.BasicBlocks[blockIndex + 1].Location : function.Instructions.Count;
            var breakToNextBlock = true;
            while (instructionIndex < instructionToStopAt)
            {
                var instruction = function.Instructions[instructionIndex++];

                if (_emitDebug && currentScope != instruction.Scope)
                {
                    if (!lexicalBlocks.TryGetValue(instruction.Scope, out var newDebugBlock))
                    {
                        var newScope = (ScopeAst)instruction.Scope;
                        newDebugBlock = lexicalBlocks[newScope] = LLVM.DIBuilderCreateLexicalBlock(_debugBuilder, debugBlock, _debugFiles[newScope.FileIndex], newScope.Line, newScope.Column);
                    }
                    currentScope = instruction.Scope;
                    debugBlock = newDebugBlock;
                }

                switch (instruction.Type)
                {
                    case InstructionType.Jump:
                    {
                        _builder.BuildBr(basicBlocks[instruction.Value1.JumpBlock.Index]);
                        breakToNextBlock = false;
                        break;
                    }
                    case InstructionType.ConditionalJump:
                    {
                        var condition = GetValue(instruction.Value1, values, allocations, functionPointer);
                        _builder.BuildCondBr(condition, basicBlocks[instruction.Value2.JumpBlock.Index], basicBlocks[blockIndex + 1]);
                        breakToNextBlock = false;
                        break;
                    }
                    case InstructionType.Return:
                    {
                        if (function.SaveStack)
                        {
                            BuildStackRestore(stackPointer);
                        }
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        _builder.BuildRet(value);
                        breakToNextBlock = false;
                        break;
                    }
                    case InstructionType.ReturnVoid:
                    {
                        if (function.SaveStack)
                        {
                            BuildStackRestore(stackPointer);
                        }
                        _builder.BuildRetVoid();
                        breakToNextBlock = false;
                        break;
                    }
                    case InstructionType.Load:
                    case InstructionType.LoadPointer:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildLoad(value);
                        break;
                    }
                    case InstructionType.Store:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var value = GetValue(instruction.Value2, values, allocations, functionPointer);
                        _builder.BuildStore(value, pointer);
                        break;
                    }
                    case InstructionType.GetPointer:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        gepValues[1] = index;
                        values[instruction.ValueIndex] = _builder.BuildGEP(pointer, instruction.Flag ? gepValues : gepValues.Slice(1, 1), ReadOnlySpan<char>.Empty);
                        break;
                    }
                    case InstructionType.GetStructPointer:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildStructGEP(pointer, (uint)instruction.Index);
                        break;
                    }
                    case InstructionType.GetUnionPointer:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildBitCast(pointer, LLVM.PointerType(targetType, 0));
                        break;
                    }
                    case InstructionType.Call:
                    {
                        BuildCall(instruction, values, allocations, functionPointer);
                        break;
                    }
                    case InstructionType.CallFunctionPointer:
                    {
                        BuildFunctionPointerCall(instruction, values, allocations, functionPointer);
                        break;
                    }
                    case InstructionType.SystemCall:
                    {
                        BuildSystemCall(instruction, values, allocations, functionPointer);
                        break;
                    }
                    case InstructionType.InlineAssembly:
                    {
                        BuildInlineAssembly(instruction, values, allocations, functionPointer);
                        break;
                    }
                    case InstructionType.IntegerExtend:
                    case InstructionType.UnsignedIntegerToIntegerExtend:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildSExtOrBitCast(value, targetType);
                        break;
                    }
                    case InstructionType.UnsignedIntegerExtend:
                    case InstructionType.IntegerToUnsignedIntegerExtend:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildZExtOrBitCast(value, targetType);
                        break;
                    }
                    case InstructionType.IntegerTruncate:
                    case InstructionType.UnsignedIntegerToIntegerTruncate:
                    case InstructionType.UnsignedIntegerTruncate:
                    case InstructionType.IntegerToUnsignedIntegerTruncate:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildTrunc(value, targetType);
                        break;
                    }
                    case InstructionType.IntegerToFloatCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildSIToFP(value, targetType);
                        break;
                    }
                    case InstructionType.UnsignedIntegerToFloatCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildUIToFP(value, targetType);
                        break;
                    }
                    case InstructionType.FloatCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildFPCast(value, targetType);
                        break;
                    }
                    case InstructionType.FloatToIntegerCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildFPToSI(value, targetType);
                        break;
                    }
                    case InstructionType.FloatToUnsignedIntegerCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildFPToUI(value, targetType);
                        break;
                    }
                    case InstructionType.PointerCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildBitCast(value, targetType);
                        break;
                    }
                    case InstructionType.PointerToIntegerCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildPtrToInt(value, targetType);
                        break;
                    }
                    case InstructionType.IntegerToPointerCast:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var targetType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildIntToPtr(value, targetType);
                        break;
                    }
                    case InstructionType.IntegerToEnumCast:
                    {
                        values[instruction.ValueIndex] = GetValue(instruction.Value1, values, allocations, functionPointer);
                        break;
                    }
                    case InstructionType.AllocateArray:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var length = GetValue(instruction.Value2, values, allocations, functionPointer);

                        var elementType =_types[instruction.LoadType.TypeIndex];
                        var arrayData = _builder.BuildArrayAlloca(elementType, length);
                        _builder.BuildStore(arrayData, pointer);
                        break;
                    }
                    case InstructionType.IsNull:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildIsNull(value);
                        break;
                    }
                    case InstructionType.IsNotNull:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildIsNotNull(value);
                        break;
                    }
                    case InstructionType.Not:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildNot(value);
                        break;
                    }
                    case InstructionType.IntegerNegate:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildNeg(value);
                        break;
                    }
                    case InstructionType.FloatNegate:
                    {
                        var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFNeg(value);
                        break;
                    }
                    case InstructionType.And:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildAnd(lhs, rhs);
                        break;
                    }
                    case InstructionType.Or:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildOr(lhs, rhs);
                        break;
                    }
                    case InstructionType.BitwiseAnd:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildAnd(lhs, rhs);
                        break;
                    }
                    case InstructionType.BitwiseOr:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildOr(lhs, rhs);
                        break;
                    }
                    case InstructionType.Xor:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildXor(lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerEquals:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        var diff = _builder.BuildPtrDiff(lhs, rhs);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, diff, LLVMValueRef.CreateConstInt(LLVM.TypeOf(diff), 0, false));
                        break;
                    }
                    case InstructionType.IntegerEquals:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatEquals:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOEQ, lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        var diff = _builder.BuildPtrDiff(lhs, rhs);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, diff, LLVMValueRef.CreateConstInt(LLVM.TypeOf(diff), 0, false));
                        break;
                    }
                    case InstructionType.IntegerNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatNotEquals:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealONE, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatGreaterThan:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntUGE, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatGreaterThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGE, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULT, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatLessThan:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULE, lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatLessThanOrEqual:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLE, lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerAdd:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        gepValues[1] = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildGEP(pointer, gepValues.Slice(1, 1), ReadOnlySpan<char>.Empty);
                        break;
                    }
                    case InstructionType.IntegerAdd:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildAdd(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatAdd:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFAdd(lhs, rhs);
                        break;
                    }
                    case InstructionType.PointerSubtract:
                    {
                        var pointer = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        gepValues[1] = _builder.BuildNeg(index);
                        values[instruction.ValueIndex] = _builder.BuildGEP(pointer, gepValues.Slice(1, 1), ReadOnlySpan<char>.Empty);
                        break;
                    }
                    case InstructionType.IntegerSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildSub(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatSubtract:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFSub(lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerMultiply:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildMul(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatMultiply:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFMul(lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerDivide:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildSDiv(lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerDivide:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildUDiv(lhs, rhs);
                        break;
                    }
                    case InstructionType.FloatDivide:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFDiv(lhs, rhs);
                        break;
                    }
                    case InstructionType.IntegerModulus:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildSRem(lhs, rhs);
                        break;
                    }
                    case InstructionType.UnsignedIntegerModulus:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildURem(lhs, rhs);
                        break;
                    }
                    case InstructionType.ShiftRight:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildAShr(lhs, rhs);
                        break;
                    }
                    case InstructionType.ShiftLeft:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildShl(lhs, rhs);
                        break;
                    }
                    case InstructionType.RotateRight:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        var result = _builder.BuildAShr(lhs, rhs);

                        var type = instruction.Value1.Type;
                        var maskSize = LLVMValueRef.CreateConstInt(_types[type.TypeIndex], type.Size * 8, false);
                        var maskShift = _builder.BuildSub(maskSize, rhs);

                        var mask = _builder.BuildShl(lhs, maskShift);

                        values[instruction.ValueIndex] = result.IsUndef ? mask : _builder.BuildOr(result, mask);
                        break;
                    }
                    case InstructionType.RotateLeft:
                    {
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        var result = _builder.BuildShl(lhs, rhs);

                        var type = instruction.Value1.Type;
                        var maskSize = LLVMValueRef.CreateConstInt(_types[type.TypeIndex], type.Size * 8, false);
                        var maskShift = _builder.BuildSub(maskSize, rhs);

                        var mask = _builder.BuildAShr(lhs, maskShift);

                        values[instruction.ValueIndex] = result.IsUndef ? mask : _builder.BuildOr(result, mask);
                        break;
                    }
                    case InstructionType.DebugSetLocation when _emitDebug:
                    {
                        var location = LLVM.DIBuilderCreateDebugLocation(_context, instruction.Source.Line, instruction.Source.Column, debugBlock, null);
                        LLVM.SetCurrentDebugLocation2(_builder, location);
                        break;
                    }
                    case InstructionType.DebugDeclareParameter:
                    {
                        var functionArg = function.Source.Arguments[instruction.Index];

                        var debugVariable = DeclareDebugParameter(functionArg.Name, instruction.Index, functionArg.Line, functionArg.Type, file, debugBlock);
                        var location = LLVM.DIBuilderCreateDebugLocation(_context, functionArg.Line, functionArg.Column, debugBlock, null);

                        LLVM.DIBuilderInsertDeclareAtEnd(_debugBuilder, allocations[instruction.Index], debugVariable, expression, location, basicBlocks[blockIndex]);
                        break;
                    }
                    case InstructionType.DebugDeclareVariable:
                    {
                        using var name = new MarshaledString(instruction.String);

                        var scope = (ScopeAst)instruction.Scope;
                        var debugVariable = DeclareDebugVariable(instruction.String, instruction.Source.Line, instruction.Value1.Type, _debugFiles[scope.FileIndex], debugBlock);
                        var location = LLVM.GetCurrentDebugLocation2(_builder);
                        var variable = GetValue(instruction.Value1, values, allocations, functionPointer);

                        LLVM.DIBuilderInsertDeclareAtEnd(_debugBuilder, variable, debugVariable, expression, location, _builder.InsertBlock);
                        break;
                    }
                }
            }
            blockIndex++;

            if (breakToNextBlock)
            {
                _builder.BuildBr(basicBlocks[blockIndex]);
            }
        }

        // Optimize the function if release build
        LLVM.RunFunctionPassManager(_passManager, functionPointer);
    }

    private static LLVMMetadataRef DeclareDebugParameter(String name, int arg, uint line, IType type, LLVMMetadataRef file, LLVMMetadataRef debugBlock)
    {
        var debugType = _debugTypes[type.TypeIndex];
        var pointer = stackalloc sbyte[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            pointer[i] = (sbyte)name[i];
        }
        return LLVM.DIBuilderCreateParameterVariable(_debugBuilder, debugBlock, pointer, name, (uint)arg+1, file, line, debugType, 0, LLVMDIFlags.LLVMDIFlagZero);
    }

    private static LLVMMetadataRef DeclareDebugVariable(String name, uint line, IType type, LLVMMetadataRef file, LLVMMetadataRef debugBlock)
    {
        var debugType = _debugTypes[type.TypeIndex];
        var pointer = stackalloc sbyte[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            pointer[i] = (sbyte)name[i];
        }
        return LLVM.DIBuilderCreateAutoVariable(_debugBuilder, debugBlock, pointer, name, file, line, debugType, 0, LLVMDIFlags.LLVMDIFlagZero, 0);
    }

    private static LLVMValueRef GetValue(InstructionValue value, ReadOnlySpan<LLVMValueRef> values, ReadOnlySpan<LLVMValueRef> allocations, LLVMValueRef functionPointer)
    {
        switch (value.ValueType)
        {
            case InstructionValueType.Value:
                return values[value.ValueIndex];
            case InstructionValueType.Allocation:
                if (value.Global)
                {
                    return _globals[value.ValueIndex];
                }
                return allocations[value.ValueIndex];
            case InstructionValueType.Argument:
                return functionPointer.GetParam((uint)value.ValueIndex);
            case InstructionValueType.Constant:
                return GetConstant(value);
            case InstructionValueType.Null:
                if (value.Type == null)
                {
                    return LLVM.ConstNull(_u8PointerType);
                }
                return LLVM.ConstNull(_types[value.Type.TypeIndex]);
            case InstructionValueType.TypeInfo:
                var typeInfo = _typeInfos[value.ValueIndex];
                return _builder.BuildBitCast(typeInfo, _typeInfoPointerType);
            case InstructionValueType.Function:
                return GetOrCreateFunctionDefinition(value.ValueIndex);
            case InstructionValueType.FileName:
                var fileName = _fileNames[value.ValueIndex];
                if (fileName.Handle == IntPtr.Zero)
                {
                    _fileNames[value.ValueIndex] = fileName = GetString(BuildSettings.Files[value.ValueIndex].Replace(BuildSettings.Path, string.Empty));
                }
                return fileName;
        }
        return null;
    }

    private static LLVMValueRef GetConstantValue(InstructionValue value)
    {
        switch (value.ValueType)
        {
            case InstructionValueType.Value:
                return _globals[value.ValueIndex];
            case InstructionValueType.Constant:
                return GetConstant(value);
            case InstructionValueType.Null:
                return LLVM.ConstNull(_types[value.Type.TypeIndex]);
            case InstructionValueType.ConstantStruct:
                Span<LLVMValueRef> fieldValues = stackalloc LLVMValueRef[value.Values.Length];
                for (var i = 0; i < fieldValues.Length; i++)
                {
                    fieldValues[i] = GetConstantValue(value.Values[i]);
                }
                return LLVMValueRef.CreateConstNamedStruct(_types[value.Type.TypeIndex], fieldValues);
            case InstructionValueType.ConstantArray when value.Values != null:
                Span<LLVMValueRef> values = stackalloc LLVMValueRef[(int)value.ArrayLength];
                for (var i = 0; i < value.ArrayLength; i++)
                {
                    values[i] = GetConstantValue(value.Values[i]);
                }
                return LLVMValueRef.CreateConstArray(_types[value.Type.TypeIndex], values);
        }
        return null;
    }

    private static LLVMValueRef GetConstant(InstructionValue value)
    {
        switch (value.Type.TypeKind)
        {
            case TypeKind.Boolean:
                return LLVMValueRef.CreateConstInt(LLVM.Int1Type(), value.ConstantValue.UnsignedInteger, false);
            case TypeKind.Integer:
            case TypeKind.Enum:
                return LLVMValueRef.CreateConstInt(_types[value.Type.TypeIndex], value.ConstantValue.UnsignedInteger, false);
            case TypeKind.Float:
                return LLVMValueRef.CreateConstReal(_types[value.Type.TypeIndex], value.ConstantValue.Double);
            default:
                return GetString(value.ConstantString, value.UseRawString, false);
        }
    }

    private static LLVMValueRef GetDefaultValue(IType type)
    {
        switch (type.TypeKind)
        {
            case TypeKind.Boolean:
                return LLVMValueRef.CreateConstInt(LLVM.Int1Type(), 0, false);
            case TypeKind.Integer:
            case TypeKind.Enum:
            case TypeKind.Type:
                return LLVMValueRef.CreateConstInt(_types[type.TypeIndex], 0, false);
            case TypeKind.Float:
                return LLVMValueRef.CreateConstReal(_types[type.TypeIndex], 0);
            case TypeKind.String:
            case TypeKind.Array:
            case TypeKind.Struct:
            case TypeKind.Any:
                var structAst = (StructAst)type;
                Span<LLVMValueRef> fields = stackalloc LLVMValueRef[structAst.Fields.Count];
                for (var i = 0; i < fields.Length; i++)
                {
                    fields[i] = GetDefaultValue(structAst.Fields[i].Type);
                }
                return LLVMValueRef.CreateConstNamedStruct(_types[structAst.TypeIndex], fields);
            case TypeKind.Pointer:
            case TypeKind.Interface:
                return LLVMValueRef.CreateConstNull(_types[type.TypeIndex]);
            case TypeKind.CArray:
            {
                var arrayType = (ArrayType)type;
                var defaultValue = GetDefaultValue(arrayType.ElementType);
                Span<LLVMValueRef> values = stackalloc LLVMValueRef[(int)arrayType.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = defaultValue;
                }
                return LLVMValueRef.CreateConstArray(_types[arrayType.ElementType.TypeIndex], values);
            }
            case TypeKind.Union:
            {
                var defaultValue = LLVMValueRef.CreateConstInt(LLVM.Int8Type(), 0, false);
                Span<LLVMValueRef> values = stackalloc LLVMValueRef[(int)type.Size];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = defaultValue;
                }
                var constArray = LLVMValueRef.CreateConstArray(LLVM.Int8Type(), values);
                return LLVMValueRef.CreateConstNamedStruct(_types[type.TypeIndex], stackalloc [] {constArray});
            }
        }
        return null;
    }

    private static LLVMValueRef GetString(String value, bool useRawString = false, bool constant = true)
    {
        var pointer = stackalloc sbyte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            pointer[i] = (sbyte)value[i];
        }

        var stringValue = LLVM.ConstStringInContext(_context, pointer, (uint)value.Length, 0);
        var stringGlobal = AddGlobal(LLVM.TypeOf(stringValue), "str");
        if (constant)
        {
            SetPrivateConstant(stringGlobal);
        }
        LLVM.SetInitializer(stringGlobal, stringValue);
        var stringPointer = LLVMValueRef.CreateConstBitCast(stringGlobal, _u8PointerType);

        if (useRawString)
        {
            return stringPointer;
        }

        var length = LLVMValueRef.CreateConstInt(LLVM.Int64Type(), (ulong)value.Length);
        return LLVMValueRef.CreateConstNamedStruct(_stringType, stackalloc [] {length, stringPointer});
    }

    private static void BuildStackRestore(LLVMValueRef stackPointer)
    {
        const string stackRestoreIntrinsic = "llvm.stackrestore";

        var stackRestore = _module.GetNamedFunction(stackRestoreIntrinsic);
        if (stackRestore.Handle == IntPtr.Zero)
        {
            stackRestore = _module.AddFunction(stackRestoreIntrinsic, LLVMTypeRef.CreateFunction(LLVM.VoidType(), stackalloc [] {_u8PointerType}, false));
        }

        var stackPointerValue = _builder.BuildLoad(stackPointer);
        _builder.BuildCall(stackRestore, stackalloc []{stackPointerValue}, ReadOnlySpan<char>.Empty);
    }

    private static void BuildCall(Instruction instruction, Span<LLVMValueRef> values, ReadOnlySpan<LLVMValueRef> allocations, LLVMValueRef functionPointer)
    {
        var callFunction = GetOrCreateFunctionDefinition(instruction.Index);
        Span<LLVMValueRef> arguments = stackalloc LLVMValueRef[instruction.Value1.Values.Length];
        for (var i = 0; i < instruction.Value1.Values.Length; i++)
        {
            arguments[i] = GetValue(instruction.Value1.Values[i], values, allocations, functionPointer);
        }
        values[instruction.ValueIndex] = _builder.BuildCall(callFunction, arguments, ReadOnlySpan<char>.Empty);
    }

    private static void BuildFunctionPointerCall(Instruction instruction, Span<LLVMValueRef> values, ReadOnlySpan<LLVMValueRef> allocations, LLVMValueRef functionPointer)
    {
        var callFunction = GetValue(instruction.Value1, values, allocations, functionPointer);
        Span<LLVMValueRef> arguments = stackalloc LLVMValueRef[instruction.Value2.Values.Length];
        for (var i = 0; i < instruction.Value2.Values.Length; i++)
        {
            arguments[i] = GetValue(instruction.Value2.Values[i], values, allocations, functionPointer);
        }
        values[instruction.ValueIndex] = _builder.BuildCall(callFunction, arguments, ReadOnlySpan<char>.Empty);
    }

    private static void BuildSystemCall(Instruction instruction, Span<LLVMValueRef> values, ReadOnlySpan<LLVMValueRef> allocations, LLVMValueRef functionPointer)
    {
        Span<LLVMValueRef> arguments = stackalloc LLVMValueRef[instruction.Value1.Values.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = GetValue(instruction.Value1.Values[i], values, allocations, functionPointer);
        }

        var syscallFunction = (FunctionAst)instruction.Source;
        var functionType = GetFunctionType(syscallFunction);

        var assembly = $"mov rax, {instruction.Index}; syscall;";

        string constraint;
        if (instruction.Flag)
        {
            constraint = arguments.Length switch
            {
                1 => "{di}",
                2 => "{di},{si}",
                3 => "{di},{si},{dx}",
                4 => "{di},{si},{dx},{r10}",
                5 => "{di},{si},{dx},{r10},{r8}",
                6 => "{di},{si},{dx},{r10},{r8},{r9}",
                _ => string.Empty
            };
        }
        else
        {
            constraint = arguments.Length switch
            {
                1 => "=A,{di}",
                2 => "=A,{di},{si}",
                3 => "=A,{di},{si},{dx}",
                4 => "=A,{di},{si},{dx},{r10}",
                5 => "=A,{di},{si},{dx},{r10},{r8}",
                6 => "=A,{di},{si},{dx},{r10},{r8},{r9}",
                _ => "=A"
            };
        }

        values[instruction.ValueIndex] = BuildAssemblyCall(assembly, constraint, functionType, arguments);
    }

    private static void BuildInlineAssembly(Instruction instruction, Span<LLVMValueRef> values, ReadOnlySpan<LLVMValueRef> allocations, LLVMValueRef functionPointer)
    {
        var assembly = (AssemblyAst)instruction.Source;

        var i = 0;
        Span<LLVMValueRef> arguments = stackalloc LLVMValueRef[assembly.InRegisters.Count];
        Span<LLVMTypeRef> argumentTypes = stackalloc LLVMTypeRef[assembly.InRegisters.Count];
        var assemblyString = new StringBuilder();
        var constraintString = new StringBuilder();

        // Declare the inputs and write the assembly instructions
        if (arguments.Length > 0)
        {
            foreach (var (register, input) in assembly.InRegisters)
            {
                arguments[i] = GetValue(input.Value, values, allocations, functionPointer);
                argumentTypes[i++] = _types[input.Value.Type.TypeIndex];
                constraintString.AppendFormat("{{{0}}},", register);
            }
            constraintString.Remove(constraintString.Length-1, 1);
        }

        foreach (var instr in assembly.Instructions)
        {
            assemblyString.Append((ReadOnlySpan<char>)instr.Instruction);
            if (instr.Value1 != null)
            {
                if (instr.Value1.Dereference)
                {
                    assemblyString.AppendFormat(" qword ptr [{0}]", instr.Value1.Register);
                }
                else if (instr.Value1.Register != null)
                {
                    assemblyString.AppendFormat(" {0}", instr.Value1.Register);
                }
                else
                {
                    assemblyString.AppendFormat(" 0x{0:x}", instr.Value1.Constant.Value.UnsignedInteger);
                }
            }
            if (instr.Value2 != null)
            {
                if (instr.Value2.Dereference)
                {
                    assemblyString.AppendFormat(", qword ptr [{0}]", instr.Value2.Register);
                }
                else if (instr.Value2.Register != null)
                {
                    assemblyString.AppendFormat(", {0}", instr.Value2.Register);
                }
                else
                {
                    assemblyString.AppendFormat(", 0x{0:x}", instr.Value2.Constant.Value.UnsignedInteger);
                }
            }
            assemblyString.Append(";\n");
        }

        var assemblyBodyType = LLVMTypeRef.CreateFunction(LLVM.VoidType(), argumentTypes, false);
        BuildAssemblyCall(assemblyString.ToString(), constraintString.ToString(), assemblyBodyType, arguments);

        // Capture the output registers if necessary
        if (assembly.OutValues.Count > 0)
        {
            arguments = stackalloc LLVMValueRef[assembly.OutValues.Count];
            argumentTypes = stackalloc LLVMTypeRef[assembly.OutValues.Count];
            assemblyString = new StringBuilder();
            constraintString = new StringBuilder();
            for (i = 0; i < assembly.OutValues.Count; i++)
            {
                var output = assembly.OutValues[i];
                arguments[i] = GetValue(output.Value, values, allocations, functionPointer);
                var type = output.Value.Type;
                argumentTypes[i] = LLVM.PointerType(_types[type.TypeIndex], 0);

                switch (type.TypeKind)
                {
                    case TypeKind.Void:
                    case TypeKind.Boolean:
                    case TypeKind.Integer:
                    case TypeKind.Enum:
                    case TypeKind.Type:
                        assemblyString.Append("mov ");
                        break;
                    case TypeKind.Float:
                        if (type.Size == 4)
                        {
                            assemblyString.Append("movss ");
                        }
                        else
                        {
                            assemblyString.Append("movsd ");
                        }
                        break;
                    default:
                        assemblyString.Append("mov ");
                        break;
                }
                assemblyString.AppendFormat("%{0}, ${1}; ", output.Register, i);
                constraintString.Append("=*m,");
            }
            constraintString.Remove(constraintString.Length-1, 1);

            var assemblyOutputType = LLVMTypeRef.CreateFunction(LLVM.VoidType(), argumentTypes, false);
            BuildAssemblyCall(assemblyString.ToString(), constraintString.ToString(), assemblyOutputType, arguments, LLVMInlineAsmDialect.LLVMInlineAsmDialectATT);
        }
    }

    private static LLVMValueRef BuildAssemblyCall(string assembly, string constraint, LLVMTypeRef assemblyFunctionType, ReadOnlySpan<LLVMValueRef> arguments, LLVMInlineAsmDialect dialect = LLVMInlineAsmDialect.LLVMInlineAsmDialectIntel)
    {
        using var assemblyString = new MarshaledString(assembly);
        using var constraintString = new MarshaledString(constraint);
        using var name = new MarshaledString(string.Empty);

        var asm = LLVM.GetInlineAsm(assemblyFunctionType, assemblyString.Value, (UIntPtr)assemblyString.Length, constraintString.Value, (UIntPtr)constraintString.Length, 1, 0, dialect);
        if (arguments.Length > 0)
        {
            fixed (LLVMValueRef* pArgs = &arguments[0])
            {
                return LLVM.BuildCall2(_builder, assemblyFunctionType, asm, (LLVMOpaqueValue**)pArgs, (uint)arguments.Length, name);
            }
        }

        return LLVM.BuildCall2(_builder, assemblyFunctionType, asm, null, 0, name);
    }

    private static void CreateTemporaryDebugStructType(StructAst structAst)
    {
        var file = _debugFiles[structAst.FileIndex];
        var pointer = stackalloc sbyte[structAst.Name.Length];
        for (var i = 0; i < structAst.Name.Length; i++)
        {
            pointer[i] = (sbyte)structAst.Name[i];
        }
        _debugTypes[structAst.TypeIndex] = LLVM.DIBuilderCreateReplaceableCompositeType(_debugBuilder, (uint)DwarfTag.Structure_type, pointer, structAst.Name, null, file, structAst.Line, 0, structAst.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, UIntPtr.Zero);
    }

    private static void CreateDebugStructType(StructAst structAst)
    {
        var file = _debugFiles[structAst.FileIndex];
        Span<LLVMMetadataRef> fields = stackalloc LLVMMetadataRef[structAst.Fields.Count];

        if (fields.Length > 0)
        {
            var structDecl = _debugTypes[structAst.TypeIndex];
            for (var i = 0; i < fields.Length; i++)
            {
                var structField = structAst.Fields[i];
                fields[i] = DeclareDebugField(structField.Name, structField.Line, structField.Offset * 8, structField.Type, structDecl, file);
            }
        }

        fixed (LLVMMetadataRef* fieldsPointer = fields)
        {
            var pointer = stackalloc sbyte[structAst.Name.Length];
            for (var i = 0; i < structAst.Name.Length; i++)
            {
                pointer[i] = (sbyte)structAst.Name[i];
            }
            var debugStruct = LLVM.DIBuilderCreateStructType(_debugBuilder, null, pointer, structAst.Name, file, structAst.Line, structAst.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)fieldsPointer, (uint)fields.Length, 0, null, null, UIntPtr.Zero);
            if (fields.Length > 0)
            {
                LLVM.MetadataReplaceAllUsesWith(_debugTypes[structAst.TypeIndex], debugStruct);
            }
            _debugTypes[structAst.TypeIndex] = debugStruct;
        }
    }

    private static LLVMMetadataRef DeclareDebugField(String name, uint line, uint offset, IType type, LLVMMetadataRef structDecl, LLVMMetadataRef file)
    {
        var pointer = stackalloc sbyte[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            pointer[i] = (sbyte)name[i];
        }
        return LLVM.DIBuilderCreateMemberType(_debugBuilder, structDecl, pointer, name, file, line, type.Size * 8, 0, offset, LLVMDIFlags.LLVMDIFlagZero, _debugTypes[type.TypeIndex]);
    }

    private static void CreateDebugEnumType(EnumAst enumAst)
    {
        var file = _debugFiles[enumAst.FileIndex];
        Span<LLVMMetadataRef> enumValues = stackalloc LLVMMetadataRef[enumAst.Values.Count];
        var isUnsigned = enumAst.BaseType.Signed ? 0 : 1;

        foreach (var (name, value) in enumAst.Values)
        {
            enumValues[value.Index] = DeclareDebugEnumValue(name, value.Value, isUnsigned);
        }

        fixed (LLVMMetadataRef* enumValuesPointer = enumValues)
        {
            var pointer = stackalloc sbyte[enumAst.Name.Length];
            for (var i = 0; i < enumAst.Name.Length; i++)
            {
                pointer[i] = (sbyte)enumAst.Name[i];
            }
            _debugTypes[enumAst.TypeIndex] = LLVM.DIBuilderCreateEnumerationType(_debugBuilder, null, pointer, enumAst.Name, file, enumAst.Line, enumAst.Size * 8, 0, (LLVMOpaqueMetadata**)enumValuesPointer, (uint)enumValues.Length, _debugTypes[enumAst.BaseType.TypeIndex]);
        }
    }

    private static LLVMMetadataRef DeclareDebugEnumValue(String name, int value, int isUnsigned)
    {
        var pointer = stackalloc sbyte[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            pointer[i] = (sbyte)name[i];
        }
        return LLVM.DIBuilderCreateEnumerator(_debugBuilder, pointer, name, value, isUnsigned);
    }

    private static void CreateDebugBasicType(PrimitiveAst type)
    {
        var pointer = stackalloc sbyte[type.Name.Length];
        for (var i = 0; i < type.Name.Length; i++)
        {
            pointer[i] = (sbyte)type.Name[i];
        }
        switch (type.TypeKind)
        {
            case TypeKind.Void:
                _debugTypes[type.TypeIndex] = null;
                break;
            case TypeKind.Boolean:
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreateBasicType(_debugBuilder, pointer, type.Name, 8, (uint)DwarfTypeEncoding.Boolean, LLVMDIFlags.LLVMDIFlagZero);
                break;
            case TypeKind.Integer:
            case TypeKind.Type:
                var encoding = type.Signed ? DwarfTypeEncoding.Signed : DwarfTypeEncoding.Unsigned;
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreateBasicType(_debugBuilder, pointer, type.Name, type.Size * 8, (uint)encoding, LLVMDIFlags.LLVMDIFlagZero);
                break;
            case TypeKind.Float:
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreateBasicType(_debugBuilder, pointer, type.Name, type.Size * 8, (uint)DwarfTypeEncoding.Float, LLVMDIFlags.LLVMDIFlagZero);
                break;
            case TypeKind.Pointer:
                break;
        }
    }

    private static void CreatePointerDebugType(PointerType pointerType)
    {
        var pointedType = _debugTypes[pointerType.PointedType.TypeIndex];
        var pointer = stackalloc sbyte[pointerType.Name.Length];
        for (var i = 0; i < pointerType.Name.Length; i++)
        {
            pointer[i] = (sbyte)pointerType.Name[i];
        }
        _debugTypes[pointerType.TypeIndex] = LLVM.DIBuilderCreatePointerType(_debugBuilder, pointedType, 64, 0, 0, pointer, pointerType.Name);
    }

    private static void CreateArrayDebugType(ArrayType arrayType)
    {
        var elementType = _debugTypes[arrayType.ElementType.TypeIndex];
        _debugTypes[arrayType.TypeIndex] = LLVM.DIBuilderCreateArrayType(_debugBuilder, arrayType.Length, 0, elementType, null, 0);
    }

    private static void CreateTemporaryDebugUnionType(UnionAst union)
    {
        var file = _debugFiles[union.FileIndex];
        var pointer = stackalloc sbyte[union.Name.Length];
        for (var i = 0; i < union.Name.Length; i++)
        {
            pointer[i] = (sbyte)union.Name[i];
        }
        _debugTypes[union.TypeIndex] = LLVM.DIBuilderCreateReplaceableCompositeType(_debugBuilder, (uint)DwarfTag.Union_type, pointer, union.Name, null, file, union.Line, 0, union.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, UIntPtr.Zero);
    }

    private static void DeclareCompoundDebugType(CompoundType compoundType)
    {
        var file = _debugFiles[0];
        Span<LLVMMetadataRef> types = stackalloc LLVMMetadataRef[compoundType.Types.Length];

        uint offset = 0;
        for (var i = 0; i < types.Length; i++)
        {
            var subType = compoundType.Types[i];
            var size = subType.Size * 8;
            types[i] = DeclareDebugField(subType.Name, 0, offset, subType, file, file);
            offset += size;
        }

        fixed (LLVMMetadataRef* typesPointer = types)
        {
            var pointer = stackalloc sbyte[compoundType.Name.Length];
            for (var i = 0; i < compoundType.Name.Length; i++)
            {
                pointer[i] = (sbyte)compoundType.Name[i];
            }
            _debugTypes[compoundType.TypeIndex] = LLVM.DIBuilderCreateStructType(_debugBuilder, null, pointer, compoundType.Name, file, 0, compoundType.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)typesPointer, (uint)types.Length, 0, null, null, UIntPtr.Zero);
        }
    }

    private static void DeclareInterfaceDebugType(InterfaceAst interfaceAst)
    {
        Span<LLVMMetadataRef> debugArgumentTypes = stackalloc LLVMMetadataRef[interfaceAst.Arguments.Count + 1];
        debugArgumentTypes[0] = _debugTypes[interfaceAst.ReturnType.TypeIndex];

        for (var i = 0; i < interfaceAst.Arguments.Count; i++)
        {
            var argument = interfaceAst.Arguments[i];
            debugArgumentTypes[i + 1] = _debugTypes[argument.Type.TypeIndex];
        }

        var functionType = _debugBuilder.CreateSubroutineType(_debugFiles[interfaceAst.FileIndex], debugArgumentTypes, LLVMDIFlags.LLVMDIFlagZero);
        var pointer = stackalloc sbyte[interfaceAst.Name.Length];
        for (var i = 0; i < interfaceAst.Name.Length; i++)
        {
            pointer[i] = (sbyte)interfaceAst.Name[i];
        }
        _debugTypes[interfaceAst.TypeIndex] = LLVM.DIBuilderCreatePointerType(_debugBuilder, functionType, 64, 0, 0, pointer, interfaceAst.Name);
    }

    private static void DeclareUnionDebugType(UnionAst union)
    {
        var file = _debugFiles[union.FileIndex];
        Span<LLVMMetadataRef> debugFields = stackalloc LLVMMetadataRef[union.Fields.Count];

        var structDecl = _debugTypes[union.TypeIndex];
        for (var i = 0; i < debugFields.Length; i++)
        {
            var field = union.Fields[i];
            debugFields[i] = DeclareDebugField(field.Name, field.Line, 0, field.Type, structDecl, file);
        }

        fixed (LLVMMetadataRef* fieldsPointer = debugFields)
        {
            var pointer = stackalloc sbyte[union.Name.Length];
            for (var i = 0; i < union.Name.Length; i++)
            {
                pointer[i] = (sbyte)union.Name[i];
            }
            var debugUnion = LLVM.DIBuilderCreateStructType(_debugBuilder, null, pointer, union.Name, file, union.Line, union.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)fieldsPointer, (uint)debugFields.Length, 0, null, null, UIntPtr.Zero);
            LLVM.MetadataReplaceAllUsesWith(_debugTypes[union.TypeIndex], debugUnion);
            _debugTypes[union.TypeIndex] = debugUnion;
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
        Union_type = 0x17,
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
