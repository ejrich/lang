using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    private static LLVMValueRef _stackPointer;
    private static LLVMTypeRef _stringType;
    private static LLVMTypeRef _typeInfoPointerType;
    private static LLVMTypeRef[] _types;
    private static LLVMValueRef[] _typeInfos;
    private static LLVMValueRef[] _globals;
    private static Queue<(LLVMValueRef, FunctionIR)> _functionsToWrite;

    private static bool _emitDebug;
    private static LLVMDIBuilderRef _debugBuilder;
    private static LLVMMetadataRef _debugCompilationUnit;
    private static List<LLVMMetadataRef> _debugFiles;
    private static LLVMMetadataRef[] _debugTypes;
    private static LLVMMetadataRef[] _debugFunctions;

    private static readonly LLVMTypeRef _u8PointerType = LLVM.PointerType(LLVM.Int8Type(), 0);
    private static readonly LLVMValueRef _zeroInt = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), 0, false);

    public static string Build()
    {
        // 1. Verify obj directory exists
        var objectPath = Path.Combine(BuildSettings.Path, ObjectDirectory);
        if (!Directory.Exists(objectPath))
            Directory.CreateDirectory(objectPath);

        // 2. Initialize the LLVM module and builder
        InitLLVM(objectPath);

        // 3. Declare types
        _types = new LLVMTypeRef[TypeTable.Count];
        _typeInfos = new LLVMValueRef[TypeTable.Count];

        const string structTypeInfoName = "StructTypeInfo";
        TypeTable.Types.Remove(structTypeInfoName, out var structTypeInfo);
        var structTypeInfoType = _types[structTypeInfo.TypeIndex] = _context.CreateNamedStruct(structTypeInfoName);
        CreateTypeInfo(structTypeInfoType, structTypeInfo.TypeIndex);

        var (typeInfoType, typeInfo) = CreateStructAndTypeInfo("TypeInfo", structTypeInfoType);
        var (typeInfoArrayType, typeInfoArray) = CreateStructAndTypeInfo("Array.*.TypeInfo", structTypeInfoType);
        var (integerTypeInfoType, integerTypeInfo) = CreateStructAndTypeInfo("IntegerTypeInfo", structTypeInfoType);
        var (pointerTypeInfoType, pointerTypeInfo) = CreateStructAndTypeInfo("PointerTypeInfo", structTypeInfoType);
        var (arrayTypeInfoType, arrayTypeInfo) = CreateStructAndTypeInfo("CArrayTypeInfo", structTypeInfoType);
        var (enumTypeInfoType, enumTypeInfo) = CreateStructAndTypeInfo("EnumTypeInfo", structTypeInfoType);
        var (compoundTypeInfoType, compoundTypeInfo) = CreateStructAndTypeInfo("CompoundTypeInfo", structTypeInfoType);
        var (stringType, stringStruct) = CreateStructAndTypeInfo("string", structTypeInfoType);
        var (stringArrayType, stringArray) = CreateStructAndTypeInfo("Array.string", structTypeInfoType);
        var (enumValueType, enumValue) = CreateStructAndTypeInfo("EnumValue", structTypeInfoType);
        var (enumValueArrayType, enumValueArray) = CreateStructAndTypeInfo("Array.EnumValue", structTypeInfoType);
        var (unionTypeInfoType, unionTypeInfo) = CreateStructAndTypeInfo("UnionTypeInfo", structTypeInfoType);
        var (unionFieldType, unionField) = CreateStructAndTypeInfo("UnionField", structTypeInfoType);
        var (unionFieldArrayType, unionFieldArray) = CreateStructAndTypeInfo("Array.UnionField", structTypeInfoType);
        var (interfaceTypeInfoType, interfaceTypeInfo) = CreateStructAndTypeInfo("InterfaceTypeInfo", structTypeInfoType);

        _stringType = stringType;
        var defaultAttributes = LLVMValueRef.CreateConstNamedStruct(stringArrayType, new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(stringType, 0))});

        var interfaceQueue = new List<InterfaceAst>();
        var structQueue = new List<StructAst>();
        var unionQueue = new List<UnionAst>();

        if (_emitDebug)
        {
            CreateTemporaryDebugStructType((StructAst)structTypeInfo);
            foreach (var (name, type) in TypeTable.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        _types[structAst.TypeIndex] = _context.CreateNamedStruct(name);
                        CreateTypeInfo(structTypeInfoType, structAst.TypeIndex);
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
                        DeclareEnum(enumAst, enumValueType, enumValueArrayType, enumTypeInfoType, stringArrayType, defaultAttributes);
                        CreateDebugEnumType(enumAst);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive, integerTypeInfoType, pointerTypeInfoType, typeInfoType);
                        CreateDebugBasicType(primitive, name);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType, arrayTypeInfoType);

                        var elementType = _debugTypes[arrayType.ElementType.TypeIndex];
                        _debugTypes[arrayType.TypeIndex] = LLVM.DIBuilderCreateArrayType(_debugBuilder, arrayType.Length, 0, elementType, null, 0);
                        break;
                    case UnionAst union:
                        DeclareUnion(union, unionTypeInfoType);
                        unionQueue.Add(union);

                        using (var structName = new MarshaledString(union.Name))
                        {
                            var file = _debugFiles[union.FileIndex];
                            _debugTypes[union.TypeIndex] = LLVM.DIBuilderCreateReplaceableCompositeType(_debugBuilder, (uint)DwarfTag.Union_type, structName.Value, (UIntPtr)structName.Length, null, file, union.Line, 0, union.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, UIntPtr.Zero);
                        }
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundType(compoundType, typeInfoType, typeInfoArrayType, compoundTypeInfoType);

                        using (var typeName = new MarshaledString(compoundType.Name))
                        {
                            var file = _debugFiles[0];
                            var types = new LLVMMetadataRef[compoundType.Types.Length];

                            uint offset = 0;
                            for (var i = 0; i < types.Length; i++)
                            {
                                var subType = compoundType.Types[i];
                                var size = subType.Size * 8;
                                using var subTypeName = new MarshaledString(subType.Name);

                                types[i] = LLVM.DIBuilderCreateMemberType(_debugBuilder, file, subTypeName.Value, (UIntPtr)subTypeName.Length, file, 0, size, 0, offset, LLVMDIFlags.LLVMDIFlagZero, _debugTypes[subType.TypeIndex]);
                                offset += size;
                            }

                            fixed (LLVMMetadataRef* typesPointer = types)
                            {
                                _debugTypes[compoundType.TypeIndex] = LLVM.DIBuilderCreateStructType(_debugBuilder, null, typeName.Value, (UIntPtr)typeName.Length, file, 0, compoundType.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)typesPointer, (uint)types.Length, 0, null, null, UIntPtr.Zero);
                            }
                        }
                        break;
                    case InterfaceAst interfaceAst:
                        DeclareInterfaceType(interfaceAst, interfaceTypeInfoType, interfaceQueue);

                        var debugArgumentTypes = new LLVMMetadataRef[interfaceAst.Arguments.Count + 1];
                        debugArgumentTypes[0] = _debugTypes[interfaceAst.ReturnType.TypeIndex];

                        for (var i = 0; i < interfaceAst.Arguments.Count; i++)
                        {
                            var argument = interfaceAst.Arguments[i];
                            debugArgumentTypes[i + 1] = _debugTypes[argument.Type.TypeIndex];
                        }

                        var functionType = _debugBuilder.CreateSubroutineType(_debugFiles[interfaceAst.FileIndex], debugArgumentTypes, LLVMDIFlags.LLVMDIFlagZero);
                        using (var interfaceName = new MarshaledString(interfaceAst.Name))
                        {
                            _debugTypes[interfaceAst.TypeIndex] = LLVM.DIBuilderCreatePointerType(_debugBuilder, functionType, 64, 0, 0, interfaceName.Value, (UIntPtr)interfaceName.Length);
                        }
                        break;
                }
            }
        }
        else
        {
            foreach (var (name, type) in TypeTable.Types)
            {
                switch (type)
                {
                    case StructAst structAst:
                        _types[structAst.TypeIndex] = _context.CreateNamedStruct(name);
                        CreateTypeInfo(structTypeInfoType, structAst.TypeIndex);
                        structQueue.Add(structAst);
                        break;
                    case EnumAst enumAst:
                        DeclareEnum(enumAst, enumValueType, enumValueArrayType, enumTypeInfoType, stringArrayType, defaultAttributes);
                        break;
                    case PrimitiveAst primitive:
                        DeclarePrimitive(primitive, integerTypeInfoType, pointerTypeInfoType, typeInfoType);
                        break;
                    case ArrayType arrayType:
                        DeclareArrayType(arrayType, arrayTypeInfoType);
                        break;
                    case UnionAst union:
                        DeclareUnion(union, unionTypeInfoType);
                        unionQueue.Add(union);
                        break;
                    case CompoundType compoundType:
                        DeclareCompoundType(compoundType, typeInfoType, typeInfoArrayType, compoundTypeInfoType);
                        break;
                    case InterfaceAst interfaceAst:
                        DeclareInterfaceType(interfaceAst, interfaceTypeInfoType, interfaceQueue);
                        break;
                }
            }
        }

        foreach (var interfaceAst in interfaceQueue)
        {
            var argumentTypes = interfaceAst.Arguments.Select(arg => _types[arg.Type.TypeIndex]).ToArray();
            var functionType = LLVMTypeRef.CreateFunction(_types[interfaceAst.ReturnType.TypeIndex], argumentTypes, false);
            _types[interfaceAst.TypeIndex] = LLVM.PointerType(functionType, 0);
        }

        var typeField = _module.GetTypeByName("TypeField");
        var typeFieldArray = _module.GetTypeByName("Array.TypeField");
        var defaultFields = LLVMValueRef.CreateConstNamedStruct(typeFieldArray, new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(typeField, 0))});

        SetStructTypeFields(typeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(typeInfoArray, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(integerTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(pointerTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(arrayTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(enumTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(unionTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields((StructAst)structTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(compoundTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(stringStruct, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(stringArray, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(enumValue, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(enumValueArray, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(unionField, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(unionFieldArray, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        SetStructTypeFields(interfaceTypeInfo, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);

        foreach (var structAst in structQueue)
        {
            SetStructTypeFields(structAst, typeField, typeFieldArray, defaultFields, stringArrayType, defaultAttributes, structTypeInfoType);
        }

        foreach (var union in unionQueue)
        {
            var typeName = GetString(union.Name);

            var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Union, 0);
            var typeSize = LLVM.ConstInt(LLVM.Int32Type(), union.Size, 0);
            var unionFields = new LLVMValueRef[union.Fields.Count];

            for (var i = 0; i < union.Fields.Count; i++)
            {
                var field = union.Fields[i];

                var fieldNameString = GetString(field.Name);

                unionFields[i] = LLVMValueRef.CreateConstNamedStruct(unionFieldType, new LLVMValueRef[] {fieldNameString, _typeInfos[field.Type.TypeIndex]});
            }

            var fieldArray = LLVMValueRef.CreateConstArray(unionFieldType, unionFields);
            var unionFieldArrayGlobal = _module.AddGlobal(LLVM.TypeOf(fieldArray), "____union_fields");
            SetPrivateConstant(unionFieldArrayGlobal);
            LLVM.SetInitializer(unionFieldArrayGlobal, fieldArray);

            var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, };
            var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(unionTypeInfoType, fields);

            LLVM.SetInitializer(_typeInfos[union.TypeIndex], typeInfoStruct);

            if (_emitDebug)
            {
                using var unionName = new MarshaledString(union.Name);

                var file = _debugFiles[union.FileIndex];
                var debugFields = new LLVMMetadataRef[union.Fields.Count];

                var structDecl = _debugTypes[union.TypeIndex];
                for (var i = 0; i < debugFields.Length; i++)
                {
                    var field = union.Fields[i];
                    using var fieldName = new MarshaledString(field.Name);

                    debugFields[i] = LLVM.DIBuilderCreateMemberType(_debugBuilder, structDecl, fieldName.Value, (UIntPtr)fieldName.Length, file, field.Line, field.Type.Size * 8, 0, 0, LLVMDIFlags.LLVMDIFlagZero, _debugTypes[field.Type.TypeIndex]);
                }

                fixed (LLVMMetadataRef* fieldsPointer = debugFields)
                {
                     var debugUnion = LLVM.DIBuilderCreateStructType(_debugBuilder, null, unionName.Value, (UIntPtr)unionName.Length, file, union.Line, union.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)fieldsPointer, (uint)debugFields.Length, 0, null, null, UIntPtr.Zero);
                     LLVM.MetadataReplaceAllUsesWith(_debugTypes[union.TypeIndex], debugUnion);
                    _debugTypes[union.TypeIndex] = debugUnion;
                }
            }
        }

        var functionTypeInfoType = _module.GetTypeByName("FunctionTypeInfo");
        var argumentType = _module.GetTypeByName("ArgumentType");
        var argumentArrayType = _module.GetTypeByName("Array.ArgumentType");
        var functionTypeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Function, 0);

        foreach (var interfaceAst in interfaceQueue)
        {
            var returnType = _typeInfos[interfaceAst.ReturnType.TypeIndex];

            var argumentCount = interfaceAst.Arguments.Count;
            var argumentValues = new LLVMValueRef[argumentCount];
            for (var arg = 0; arg < argumentCount; arg++)
            {
                var argument = interfaceAst.Arguments[arg];

                var argNameString = GetString(argument.Name);
                var argumentTypeInfo = _typeInfos[argument.Type.TypeIndex];
                var argumentValue = LLVMValueRef.CreateConstNamedStruct(argumentType, new LLVMValueRef[] {argNameString, argumentTypeInfo});

                argumentValues[arg] = argumentValue;
            }

            var argumentArray = LLVMValueRef.CreateConstArray(argumentType, argumentValues);
            var argumentArrayGlobal = _module.AddGlobal(LLVM.TypeOf(argumentArray), "____type_fields");
            SetPrivateConstant(argumentArrayGlobal);
            LLVM.SetInitializer(argumentArrayGlobal, argumentArray);

            var arguments = LLVMValueRef.CreateConstNamedStruct(argumentArrayType, new LLVMValueRef[]
            {
                LLVM.ConstInt(LLVM.Int32Type(), (ulong)interfaceAst.Arguments.Count, 0),
                argumentArrayGlobal
            });

            var typeNameString = GetString(interfaceAst.Name);
            var fields = new LLVMValueRef[]{typeNameString, functionTypeKind, _zeroInt, returnType, arguments};

            var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(interfaceTypeInfoType, fields);
            LLVM.SetInitializer(_typeInfos[interfaceAst.TypeIndex], typeInfoStruct);
        }

        foreach (var (_, functions) in TypeTable.Functions)
        {
            for (var i = 0; i < functions.Count; i++)
            {
                var function = functions[i];

                var returnType = _typeInfos[function.ReturnType.TypeIndex];

                var argumentCount = function.Flags.HasFlag(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
                var argumentValues = new LLVMValueRef[argumentCount];
                for (var arg = 0; arg < argumentCount; arg++)
                {
                    var argument = function.Arguments[arg];

                    var argNameString = GetString(argument.Name);
                    var argumentTypeInfo = _typeInfos[argument.Type.TypeIndex];
                    var argumentValue = LLVMValueRef.CreateConstNamedStruct(argumentType, new LLVMValueRef[] {argNameString, argumentTypeInfo});

                    argumentValues[arg] = argumentValue;
                }

                var argumentArray = LLVMValueRef.CreateConstArray(argumentType, argumentValues);
                var argumentArrayGlobal = _module.AddGlobal(LLVM.TypeOf(argumentArray), "____type_fields");
                SetPrivateConstant(argumentArrayGlobal);
                LLVM.SetInitializer(argumentArrayGlobal, argumentArray);

                var arguments = LLVMValueRef.CreateConstNamedStruct(argumentArrayType, new LLVMValueRef[]
                {
                    LLVM.ConstInt(LLVM.Int32Type(), (ulong)function.Arguments.Count, 0),
                    argumentArrayGlobal
                });

                LLVMValueRef attributes;
                if (function.Attributes != null)
                {
                    var attributeRefs = new LLVMValueRef[function.Attributes.Count];

                    for (var attributeIndex = 0; attributeIndex < attributeRefs.Length; attributeIndex++)
                    {
                        attributeRefs[attributeIndex] = GetString(function.Attributes[attributeIndex]);
                    }

                    var attributesArray = LLVMValueRef.CreateConstArray(_stringType, attributeRefs);
                    var attributesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(attributesArray), "____function_attributes");
                    SetPrivateConstant(attributesArrayGlobal);
                    LLVM.SetInitializer(attributesArrayGlobal, attributesArray);

                    attributes = LLVMValueRef.CreateConstNamedStruct(stringArrayType, new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)attributeRefs.Length, 0), attributesArrayGlobal
                    });
                }
                else
                {
                    attributes = defaultAttributes;
                }

                var typeNameString = GetString(function.Name);

                var fields = new LLVMValueRef[]{typeNameString, functionTypeKind, _zeroInt, returnType, arguments, attributes};

                CreateAndSetTypeInfo(functionTypeInfoType, fields, function.TypeIndex);
            }
        }

        // 4. Declare variables
        LLVMValueRef typeTable = null;
        _globals = new LLVMValueRef[Program.GlobalVariables.Count];
        for (var i = 0; i < Program.GlobalVariables.Count; i++)
        {
            var globalVariable = Program.GlobalVariables[i];
            LLVMValueRef global;
            if (globalVariable.Array)
            {
                var elementType = _types[globalVariable.Type.TypeIndex];
                global = _module.AddGlobal(LLVM.ArrayType(elementType, globalVariable.ArrayLength), globalVariable.Name);

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
                global = _module.AddGlobal(_types[globalVariable.Type.TypeIndex], globalVariable.Name);
            }

            if (globalVariable.InitialValue != null)
            {
                var initialValue = GetConstantValue(globalVariable.InitialValue);
                LLVM.SetInitializer(global, initialValue);
            }

            if (_emitDebug && globalVariable.FileIndex.HasValue)
            {
                using var name = new MarshaledString(globalVariable.Name);
                var file = _debugFiles[globalVariable.FileIndex.Value];
                var debugType = _debugTypes[globalVariable.Type.TypeIndex];
                var globalDebug = LLVM.DIBuilderCreateGlobalVariableExpression(_debugBuilder, _debugCompilationUnit, name.Value, (UIntPtr)name.Length, null, UIntPtr.Zero, file, globalVariable.Line, debugType, 0, null, null, 0);
                LLVM.GlobalSetMetadata(global, 0, globalDebug);
            }

            LLVM.SetLinkage(global, LLVMLinkage.LLVMPrivateLinkage);
            _globals[i] = global;

            if (globalVariable.Name == "__type_table")
            {
                typeTable = global;
                SetPrivateConstant(typeTable);
            }
        }

        // 5. Write type table
        _typeInfoPointerType = LLVM.PointerType(typeInfoType, 0);
        var typeArray = LLVMValueRef.CreateConstArray(_typeInfoPointerType, _typeInfos);
        var typeArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typeArray), "____type_array");
        SetPrivateConstant(typeArrayGlobal);
        LLVM.SetInitializer(typeArrayGlobal, typeArray);

        var typeCount = LLVM.ConstInt(LLVM.Int32Type(), (ulong)_typeInfos.Length, 0);
        LLVM.SetInitializer(typeTable, LLVMValueRef.CreateConstNamedStruct(typeInfoArrayType, new LLVMValueRef[] {typeCount, typeArrayGlobal}));

        // 6. Write the program beginning at the entrypoint
        _functionsToWrite = new();
        WriteFunctionDefinition("__start", Program.EntryPoint);
        while (_functionsToWrite.Any())
        {
            var (functionPointer, function) = _functionsToWrite.Dequeue();
            WriteFunction(functionPointer, function);
        }

        // 7. Compile to object file
        var objectFile = Path.Combine(objectPath, $"{BuildSettings.Name}.o");
        Compile(objectFile, BuildSettings.OutputAssembly);

        return objectFile;
    }

    private static void InitLLVM(string objectPath)
    {
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

            LLVM.InitializeFunctionPassManager(_passManager);
            _codeGenLevel = LLVMCodeGenOptLevel.LLVMCodeGenLevelAggressive;
        }
        else
        {
            _emitDebug = true;
            _debugBuilder = _module.CreateDIBuilder();
            _debugFiles = BuildSettings.Files.Select(file => _debugBuilder.CreateFile(Path.GetFileName(file), Path.GetDirectoryName(file))).ToList();
            _debugCompilationUnit = _debugBuilder.CreateCompileUnit(LLVMDWARFSourceLanguage.LLVMDWARFSourceLanguageC, _debugFiles[0], "ol", 0, string.Empty, 0, string.Empty, LLVMDWARFEmissionKind.LLVMDWARFEmissionFull, 0, 0, 0, string.Empty, string.Empty);

            AddModuleFlag("Dwarf Version", 4);
            AddModuleFlag("Debug Info Version", LLVM.DebugMetadataVersion());
            AddModuleFlag("PIE Level", 2);

            _debugTypes = new LLVMMetadataRef[TypeTable.Count];
            _debugFunctions = new LLVMMetadataRef[Program.FunctionCount];
        }
    }

    private static void AddModuleFlag(string flagName, uint flagValue)
    {
        using var name = new MarshaledString(flagName);
        var value = LLVM.ValueAsMetadata(LLVM.ConstInt(LLVM.Int32Type(), flagValue, 0));
        LLVM.AddModuleFlag(_module, LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, name.Value, (UIntPtr)name.Length, value);
    }

    private static (LLVMTypeRef, StructAst) CreateStructAndTypeInfo(string typeName, LLVMTypeRef typeInfoType)
    {
        TypeTable.Types.Remove(typeName, out var typeInfo);
        var typeStruct = _types[typeInfo.TypeIndex] = _context.CreateNamedStruct(typeName);

        CreateTypeInfo(typeInfoType, typeInfo.TypeIndex);

        if (_emitDebug)
        {
            CreateTemporaryDebugStructType((StructAst)typeInfo);
        }
        return (typeStruct, (StructAst)typeInfo);
    }

    private static LLVMValueRef CreateTypeInfo(LLVMTypeRef typeInfoType, int typeIndex)
    {
        var typeInfo = _module.AddGlobal(typeInfoType, "____type_info");
        SetPrivateConstant(typeInfo);
        return _typeInfos[typeIndex] = typeInfo;
    }

    private static void DeclareEnum(EnumAst enumAst, LLVMTypeRef enumValueType, LLVMTypeRef enumValueArrayType, LLVMTypeRef enumTypeInfoType, LLVMTypeRef stringArrayType, LLVMValueRef defaultAttributes)
    {
        _types[enumAst.TypeIndex] = GetIntegerType(enumAst.BaseType.Size);

        var typeName = GetString(enumAst.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Enum, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), enumAst.Size, 0);

        var enumValueRefs = new LLVMValueRef[enumAst.Values.Count];

        for (var i = 0; i < enumAst.Values.Count; i++)
        {
            var value = enumAst.Values[i];

            var enumValueNameString = GetString(value.Name);
            var enumValue = LLVM.ConstInt(LLVM.Int32Type(), (uint)value.Value, 0);

            enumValueRefs[i] = LLVMValueRef.CreateConstNamedStruct(enumValueType, new LLVMValueRef[] {enumValueNameString, enumValue});
        }

        var enumValuesArray = LLVMValueRef.CreateConstArray(enumValueType, enumValueRefs);
        var enumValuesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(enumValuesArray), "____enum_values");
        SetPrivateConstant(enumValuesArrayGlobal);
        LLVM.SetInitializer(enumValuesArrayGlobal, enumValuesArray);

        var valuesArray = LLVMValueRef.CreateConstNamedStruct(enumValueArrayType, new LLVMValueRef[]
        {
            LLVM.ConstInt(LLVM.Int32Type(), (ulong)enumValueRefs.Length, 0), enumValuesArrayGlobal
        });

        LLVMValueRef attributes;
        if (enumAst.Attributes != null)
        {
            var attributeRefs = new LLVMValueRef[enumAst.Attributes.Count];

            for (var i = 0; i < attributeRefs.Length; i++)
            {
                attributeRefs[i] = GetString(enumAst.Attributes[i]);
            }

            var attributesArray = LLVMValueRef.CreateConstArray(_stringType, attributeRefs);
            var attributesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(attributesArray), "____enum_attributes");
            SetPrivateConstant(attributesArrayGlobal);
            LLVM.SetInitializer(attributesArrayGlobal, attributesArray);

            attributes = LLVMValueRef.CreateConstNamedStruct(stringArrayType, new LLVMValueRef[]
            {
                LLVM.ConstInt(LLVM.Int32Type(), (ulong)attributeRefs.Length, 0), attributesArrayGlobal
            });
        }
        else
        {
            attributes = defaultAttributes;
        }

        var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, _typeInfos[enumAst.BaseType.TypeIndex], valuesArray, attributes};
        CreateAndSetTypeInfo(enumTypeInfoType, fields, enumAst.TypeIndex);
    }

    private static void DeclarePrimitive(PrimitiveAst primitive, LLVMTypeRef integerTypeInfoType, LLVMTypeRef pointerTypeInfoType, LLVMTypeRef typeInfoType)
    {
        _types[primitive.TypeIndex] = primitive.TypeKind switch
        {
            TypeKind.Void => LLVM.VoidType(),
            TypeKind.Boolean => LLVM.Int1Type(),
            TypeKind.Integer => GetIntegerType(primitive.Size),
            TypeKind.Float => primitive.Size == 4 ? LLVM.FloatType() : LLVM.DoubleType(),
            TypeKind.Pointer => GetPointerType(primitive.PointerType),
            TypeKind.Type => LLVM.Int32Type(),
            _ => null
        };

        var typeName = GetString(primitive.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)primitive.TypeKind, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), primitive.Size, 0);

        switch (primitive.TypeKind)
        {
            case TypeKind.Integer:
            {
                var signed = LLVM.ConstInt(LLVM.Int1Type(), (byte)(primitive.Signed ? 1 : 0), 0);
                var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, signed};
                CreateAndSetTypeInfo(integerTypeInfoType, fields, primitive.TypeIndex);
                break;
            }
            case TypeKind.Pointer:
            {
                var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, _typeInfos[primitive.PointerType.TypeIndex]};
                CreateAndSetTypeInfo(pointerTypeInfoType, fields, primitive.TypeIndex);
                break;
            }
            default:
            {
                var fields = new LLVMValueRef[]{typeName, typeKind, typeSize};
                CreateAndSetTypeInfo(typeInfoType, fields, primitive.TypeIndex);
                break;
            }
        }
    }

    private static void DeclareArrayType(ArrayType arrayType, LLVMTypeRef arrayTypeInfoType)
    {
        _types[arrayType.TypeIndex] = LLVM.ArrayType(_types[arrayType.ElementType.TypeIndex], arrayType.Length);

        var typeName = GetString(arrayType.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.CArray, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), arrayType.Size, 0);
        var arrayLength = LLVM.ConstInt(LLVM.Int32Type(), arrayType.Length, 0);

        var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, arrayLength, _typeInfos[arrayType.ElementType.TypeIndex]};
        CreateAndSetTypeInfo(arrayTypeInfoType, fields, arrayType.TypeIndex);
    }

    private static void DeclareUnion(UnionAst union, LLVMTypeRef unionTypeInfo)
    {
        var type = _types[union.TypeIndex] = _context.CreateNamedStruct(union.Name);

        type.StructSetBody(new []{LLVMTypeRef.CreateArray(LLVM.Int8Type(), union.Size)}, false);

        CreateTypeInfo(unionTypeInfo, union.TypeIndex);
    }

    private static void DeclareCompoundType(CompoundType compoundType, LLVMTypeRef typeInfo, LLVMTypeRef typeInfoArray, LLVMTypeRef compoundTypeInfo)
    {
        var types = new LLVMTypeRef[compoundType.Types.Length];
        var typeInfos = new LLVMValueRef[compoundType.Types.Length];

        for (var i = 0; i < types.Length; i++)
        {
            var type = compoundType.Types[i];
            types[i] = _types[type.TypeIndex];
            typeInfos[i] = _typeInfos[type.TypeIndex];
        }

        _types[compoundType.TypeIndex] = LLVMTypeRef.CreateStruct(types, true);

        var typesArray = LLVMValueRef.CreateConstArray(typeInfo, typeInfos);
        var typesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typesArray), "____enum_values");
        SetPrivateConstant(typesArrayGlobal);
        LLVM.SetInitializer(typesArrayGlobal, typesArray);

        var array = LLVMValueRef.CreateConstNamedStruct(typeInfoArray, new LLVMValueRef[]
        {
            LLVM.ConstInt(LLVM.Int32Type(), (ulong)typeInfos.Length, 0), typesArrayGlobal
        });

        var typeName = GetString(compoundType.Name);
        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)TypeKind.Compound, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), compoundType.Size, 0);

        var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, array};
        CreateAndSetTypeInfo(compoundTypeInfo, fields, compoundType.TypeIndex);
    }

    private static void DeclareInterfaceType(InterfaceAst interfaceAst, LLVMTypeRef typeInfoType, List<InterfaceAst> interfaceQueue)
    {
        interfaceQueue.Add(interfaceAst);

        CreateTypeInfo(typeInfoType, interfaceAst.TypeIndex);
    }

    private static void SetStructTypeFields(StructAst structAst, LLVMTypeRef typeFieldType, LLVMTypeRef typeFieldArrayType, LLVMValueRef defaultFields, LLVMTypeRef stringArrayType, LLVMValueRef defaultAttributes, LLVMTypeRef structTypeInfoType)
    {
        var typeName = GetString(structAst.Name);

        var typeKind = LLVM.ConstInt(LLVM.Int32Type(), (uint)structAst.TypeKind, 0);
        var typeSize = LLVM.ConstInt(LLVM.Int32Type(), structAst.Size, 0);
        LLVMValueRef structTypeInfoFields;

        if (structAst.Fields.Any())
        {
            var structFields = new LLVMTypeRef[structAst.Fields.Count];
            var typeFields = new LLVMValueRef[structAst.Fields.Count];

            for (var i = 0; i < structAst.Fields.Count; i++)
            {
                var field = structAst.Fields[i];
                structFields[i] = _types[field.Type.TypeIndex];

                var fieldNameString = GetString(field.Name);
                var fieldOffset = LLVM.ConstInt(LLVM.Int32Type(), field.Offset, 0);

                LLVMValueRef fieldAttributes;
                if (field.Attributes != null)
                {
                    var attributeRefs = new LLVMValueRef[field.Attributes.Count];

                    for (var attributeIndex = 0; attributeIndex < attributeRefs.Length; attributeIndex++)
                    {
                        attributeRefs[attributeIndex] = GetString(field.Attributes[attributeIndex]);
                    }

                    var attributesArray = LLVMValueRef.CreateConstArray(_stringType, attributeRefs);
                    var attributesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(attributesArray), "____field_attributes");
                    SetPrivateConstant(attributesArrayGlobal);
                    LLVM.SetInitializer(attributesArrayGlobal, attributesArray);

                    fieldAttributes = LLVMValueRef.CreateConstNamedStruct(stringArrayType, new LLVMValueRef[]
                    {
                        LLVM.ConstInt(LLVM.Int32Type(), (ulong)attributeRefs.Length, 0), attributesArrayGlobal
                    });
                }
                else
                {
                    fieldAttributes = defaultAttributes;
                }

                var typeField = LLVMValueRef.CreateConstNamedStruct(typeFieldType, new LLVMValueRef[] {fieldNameString, fieldOffset, _typeInfos[field.Type.TypeIndex], fieldAttributes});

                typeFields[i] = typeField;
            }
            _types[structAst.TypeIndex].StructSetBody(structFields, false);

            var typeFieldArray = LLVMValueRef.CreateConstArray(typeFieldType, typeFields);
            var typeFieldArrayGlobal = _module.AddGlobal(LLVM.TypeOf(typeFieldArray), "____type_fields");
            SetPrivateConstant(typeFieldArrayGlobal);
            LLVM.SetInitializer(typeFieldArrayGlobal, typeFieldArray);

            structTypeInfoFields = LLVMValueRef.CreateConstNamedStruct(typeFieldArrayType, new LLVMValueRef[]
            {
                LLVM.ConstInt(LLVM.Int32Type(), (ulong)structAst.Fields.Count, 0),
                typeFieldArrayGlobal
            });

            if (_emitDebug)
            {
                CreateDebugStructType(structAst);
            }
        }
        else
        {
            structTypeInfoFields = defaultFields;
        }

        LLVMValueRef attributes;
        if (structAst.Attributes != null)
        {
            var attributeRefs = new LLVMValueRef[structAst.Attributes.Count];

            for (var i = 0; i < attributeRefs.Length; i++)
            {
                attributeRefs[i] = GetString(structAst.Attributes[i]);
            }

            var attributesArray = LLVMValueRef.CreateConstArray(_stringType, attributeRefs);
            var attributesArrayGlobal = _module.AddGlobal(LLVM.TypeOf(attributesArray), "____struct_attributes");
            SetPrivateConstant(attributesArrayGlobal);
            LLVM.SetInitializer(attributesArrayGlobal, attributesArray);

            attributes = LLVMValueRef.CreateConstNamedStruct(stringArrayType, new LLVMValueRef[]
            {
                LLVM.ConstInt(LLVM.Int32Type(), (ulong)attributeRefs.Length, 0), attributesArrayGlobal
            });
        }
        else
        {
            attributes = defaultAttributes;
        }

        var fields = new LLVMValueRef[]{typeName, typeKind, typeSize, structTypeInfoFields, attributes};
        var typeInfoStruct = LLVMValueRef.CreateConstNamedStruct(structTypeInfoType, fields);

        var typeInfo = _typeInfos[structAst.TypeIndex];
        LLVM.SetInitializer(typeInfo, typeInfoStruct);
    }

    private static void CreateAndSetTypeInfo(LLVMTypeRef typeInfoType, LLVMValueRef[] fields, int typeIndex)
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

    private static LLVMTypeRef GetPointerType(IType pointerType)
    {
        if (pointerType.TypeKind == TypeKind.Void)
        {
            return LLVM.PointerType(LLVM.Int8Type(), 0);
        }

        return LLVM.PointerType(_types[pointerType.TypeIndex], 0);
    }

    private static void SetPrivateConstant(LLVMValueRef variable)
    {
        LLVM.SetLinkage(variable, LLVMLinkage.LLVMPrivateLinkage);
        LLVM.SetGlobalConstant(variable, 1);
        LLVM.SetUnnamedAddr(variable, 1);
    }

    private static LLVMValueRef WriteFunctionDefinition(string name, FunctionIR function)
    {
        var varargs = function.Source.Flags.HasFlag(FunctionFlags.Varargs);
        var sourceArguments = function.Source.Arguments;
        var argumentCount = varargs ? sourceArguments.Count - 1 : sourceArguments.Count;

        var argumentTypes = new LLVMTypeRef[argumentCount];
        LLVMValueRef functionPointer;

        if (_emitDebug && function.Instructions != null)
        {
            // Get the argument types and create debug symbols
            var debugArgumentTypes = new LLVMMetadataRef[argumentCount + 1];
            debugArgumentTypes[0] = _debugTypes[function.Source.ReturnType.TypeIndex];

            for (var i = 0; i < argumentCount; i++)
            {
                var argumentType = sourceArguments[i].Type.TypeIndex;
                argumentTypes[i] = _types[argumentType];
                debugArgumentTypes[i + 1] = _debugTypes[argumentType];
            }

            var file = _debugFiles[function.Source.FileIndex];
            var functionType = _debugBuilder.CreateSubroutineType(file, debugArgumentTypes, LLVMDIFlags.LLVMDIFlagZero);
            var debugFunction = _debugFunctions[function.Index] = _debugBuilder.CreateFunction(file, function.Source.Name, name, file, function.Source.Line, functionType, 0, 1, function.Source.Line, LLVMDIFlags.LLVMDIFlagPrototyped, 0);

            // Declare the function
            functionPointer = _module.AddFunction(name, LLVMTypeRef.CreateFunction(_types[function.Source.ReturnType.TypeIndex], argumentTypes, varargs));
            LLVM.SetSubprogram(functionPointer, debugFunction);
        }
        else
        {
            for (var i = 0; i < argumentCount; i++)
            {
                argumentTypes[i] = _types[sourceArguments[i].Type.TypeIndex];
            }
            functionPointer = _module.AddFunction(name, LLVMTypeRef.CreateFunction(_types[function.Source.ReturnType.TypeIndex], argumentTypes, varargs));
        }

        if (function.Instructions != null)
        {
            _functionsToWrite.Enqueue((functionPointer, function));
        }

        return functionPointer;
    }

    private static LLVMValueRef GetOrCreateFunctionDefinition(string name)
    {
        var functionPointer = _module.GetNamedFunction(name);
        if (functionPointer.Handle == IntPtr.Zero)
        {
            functionPointer = WriteFunctionDefinition(name, Program.Functions[name]);
        }

        return functionPointer;
    }

    private static void WriteFunction(LLVMValueRef functionPointer, FunctionIR function)
    {
        // Declare the basic blocks
        var basicBlocks = new LLVMBasicBlockRef[function.BasicBlocks.Count];
        foreach (var block in function.BasicBlocks)
        {
            basicBlocks[block.Index] = functionPointer.AppendBasicBlock(block.Index.ToString());
        }
        LLVM.PositionBuilderAtEnd(_builder, basicBlocks[0]);
        _builder.CurrentDebugLocation = null;

        // Allocate the function stack
        var allocations = new LLVMValueRef[function.Allocations.Count];
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

        if (function.SaveStack)
        {
            BuildStackSave();
        }

        LLVMMetadataRef debugBlock = null;
        LLVMMetadataRef file = null;
        LLVMMetadataRef expression = null;
        Stack<LLVMMetadataRef> debugBlockStack = null;
        if (_emitDebug)
        {
            debugBlock = _debugFunctions[function.Index];
            file = _debugFiles[function.Source.FileIndex];
            expression = LLVM.DIBuilderCreateExpression(_debugBuilder, null, UIntPtr.Zero);
            debugBlockStack = new();
        }

        // Write the instructions
        var blockIndex = 0;
        var instructionIndex = 0;
        var values = new LLVMValueRef[function.ValueCount];
        while (blockIndex < function.BasicBlocks.Count)
        {
            LLVM.PositionBuilderAtEnd(_builder, basicBlocks[blockIndex]); // Redundant for the first pass, not a big deal
            var instructionToStopAt = blockIndex < function.BasicBlocks.Count - 1 ? function.BasicBlocks[blockIndex + 1].Location : function.Instructions.Count;
            var breakToNextBlock = true;
            while (instructionIndex < instructionToStopAt)
            {
                var instruction = function.Instructions[instructionIndex++];

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
                            BuildStackRestore();
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
                            BuildStackRestore();
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
                        values[instruction.ValueIndex] = _builder.BuildGEP(pointer, instruction.GetFirstPointer ? new []{_zeroInt, index} : new []{index});
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
                        var callFunction = GetOrCreateFunctionDefinition(instruction.String);
                        var arguments = new LLVMValueRef[instruction.Value1.Values.Length];
                        for (var i = 0; i < instruction.Value1.Values.Length; i++)
                        {
                            arguments[i] = GetValue(instruction.Value1.Values[i], values, allocations, functionPointer);
                        }
                        values[instruction.ValueIndex] = _builder.BuildCall(callFunction, arguments);
                        break;
                    }
                    case InstructionType.CallFunctionPointer:
                    {
                        var callFunction = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var arguments = new LLVMValueRef[instruction.Value2.Values.Length];
                        for (var i = 0; i < instruction.Value2.Values.Length; i++)
                        {
                            arguments[i] = GetValue(instruction.Value2.Values[i], values, allocations, functionPointer);
                        }
                        values[instruction.ValueIndex] = _builder.BuildCall(callFunction, arguments);
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
                    case InstructionType.AllocateArray:
                    {
                        var length = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var elementType = _types[instruction.Value2.Type.TypeIndex];
                        values[instruction.ValueIndex] = _builder.BuildArrayAlloca(elementType, length);
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
                        var index = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildGEP(pointer, new []{index});
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
                        index = _builder.BuildNeg(index);
                        values[instruction.ValueIndex] = _builder.BuildGEP(pointer, new []{index});
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
                    case InstructionType.FloatModulus:
                    {
                        BuildSettings.Dependencies.Add("m");
                        var lhs = GetValue(instruction.Value1, values, allocations, functionPointer);
                        var rhs = GetValue(instruction.Value2, values, allocations, functionPointer);
                        values[instruction.ValueIndex] = _builder.BuildFRem(lhs, rhs);
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
                    case InstructionType.DebugSetLocation:
                    {
                        var location = LLVM.DIBuilderCreateDebugLocation(_context, instruction.Source.Line, instruction.Source.Column, debugBlock, null);
                        LLVM.SetCurrentDebugLocation2(_builder, location);
                        break;
                    }
                    case InstructionType.DebugPushLexicalBlock:
                    {
                        debugBlockStack.Push(debugBlock);
                        debugBlock = LLVM.DIBuilderCreateLexicalBlock(_debugBuilder, debugBlock, file, instruction.Source.Line, instruction.Source.Column);
                        break;
                    }
                    case InstructionType.DebugPopLexicalBlock:
                    {
                        debugBlock = debugBlockStack.Pop();
                        break;
                    }
                    case InstructionType.DebugDeclareParameter:
                    {
                        var argument = LLVM.GetParam(functionPointer, (uint)instruction.Index);
                        var functionArg = function.Source.Arguments[instruction.Index];

                        using var argName = new MarshaledString(functionArg.Name);

                        var debugType = _debugTypes[functionArg.Type.TypeIndex];
                        var debugVariable = LLVM.DIBuilderCreateParameterVariable(_debugBuilder, debugBlock, argName.Value, (UIntPtr)argName.Length, (uint)instruction.Index+1, file, functionArg.Line, debugType, 0, LLVMDIFlags.LLVMDIFlagZero);
                        var location = LLVM.DIBuilderCreateDebugLocation(_context, functionArg.Line, functionArg.Column, debugBlock, null);

                        LLVM.DIBuilderInsertDeclareAtEnd(_debugBuilder, allocations[instruction.Index], debugVariable, expression, location, basicBlocks[blockIndex]);
                        break;
                    }
                    case InstructionType.DebugDeclareVariable:
                    {
                        using var name = new MarshaledString(instruction.String);

                        var debugVariable = LLVM.DIBuilderCreateAutoVariable(_debugBuilder, debugBlock, name.Value, (UIntPtr)name.Length, file, instruction.Source.Line, _debugTypes[instruction.Value1.Type.TypeIndex], 0, LLVMDIFlags.LLVMDIFlagZero, 0);
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

    private static LLVMValueRef GetValue(InstructionValue value, LLVMValueRef[] values, LLVMValueRef[] allocations, LLVMValueRef functionPointer)
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
                return GetOrCreateFunctionDefinition(value.ConstantString);
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
                var fieldValues = new LLVMValueRef[value.Values.Length];
                for (var i = 0; i < fieldValues.Length; i++)
                {
                    fieldValues[i] = GetConstantValue(value.Values[i]);
                }
                return LLVMValueRef.CreateConstNamedStruct(_types[value.Type.TypeIndex], fieldValues);
            case InstructionValueType.ConstantArray when value.Values != null:
                var values = new LLVMValueRef[value.ArrayLength];
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
            case TypeKind.String:
                return GetString(value.ConstantString, value.UseRawString, false);
        }
        return null;
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
                var fields = new LLVMValueRef[structAst.Fields.Count];
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
                var values = new LLVMValueRef[arrayType.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = defaultValue;
                }
                return LLVMValueRef.CreateConstArray(_types[arrayType.ElementType.TypeIndex], values);
            }
            case TypeKind.Union:
            {
                var defaultValue = LLVMValueRef.CreateConstInt(LLVM.Int8Type(), 0, false);
                var values = new LLVMValueRef[type.Size];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = defaultValue;
                }
                var constArray = LLVMValueRef.CreateConstArray(LLVM.Int8Type(), values);
                return LLVMValueRef.CreateConstNamedStruct(_types[type.TypeIndex], new [] {constArray});
            }
        }
        return null;
    }

    private static LLVMValueRef GetString(string value, bool useRawString = false, bool constant = true)
    {
        var stringValue = _context.GetConstString(value, false);
        var stringGlobal = _module.AddGlobal(LLVM.TypeOf(stringValue), "str");
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

        var length = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)value.Length, false);
        return LLVMValueRef.CreateConstNamedStruct(_stringType, new [] {length, stringPointer});
    }

    private static void BuildStackSave()
    {
        const string stackSaveIntrinsic = "llvm.stacksave";
        _stackPointer = _builder.BuildAlloca(_u8PointerType);

        var function = _module.GetNamedFunction(stackSaveIntrinsic);
        if (function.Handle == IntPtr.Zero)
        {
            function = _module.AddFunction(stackSaveIntrinsic, LLVMTypeRef.CreateFunction(_u8PointerType, Array.Empty<LLVMTypeRef>()));
        }

        var stackPointer = _builder.BuildCall(function, Array.Empty<LLVMValueRef>(), "stackPointer");
        _builder.BuildStore(stackPointer, _stackPointer);
    }

    private static void BuildStackRestore()
    {
        const string stackRestoreIntrinsic = "llvm.stackrestore";

        var function = _module.GetNamedFunction(stackRestoreIntrinsic);
        if (function.Handle == IntPtr.Zero)
        {
            function = _module.AddFunction(stackRestoreIntrinsic, LLVMTypeRef.CreateFunction(LLVM.VoidType(), new [] {_u8PointerType}));
        }

        var stackPointer = _builder.BuildLoad(_stackPointer);
        _builder.BuildCall(function, new []{stackPointer});
    }

    private static void Compile(string objectFile, bool outputIntermediate)
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

        var targetMachine = target.CreateTargetMachine(defaultTriple, "generic", string.Empty, _codeGenLevel, LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);

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

    private static void CreateTemporaryDebugStructType(StructAst structAst)
    {
        using var structName = new MarshaledString(structAst.Name);

        var file = _debugFiles[structAst.FileIndex];
        _debugTypes[structAst.TypeIndex] = LLVM.DIBuilderCreateReplaceableCompositeType(_debugBuilder, (uint)DwarfTag.Structure_type, structName.Value, (UIntPtr)structName.Length, null, file, structAst.Line, 0, structAst.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, UIntPtr.Zero);
    }

    private static void CreateDebugStructType(StructAst structAst)
    {
        using var structName = new MarshaledString(structAst.Name);

        var file = _debugFiles[structAst.FileIndex];
        var fields = new LLVMMetadataRef[structAst.Fields.Count];

        if (fields.Length > 0)
        {
            var structDecl = _debugTypes[structAst.TypeIndex];
            for (var i = 0; i < fields.Length; i++)
            {
                var structField = structAst.Fields[i];
                using var fieldName = new MarshaledString(structField.Name);

                fields[i] = LLVM.DIBuilderCreateMemberType(_debugBuilder, structDecl, fieldName.Value, (UIntPtr)fieldName.Length, file, structField.Line, structField.Size * 8, 0, structField.Offset * 8, LLVMDIFlags.LLVMDIFlagZero, _debugTypes[structField.Type.TypeIndex]);
            }
        }

        fixed (LLVMMetadataRef* fieldsPointer = fields)
        {
            var debugStruct = LLVM.DIBuilderCreateStructType(_debugBuilder, null, structName.Value, (UIntPtr)structName.Length, file, structAst.Line, structAst.Size * 8, 0, LLVMDIFlags.LLVMDIFlagZero, null, (LLVMOpaqueMetadata**)fieldsPointer, (uint)fields.Length, 0, null, null, UIntPtr.Zero);
            if (fields.Length > 0)
            {
                LLVM.MetadataReplaceAllUsesWith(_debugTypes[structAst.TypeIndex], debugStruct);
            }
            _debugTypes[structAst.TypeIndex] = debugStruct;
        }
    }

    private static void CreateDebugEnumType(EnumAst enumAst)
    {
        using var enumName = new MarshaledString(enumAst.Name);

        var file = _debugFiles[enumAst.FileIndex];
        var enumValues = new LLVMMetadataRef[enumAst.Values.Count];
        var isUnsigned = enumAst.BaseType.Signed ? 0 : 1;

        for (var i = 0; i < enumValues.Length; i++)
        {
            var enumValue = enumAst.Values[i];
            using var valueName = new MarshaledString(enumValue.Name);

            enumValues[i] = LLVM.DIBuilderCreateEnumerator(_debugBuilder, valueName.Value, (UIntPtr)valueName.Length, enumValue.Value, isUnsigned);
        }

        fixed (LLVMMetadataRef* enumValuesPointer = enumValues)
        {
            _debugTypes[enumAst.TypeIndex] = LLVM.DIBuilderCreateEnumerationType(_debugBuilder, null, enumName.Value, (UIntPtr)enumName.Length, file, enumAst.Line, (uint)enumAst.Size * 8, 0, (LLVMOpaqueMetadata**)enumValuesPointer, (uint)enumValues.Length, _debugTypes[enumAst.BaseType.TypeIndex]);
        }
    }

    private static void CreateDebugBasicType(PrimitiveAst type, string typeName)
    {
        using var name = new MarshaledString(type.Name);
        switch (type.TypeKind)
        {
            case TypeKind.Void:
                _debugTypes[type.TypeIndex] = null;
                break;
            case TypeKind.Boolean:
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreateBasicType(_debugBuilder, name.Value, (UIntPtr)name.Length, 8, (uint)DwarfTypeEncoding.Boolean, LLVMDIFlags.LLVMDIFlagZero);
                break;
            case TypeKind.Integer:
            case TypeKind.Type:
                var encoding = type.Signed ? DwarfTypeEncoding.Signed : DwarfTypeEncoding.Unsigned;
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreateBasicType(_debugBuilder, name.Value, (UIntPtr)name.Length, type.Size * 8, (uint)encoding, LLVMDIFlags.LLVMDIFlagZero);
                break;
            case TypeKind.Float:
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreateBasicType(_debugBuilder, name.Value, (UIntPtr)name.Length, type.Size * 8, (uint)DwarfTypeEncoding.Float, LLVMDIFlags.LLVMDIFlagZero);
                break;
            case TypeKind.Pointer:
                var pointerType = _debugTypes[type.PointerType.TypeIndex];
                _debugTypes[type.TypeIndex] = LLVM.DIBuilderCreatePointerType(_debugBuilder, pointerType, 64, 0, 0, name.Value, (UIntPtr)name.Length);
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
