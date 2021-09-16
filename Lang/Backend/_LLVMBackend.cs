using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LLVMSharp.Interop;

namespace Lang.Backend
{
    public unsafe class _LLVMBackend : IBackend
    {
        private const string ObjectDirectory = "obj";

        private ProgramGraph _programGraph;
        private LLVMModuleRef _module;
        private LLVMContextRef _context;
        private LLVMBuilderRef _builder;
        private LLVMPassManagerRef _passManager;

        private LLVMValueRef _stackPointer;
        private LLVMTypeRef _stringType;
        private LLVMTypeRef _u8PointerType;
        private LLVMTypeRef[] _types;
        private Queue<(LLVMValueRef, FunctionIR)> _functionsToWrite = new();

        private bool _emitDebug;
        private LLVMDIBuilderRef _debugBuilder;
        private LLVMMetadataRef _debugCompilationUnit;
        private List<LLVMMetadataRef> _debugFiles;
        private Dictionary<string, LLVMMetadataRef> _debugTypes;
        private Dictionary<string, LLVMMetadataRef> _debugFunctions;

        private readonly LLVMValueRef _zeroInt = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), 0, false);

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

            // 4. Write the program beginning at the entrypoint
            WriteFunctionDefinition("main", Program.EntryPoint);
            while (_functionsToWrite.Any())
            {
                var (functionPointer, function) = _functionsToWrite.Dequeue();
                WriteFunction(functionPointer, function);
            }

            // 5. Compile to object file
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
            // 1. Declare types
            _types = new LLVMTypeRef[TypeTable.Count];
            var structs = new Dictionary<string, LLVMTypeRef>();
            // if (_emitDebug)
            // {
            //     foreach (var (name, type) in TypeTable.Types)
            //     {
            //         switch (type)
            //         {
            //             case StructAst structAst:
            //                 structs[name] = _context.CreateNamedStruct(name);

            //                 if (structAst.Fields.Any())
            //                 {
            //                     using var structName = new MarshaledString(structAst.Name);

            //                     var file = _debugFiles[structAst.FileIndex];
            //                     _debugTypes[name] = LLVM.DIBuilderCreateForwardDecl(_debugBuilder, (uint)DwarfTag.Structure_type, structName.Value, (UIntPtr)structName.Length, null, file, structAst.Line, 0, structAst.Size * 8, 0, null, (UIntPtr)0);
            //                 }
            //                 else
            //                 {
            //                     CreateDebugStructType(structAst, name);
            //                 }
            //                 break;
            //             case EnumAst enumAst:
            //                 CreateDebugEnumType(enumAst);
            //                 break;
            //             case PrimitiveAst primitive:
            //                 CreateDebugBasicType(primitive, name);
            //                 break;
            //             case ArrayType arrayType:
            //                 using (var typeName = new MarshaledString(type.Name))
            //                 {
            //                     var pointerType = _debugTypes[arrayType.ElementTypeDefinition.GenericName];
            //                     _debugTypes[name] = LLVM.DIBuilderCreatePointerType(_debugBuilder, pointerType, 64, 0, 0, typeName.Value, (UIntPtr)typeName.Length);
            //                 }
            //                 break;
            //         }
            //     }
            //     foreach (var (name, type) in TypeTable.Types)
            //     {
            //         if (type is StructAst structAst && structAst.Fields.Any())
            //         {
            //             var fields = structAst.Fields.Select(field => ConvertTypeDefinition(field.TypeDefinition)).ToArray();
            //             structs[name].StructSetBody(fields, false);

            //             CreateDebugStructType(structAst, name);
            //         }
            //     }
            // }
            // else
            {
                foreach (var (name, type) in TypeTable.Types)
                {
                    switch (type)
                    {
                        case StructAst structAst:
                            structs[name] = _types[structAst.TypeIndex] = _context.CreateNamedStruct(name);
                            break;
                        case EnumAst enumAst:
                            _types[enumAst.TypeIndex] = GetIntegerType(enumAst.BaseType.Size);
                            break;
                        case PrimitiveAst primitive:
                            _types[primitive.TypeIndex] = primitive.TypeKind switch
                            {
                                TypeKind.Void => LLVM.VoidType(),
                                TypeKind.Boolean => LLVM.Int1Type(),
                                TypeKind.Integer => GetIntegerType(primitive.Size),
                                TypeKind.Float => primitive.Size == 4 ? LLVM.FloatType() : LLVM.DoubleType(),
                                TypeKind.Pointer => GetPointerType(primitive.PointerType),
                                _ => null
                            };
                            break;
                        case ArrayType arrayType:
                            // TODO Should this be stored?
                            break;
                    }
                }
                foreach (var (name, type) in TypeTable.Types)
                {
                    if (type is StructAst structAst && structAst.Fields.Any())
                    {
                        var fields = structAst.Fields.Select(field => _types[field.Type.TypeIndex]).ToArray();
                        structs[name].StructSetBody(fields, false);
                    }
                }
            }
            _stringType = structs["string"];
            _u8PointerType = LLVM.PointerType(LLVM.Int8Type(), 0);

            // 2. Declare variables
            var globals = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>();
            // foreach (var globalVariable in _programGraph.Variables)
            // {
            //     if (globalVariable.Constant && globalVariable.TypeDefinition.TypeKind != TypeKind.String)
            //     {
            //         var (_, constant) = WriteExpression(globalVariable.Value, null);
            //         globals.Add(globalVariable.Name, (globalVariable.TypeDefinition, constant));
            //     }
            //     else
            //     {
            //         var typeDef = globalVariable.TypeDefinition;
            //         var type = ConvertTypeDefinition(typeDef);
            //         var global = _module.AddGlobal(type, globalVariable.Name);
            //         LLVM.SetLinkage(global, LLVMLinkage.LLVMPrivateLinkage);
            //         if (globalVariable.Value != null)
            //         {
            //             LLVM.SetInitializer(global, WriteExpression(globalVariable.Value, null).value);
            //         }
            //         else if (typeDef.TypeKind == TypeKind.Integer || typeDef.TypeKind == TypeKind.Float)
            //         {
            //             LLVM.SetInitializer(global, GetConstZero(type));
            //         }
            //         else if (typeDef.TypeKind == TypeKind.Pointer)
            //         {
            //             LLVM.SetInitializer(global, LLVM.ConstNull(type));
            //         }

            //         if (_emitDebug)
            //         {
            //             using var name = new MarshaledString(globalVariable.Name);

            //             var file = _debugFiles[globalVariable.FileIndex];
            //             var debugType = GetDebugType(globalVariable.TypeDefinition);
            //             var globalDebug = LLVM.DIBuilderCreateGlobalVariableExpression(_debugBuilder, _debugCompilationUnit, name.Value, (UIntPtr)name.Length, null, (UIntPtr)0, file, globalVariable.Line, debugType, 0, null, null, 0);
            //             LLVM.GlobalSetMetadata(global, 0, globalDebug);
            //         }

            //         globals.Add(globalVariable.Name, (globalVariable.TypeDefinition, global));
            //     }
            // }

            // 3. Write type table
            var typeTable = globals["__type_table"].value;
            SetPrivateConstant(typeTable);
            var typeInfoType = structs["TypeInfo"];

            var types = new LLVMValueRef[TypeTable.Count];
            var typePointers = new Dictionary<string, (IType type, LLVMValueRef typeInfo)>();
            foreach (var (name, type) in TypeTable.Types)
            {
                var typeInfo = _module.AddGlobal(typeInfoType, "____type_info");
                SetPrivateConstant(typeInfo);
                types[type.TypeIndex] = typeInfo;
                typePointers[name] = (type, typeInfo);
            }
            foreach (var (name, functions) in TypeTable.Functions)
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

            var typeFieldType = structs["TypeField"];
            var typeFieldArrayType = structs["Array.TypeField"];
            var enumValueType = structs["EnumValue"];
            var enumValueArrayType = structs["Array.EnumValue"];
            var argumentType = structs["ArgumentType"];
            var argumentArrayType = structs["Array.ArgumentType"];
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

                        var typeField = LLVMValueRef.CreateConstNamedStruct(typeFieldType, new LLVMValueRef[] {fieldNameString, fieldOffset, typePointers[field.TypeDefinition.GenericName].typeInfo});

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
                    returnType = typePointers[function.ReturnTypeDefinition.GenericName].typeInfo;

                    var argumentCount = function.Varargs ? function.Arguments.Count - 1 : function.Arguments.Count;
                    var argumentValues = new LLVMValueRef[argumentCount];
                    for (var i = 0; i < argumentCount; i++)
                    {
                        var argument = function.Arguments[i];

                        var argNameString = BuildString(argument.Name);
                        var argumentTypeInfo = argument.TypeDefinition.Name switch
                        {
                            "Type" => typePointers["s32"].typeInfo,
                                "Params" => typePointers[$"Array.{argument.TypeDefinition.Generics[0].GenericName}"].typeInfo,
                                _ => typePointers[argument.TypeDefinition.GenericName].typeInfo
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
                    returnType = LLVM.ConstNull(LLVM.PointerType(typeInfoType, 0));
                    arguments = LLVMValueRef.CreateConstNamedStruct(argumentArrayType, new LLVMValueRef[]{_zeroInt, LLVM.ConstNull(LLVM.PointerType(argumentType, 0))});
                }

                LLVMValueRef pointerType;
                if (type is PrimitiveAst primitive && primitive.TypeKind == TypeKind.Pointer)
                {
                    pointerType = typePointers[primitive.PointerTypeDefinition.GenericName].typeInfo;
                }
                else
                {
                    pointerType = LLVM.ConstNull(LLVM.PointerType(typeInfoType, 0));
                }

                LLVMValueRef elementType;
                if (type is ArrayType arrayType)
                {
                    elementType = typePointers[arrayType.ElementTypeDefinition.GenericName].typeInfo;
                }
                else
                {
                    elementType = LLVM.ConstNull(LLVM.PointerType(typeInfoType, 0));
                }

                LLVM.SetInitializer(typeInfo, LLVMValueRef.CreateConstNamedStruct(typeInfoType, new LLVMValueRef[] {typeNameString, typeKind, typeSize, fields, enumValues, returnType, arguments, pointerType, elementType}));
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

        private LLVMTypeRef GetIntegerType(uint size)
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

        private LLVMTypeRef GetPointerType(IType pointerType)
        {
            if (pointerType.TypeKind == TypeKind.Void)
            {
                return LLVM.PointerType(LLVM.Int8Type(), 0);
            }

            return LLVM.PointerType(_types[pointerType.TypeIndex], 0);
        }

        private void SetPrivateConstant(LLVMValueRef variable)
        {
            LLVM.SetLinkage(variable, LLVMLinkage.LLVMPrivateLinkage);
            LLVM.SetGlobalConstant(variable, 1);
            LLVM.SetUnnamedAddr(variable, 1);
        }

        private LLVMValueRef WriteFunctionDefinition(string name, FunctionIR function)
        {
            var argumentTypes = new LLVMTypeRef[function.Arguments.Length];
            for (var i = 0; i < function.Arguments.Length; i++)
            {
                argumentTypes[i] = _types[function.Arguments[i].TypeIndex];
            }
            var functionPointer = _module.AddFunction(name, LLVMTypeRef.CreateFunction(_types[function.ReturnType.TypeIndex], argumentTypes, function.Varargs));

            if (function.Instructions != null)
            {
                _functionsToWrite.Enqueue((functionPointer, function));
            }

            return functionPointer;
        }

        private LLVMValueRef GetOrCreateFunctionDefinition(string name)
        {
            var functionPointer = _module.GetNamedFunction(name);
            if (functionPointer.Handle == IntPtr.Zero)
            {
                functionPointer = WriteFunctionDefinition(name, Program.Functions[name]);
            }

            return functionPointer;
        }

        private void WriteFunction(LLVMValueRef functionPointer, FunctionIR function)
        {
            // Declare the basic blocks
            var basicBlocks = new LLVMBasicBlockRef[function.BasicBlocks.Count];
            foreach (var block in function.BasicBlocks)
            {
                basicBlocks[block.Index] = functionPointer.AppendBasicBlock(block.Index.ToString());
            }
            LLVM.PositionBuilderAtEnd(_builder, basicBlocks[0]);

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
                            _builder.BuildBr(basicBlocks[instruction.Index.Value]);
                            breakToNextBlock = false;
                            break;
                        }
                        case InstructionType.ConditionalJump:
                        {
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
                        case InstructionType.Call:
                        {
                            var callFunction = GetOrCreateFunctionDefinition(instruction.CallFunction);
                            var arguments = new LLVMValueRef[instruction.Value1.Values.Length];
                            for (var i = 0; i < instruction.Value1.Values.Length; i++)
                            {
                                arguments[i] = GetValue(instruction.Value1.Values[i], values, allocations, functionPointer);
                            }
                            values[instruction.ValueIndex] = _builder.BuildCall(callFunction, arguments);
                            break;
                        }
                        case InstructionType.IntegerExtend:
                        {
                            var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                            var targetType = _types[instruction.Value2.Type.TypeIndex];
                            values[instruction.ValueIndex] = _builder.BuildSExtOrBitCast(value, targetType);
                            break;
                        }
                        case InstructionType.UnsignedIntegerExtend:
                        {
                            var value = GetValue(instruction.Value1, values, allocations, functionPointer);
                            var targetType = _types[instruction.Value2.Type.TypeIndex];
                            values[instruction.ValueIndex] = _builder.BuildZExtOrBitCast(value, targetType);
                            break;
                        }
                        case InstructionType.IntegerTruncate:
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
                            _programGraph.Dependencies.Add("m");
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
                    }
                }
                blockIndex++;

                if (breakToNextBlock)
                {
                    _builder.BuildBr(basicBlocks[blockIndex]);
                }
            }
        }

        private LLVMValueRef GetValue(InstructionValue value, LLVMValueRef[] values, LLVMValueRef[] allocations, LLVMValueRef functionPointer)
        {
            switch (value.ValueType)
            {
                case InstructionValueType.Value:
                    return values[value.ValueIndex];
                case InstructionValueType.Allocation:
                    return allocations[value.ValueIndex];
                case InstructionValueType.Argument:
                    return functionPointer.GetParam((uint)value.ValueIndex);
                case InstructionValueType.Constant:
                    return GetConstant(value);
                case InstructionValueType.Null:
                    return LLVM.ConstNull(_u8PointerType);
                case InstructionValueType.ConstantStruct:
                case InstructionValueType.ConstantArray:
                    // TODO Implement me
                    break;
            }
            return null;
        }

        private LLVMValueRef GetConstant(InstructionValue value, bool constant = false)
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
                    var stringValue = _context.GetConstString(value.ConstantString, false);
                    var stringGlobal = _module.AddGlobal(LLVM.TypeOf(stringValue), "str");
                    if (constant)
                    {
                        SetPrivateConstant(stringGlobal);
                    }
                    LLVM.SetInitializer(stringGlobal, stringValue);
                    var stringPointer = LLVMValueRef.CreateConstBitCast(stringGlobal, _u8PointerType);

                    if (value.UseRawString)
                    {
                        return stringPointer;
                    }

                    var length = LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)value.ConstantString.Length, false);
                    return LLVMValueRef.CreateConstNamedStruct(_stringType, new []{length, stringPointer});
            }
            return null;
        }

        private void BuildStackSave()
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

        private void BuildStackRestore()
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

        // TODO Remove
        private string GetFunctionName(string name, int functionIndex, int functionCount)
        {
            return functionCount == 1 ? name : $"{name}.{functionIndex}";
        }

        // TODO Remove
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

        // TODO Rework the debugging info
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

                    fields[i] = LLVM.DIBuilderCreateMemberType(_debugBuilder, structDecl, fieldName.Value, (UIntPtr)fieldName.Length, file, structField.Line, structField.Size * 8, 0, structField.Offset * 8, LLVMDIFlags.LLVMDIFlagZero, GetDebugType(structField.TypeDefinition));
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
            var isUnsigned = enumAst.BaseTypeDefinition.PrimitiveType.Signed ? 0 : 1;

            for (var i = 0; i < enumValues.Length; i++)
            {
                var enumValue = enumAst.Values[i];
                using var valueName = new MarshaledString(enumValue.Name);

                enumValues[i] = LLVM.DIBuilderCreateEnumerator(_debugBuilder, valueName.Value, (UIntPtr)valueName.Length, enumValue.Value, isUnsigned);
            }

            fixed (LLVMMetadataRef* enumValuesPointer = enumValues)
            {
                _debugTypes[enumAst.Name] = LLVM.DIBuilderCreateEnumerationType(_debugBuilder, null, enumName.Value, (UIntPtr)enumName.Length, file, enumAst.Line, (uint)enumAst.BaseTypeDefinition.PrimitiveType.Bytes * 8, 0, (LLVMOpaqueMetadata**)enumValuesPointer, (uint)enumValues.Length, GetDebugType(enumAst.BaseTypeDefinition));
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
                    var pointerType = _debugTypes[type.PointerTypeDefinition.GenericName];
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
