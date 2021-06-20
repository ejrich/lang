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

        private ProgramGraph _programGraph;
        private LLVMModuleRef _module;
        private LLVMBuilderRef _builder;
        private LLVMPassManagerRef _passManager;
        private IFunction _currentFunction;
        private LLVMValueRef _stackPointer;
        private bool _stackPointerExists;
        private bool _stackSaved;

        private readonly Queue<LLVMValueRef> _allocationQueue = new();
        private readonly LLVMValueRef _zeroInt = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), 0, false);
        private readonly TypeDefinition _intTypeDefinition = new() {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};

        public string WriteFile(string projectPath, ProgramGraph programGraph, BuildSettings buildSettings)
        {
            _programGraph = programGraph;
            // 1. Initialize the LLVM module and builder
            InitLLVM(programGraph.Name, buildSettings.Release);

            // 2. Verify obj directory exists
            var objectPath = Path.Combine(projectPath, ObjectDirectory);
            if (!Directory.Exists(objectPath))
                Directory.CreateDirectory(objectPath);

            var objectFile = Path.Combine(objectPath, $"{programGraph.Name}.o");

            // 3. Write Data section
            var globals = WriteData(programGraph);

            // 4. Write Function and Operator overload definitions
            foreach (var (name, functions) in programGraph.Functions)
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    if (function.Compiler || function.CallsCompiler) continue;

                    if (name == "main")
                    {
                        function.Name = "__main";
                        WriteFunctionDefinition(function.Name, function.Arguments, function.ReturnType);
                    }
                    else if (name == "__start")
                    {
                        function.Name = "main";
                        WriteFunctionDefinition(function.Name, function.Arguments, function.ReturnType);
                    }
                    else
                    {
                        var functionName = GetFunctionName(name, i, functions.Count);
                        WriteFunctionDefinition(functionName, function.Arguments, function.ReturnType, function.Varargs);
                    }
                }
            }
            foreach (var (type, overloads) in programGraph.OperatorOverloads)
            {
                foreach (var (op, overload) in overloads)
                {
                    var overloadName = GetOperatorOverloadName(type, op);
                    WriteFunctionDefinition(overloadName, overload.Arguments, overload.ReturnType);
                }
            }

            // 5. Write Function and Operator overload bodies
            foreach (var (name, functions) in programGraph.Functions)
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var functionAst = functions[i];
                    if (functionAst.Extern || functionAst.Compiler || functionAst.CallsCompiler) continue;

                    var functionName = name switch
                    {
                        "main" or "__start" => functionAst.Name,
                        _ => GetFunctionName(name, i, functions.Count)
                    };
                    var function = LLVMApi.GetNamedFunction(_module, functionName);
                    var argumentCount = functionAst.Varargs ? functionAst.Arguments.Count - 1 : functionAst.Arguments.Count;
                    WriteFunction(functionAst, argumentCount, globals, function);
                }
            }
            foreach (var (type, overloads) in programGraph.OperatorOverloads)
            {
                foreach (var (op, overload) in overloads)
                {
                    var overloadName = GetOperatorOverloadName(type, op);
                    var function = LLVMApi.GetNamedFunction(_module, overloadName);
                    WriteFunction(overload, 2, globals, function);
                }
            }

            // 6. Compile to object file
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

        private IDictionary<string, (TypeDefinition type, LLVMValueRef value)> WriteData(ProgramGraph programGraph)
        {
            // 1. Declare structs and enums
            var structs = new Dictionary<string, LLVMTypeRef>();
            foreach (var (name, type) in programGraph.Types)
            {
                if (type is StructAst structAst)
                {
                    structs[name] = LLVMApi.StructCreateNamed(LLVMApi.GetModuleContext(_module), name);
                }
            }
            foreach (var (name, type) in programGraph.Types)
            {
                if (type is StructAst structAst && structAst.Fields.Any())
                {
                    var fields = structAst.Fields.Select(field => ConvertTypeDefinition(field.Type)).ToArray();
                    LLVMApi.StructSetBody(structs[name], fields, false);
                }
            }

            // 2. Declare variables
            var globals = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>();
            foreach (var globalVariable in programGraph.Variables)
            {
                if (globalVariable.Constant)
                {
                    var (_, constant) = WriteExpression(globalVariable.Value, null);
                    globals.Add(globalVariable.Name, (globalVariable.Type, constant));
                }
                else
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
            }

            // 3. Write type table
            var typeTable = globals["__type_table"].value;
            SetPrivateConstant(typeTable);
            var typeInfoType = structs["TypeInfo"];

            var types = new LLVMValueRef[programGraph.TypeCount];
            var typePointers = new Dictionary<string, (IType type, LLVMValueRef typeInfo)>();
            foreach (var (name, type) in programGraph.Types)
            {
                var typeInfo = LLVMApi.AddGlobal(_module, typeInfoType, "____type_info");
                SetPrivateConstant(typeInfo);
                types[type.TypeIndex] = typeInfo;
                typePointers[name] = (type, typeInfo);
            }
            foreach (var (name, functions) in programGraph.Functions)
            {
                for (var i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    var typeInfo = LLVMApi.AddGlobal(_module, typeInfoType, "____type_info");
                    SetPrivateConstant(typeInfo);
                    types[function.TypeIndex] = typeInfo;
                    var functionName = GetFunctionName(name, i, functions.Count);
                    typePointers[functionName] = (function, typeInfo);
                }
            }

            var typeFieldType = LLVMApi.GetTypeByName(_module, "TypeField");
            var enumValueType = LLVMApi.GetTypeByName(_module, "EnumValue");
            var argumentType = LLVMApi.GetTypeByName(_module, "ArgumentType");
            foreach (var (_, (type, typeInfo)) in typePointers)
            {
                var typeName = LLVMApi.ConstString(type.Name, (uint)type.Name.Length, false);
                var typeNameString = LLVMApi.AddGlobal(_module, typeName.TypeOf(), "str");
                SetPrivateConstant(typeNameString);
                LLVMApi.SetInitializer(typeNameString, typeName);

                var typeKind = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (uint)type.TypeKind, false);
                var typeSize = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), type.Size, false);

                LLVMValueRef fields;
                if (type is StructAst structAst)
                {
                    var typeFields = new LLVMValueRef[structAst.Fields.Count];

                    for (var i = 0; i < structAst.Fields.Count; i++)
                    {
                        var field = structAst.Fields[i];

                        var fieldName = LLVMApi.ConstString(field.Name, (uint)field.Name.Length, false);
                        var fieldNameString = LLVMApi.AddGlobal(_module, typeName.TypeOf(), "str");
                        SetPrivateConstant(fieldNameString);
                        LLVMApi.SetInitializer(fieldNameString, fieldName);
                        var fieldOffset = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), field.Offset, false);

                        var typeField = LLVMApi.ConstStruct(new [] {fieldNameString, fieldOffset, typePointers[field.Type.GenericName].typeInfo}, false);

                        typeFields[i] = typeField;
                    }

                    var typeFieldArray = LLVMApi.ConstArray(typeInfoType, typeFields);
                    var typeFieldArrayGlobal = LLVMApi.AddGlobal(_module, typeFieldArray.TypeOf(), "____type_fields");
                    SetPrivateConstant(typeFieldArrayGlobal);
                    LLVMApi.SetInitializer(typeFieldArrayGlobal, typeFieldArray);

                    fields = LLVMApi.ConstStruct(new []
                    {
                        LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)structAst.Fields.Count, false),
                        typeFieldArrayGlobal
                    }, false);
                }
                else
                {
                    fields = LLVMApi.ConstStruct(new []{_zeroInt, LLVMApi.ConstNull(LLVMTypeRef.PointerType(typeFieldType, 0))}, false);
                }

                LLVMValueRef enumValues;
                if (type is EnumAst enumAst)
                {
                    var enumValueRefs = new LLVMValueRef[enumAst.Values.Count];

                    for (var i = 0; i < enumAst.Values.Count; i++)
                    {
                        var value = enumAst.Values[i];

                        var enumValueName = LLVMApi.ConstString(value.Name, (uint)value.Name.Length, false);
                        var enumValueNameString = LLVMApi.AddGlobal(_module, typeName.TypeOf(), "str");
                        SetPrivateConstant(enumValueNameString);
                        LLVMApi.SetInitializer(enumValueNameString, enumValueName);

                        var enumValue = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (uint)value.Value, false);

                        enumValueRefs[i] = LLVMApi.ConstStruct(new [] {enumValueNameString, enumValue}, false);
                    }

                    var enumValuesArray = LLVMApi.ConstArray(typeInfoType, enumValueRefs);
                    var enumValuesArrayGlobal = LLVMApi.AddGlobal(_module, enumValuesArray.TypeOf(), "____enum_values");
                    SetPrivateConstant(enumValuesArrayGlobal);
                    LLVMApi.SetInitializer(enumValuesArrayGlobal, enumValuesArray);

                    enumValues = LLVMApi.ConstStruct(new []
                    {
                        LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)enumAst.Values.Count, false),
                        enumValuesArrayGlobal
                    }, false);
                }
                else
                {
                    enumValues = LLVMApi.ConstStruct(new [] {_zeroInt, LLVMApi.ConstNull(LLVMTypeRef.PointerType(enumValueType, 0))}, false);
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

                        var argName = LLVMApi.ConstString(argument.Name, (uint)argument.Name.Length, false);
                        var argNameString = LLVMApi.AddGlobal(_module, typeName.TypeOf(), "str");
                        SetPrivateConstant(argNameString);
                        LLVMApi.SetInitializer(argNameString, argName);

                        var argumentTypeInfo = argument.Type.Name switch
                        {
                            "Type" => typePointers["s32"].typeInfo,
                            "Params" => typePointers[$"List.{argument.Type.Generics[0].GenericName}"].typeInfo,
                            _ => typePointers[argument.Type.GenericName].typeInfo
                        };

                        var argumentValue = LLVMApi.ConstStruct(new [] {argNameString, argumentTypeInfo}, false);

                        argumentValues[i] = argumentValue;
                    }

                    var argumentArray = LLVMApi.ConstArray(typeInfoType, argumentValues);
                    var argumentArrayGlobal = LLVMApi.AddGlobal(_module, argumentArray.TypeOf(), "____type_fields");
                    SetPrivateConstant(argumentArrayGlobal);
                    LLVMApi.SetInitializer(argumentArrayGlobal, argumentArray);

                    arguments = LLVMApi.ConstStruct(new []
                    {
                        LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)function.Arguments.Count, false),
                        argumentArrayGlobal
                    }, false);
                }
                else
                {
                    returnType = LLVMApi.ConstNull(LLVMTypeRef.PointerType(typeFieldType, 0));
                    arguments = LLVMApi.ConstStruct(new []{_zeroInt, LLVMApi.ConstNull(LLVMTypeRef.PointerType(argumentType, 0))}, false);
                }

                LLVMApi.SetInitializer(typeInfo, LLVMApi.ConstStruct(new [] {typeNameString, typeKind, typeSize, fields, enumValues, returnType, arguments}, false));
            }

            var typeArray = LLVMApi.ConstArray(LLVMApi.PointerType(typeInfoType, 0), types);
            var typeArrayGlobal = LLVMApi.AddGlobal(_module, typeArray.TypeOf(), "____type_array");
            SetPrivateConstant(typeArrayGlobal);
            LLVMApi.SetInitializer(typeArrayGlobal, typeArray);

            var typeCount = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)types.Length, false);
            LLVMApi.SetInitializer(typeTable, LLVMApi.ConstStruct(new [] {typeCount, typeArrayGlobal}, false));

            return globals;
        }

        private void SetPrivateConstant(LLVMValueRef variable)
        {
            LLVMApi.SetLinkage(variable, LLVMLinkage.LLVMPrivateLinkage);
            LLVMApi.SetGlobalConstant(variable, true);
            LLVMApi.SetUnnamedAddr(variable, true);
        }

        private LLVMValueRef WriteFunctionDefinition(string name, List<DeclarationAst> arguments, TypeDefinition returnType, bool varargs = false)
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

        private void WriteFunction(IFunction functionAst, int argumentCount, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> globals, LLVMValueRef function)
        {
            _currentFunction = functionAst;
            // 1. Get function definition
            LLVMApi.PositionBuilderAtEnd(_builder, function.AppendBasicBlock("entry"));
            var localVariables = new Dictionary<string, (TypeDefinition type, LLVMValueRef value)>(globals);

            // 2. Allocate arguments on the stack
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
                case ReturnAst returnAst:
                    BuildAllocations(returnAst.Value);
                    return true;
                case CallAst call:
                    BuildCallAllocations(call);
                    break;
                case DeclarationAst declaration:
                    if (declaration.Constant) break;

                    var type = ConvertTypeDefinition(declaration.Type);
                    var variable = LLVMApi.BuildAlloca(_builder, type, declaration.Name);
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
                            var arrayType = LLVMTypeRef.ArrayType(targetType, declaration.Type.ConstCount.Value);
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
                                    var iterationValue = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(function.ReturnType), function.Name);
                                    _allocationQueue.Enqueue(iterationValue);
                                }
                                break;
                            case IndexAst index:
                                BuildAllocations(index.Index);
                                if (index.CallsOverload && !structField.Pointers[i])
                                {
                                    var overload = _programGraph.OperatorOverloads[index.OverloadType.GenericName][Operator.Subscript];
                                    var iterationValue = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(overload.ReturnType), overload.ReturnType.GenericName);
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
                    var indexVariable = LLVMApi.BuildAlloca(_builder, LLVMTypeRef.Int32Type(), each.IterationVariable);
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
                            var iterationValue = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(function.ReturnType), function.Name);
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
                                        var iterationValue = LLVMApi.BuildAlloca(_builder, ConvertTypeDefinition(function.ReturnType), function.Name);
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
        }

        private void BuildStructAllocations(string name)
        {
            var structDef = _programGraph.Types[name] as StructAst;
            foreach (var field in structDef!.Fields)
            {
                if (field.Type.Name == "List")
                {
                    if (field.Type.CArray) continue;
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

            if (declaration.Constant)
            {
                var (_, constant) = WriteExpression(declaration.Value, localVariables);

                localVariables.Add(declaration.Name, (declaration.Type, constant));
                return;
            }

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
            IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, List<AssignmentAst> values = null)
        {
            var assignments = values?.ToDictionary(_ => (_.Reference as IdentifierAst)!.Name);
            var structDef = _programGraph.Types[typeDef.GenericName] as StructAst;
            for (var i = 0; i < structDef!.Fields.Count; i++)
            {
                var structField = structDef.Fields[i];
                var type = ConvertTypeDefinition(structField.Type);
                if (type.TypeKind == LLVMTypeKind.LLVMArrayTypeKind)
                    continue;

                var field = LLVMApi.BuildStructGEP(_builder, variable, (uint) i, structField.Name);

                if (assignments != null && assignments.TryGetValue(structField.Name, out var assignment))
                {
                    var expression = WriteExpression(assignment.Value, localVariables);
                    var value = CastValue(expression, structField.Type);

                    LLVMApi.BuildStore(_builder, value, field);
                }
                else if (structField.Type.Name == "List")
                {
                    if (structField.Type.CArray) continue;
                    InitializeConstList(field, structField.Type.ConstCount.Value, structField.Type.Generics[0]);
                }
                else switch (type.TypeKind)
                {
                    case LLVMTypeKind.LLVMPointerTypeKind:
                        LLVMApi.BuildStore(_builder, LLVMApi.ConstNull(type), field);
                        break;
                    case LLVMTypeKind.LLVMStructTypeKind:
                        InitializeStruct(structField.Type, field, localVariables);
                        break;
                    default:
                        switch (structField.DefaultValue)
                        {
                            case ConstantAst constant:
                                var constantValue = BuildConstant(type, constant);
                                LLVMApi.BuildStore(_builder, constantValue, field);
                                break;
                            case StructFieldRefAst structFieldRef:
                                var enumName = structFieldRef.TypeNames[0];
                                var enumDef = (EnumAst)_programGraph.Types[enumName];
                                var value = enumDef.Values[structFieldRef.ValueIndices[0]].Value;
                                var enumValue = LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (ulong)value, false);
                                LLVMApi.BuildStore(_builder, enumValue, field);
                                break;
                            case null:
                                LLVMApi.BuildStore(_builder, GetConstZero(type), field);
                                break;
                        }
                        break;
                }
            }
        }

        private void InitializeConstList(LLVMValueRef list, uint length, TypeDefinition listType)
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
            if (loaded && type.Type == Lang.Translation.Type.Pointer)
            {
                type = type.Generics[0];
            }

            // 2. Evaluate the expression value
            var expression = WriteExpression(assignment.Value, localVariables);
            if (assignment.Operator != Operator.None && !constant)
            {
                // 2a. Build expression with variable value as the LHS
                var value = LLVMApi.BuildLoad(_builder, variable, "tmpvalue");
                expression.value = BuildExpression((type, value), expression, assignment.Operator, type);
                expression.type = type; // The type should now be the type of the variable
            }

            // 3. Reallocate the value of the variable
            var assignmentValue = CastValue(expression, type);
            if (!constant) // Values are either readonly or constants, so don't store
            {
                LLVMApi.BuildStore(_builder, assignmentValue, variable);
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
                        // Load the List data and set the compareTarget to the list count
                        if (iterationType.CArray)
                        {
                            listData = iterationValue;
                            compareTarget = WriteExpression(iterationType.Count, localVariables).value;
                        }
                        else
                        {
                            var dataPointer = LLVMApi.BuildStructGEP(_builder, iterationValue, 1, "dataptr");
                            listData = LLVMApi.BuildLoad(_builder, dataPointer, "data");

                            var lengthPointer= LLVMApi.BuildStructGEP(_builder, iterationValue, 0, "lengthptr");
                            compareTarget = LLVMApi.BuildLoad(_builder, lengthPointer, "length");
                        }
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
                        var pointerIndices = iterationType.CArray ? new []{_zeroInt, indexValue} : new []{indexValue};
                        var iterationVariable = LLVMApi.BuildGEP(_builder, listData, pointerIndices, each.IterationVariable);
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

        private (TypeDefinition type, LLVMValueRef value) WriteExpression(IAst ast, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables)
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
                case IdentifierAst identifier:
                {
                    if (!localVariables.TryGetValue(identifier.Name, out var typeValue))
                    {
                        var typeDef = _programGraph.Types[identifier.Name];
                        return (_intTypeDefinition, LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (uint)typeDef.TypeIndex, false));
                    }
                    var (type, value) = typeValue;
                    if (!type.Constant)
                    {
                        value = LLVMApi.BuildLoad(_builder, value, identifier.Name);
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
                        return (enumDef.BaseType, LLVMApi.ConstInt(GetIntegerType(enumDef.BaseType.PrimitiveType), (ulong)value, false));
                    }
                    var (type, field) = BuildStructField(structField, localVariables, out var loaded, out var constant);
                    if (!loaded && !constant)
                    {
                        field = LLVMApi.BuildLoad(_builder, field, "field");
                    }
                    return (type, field);
                }
                case CallAst call:
                    var functions = _programGraph.Functions[call.Function];
                    LLVMValueRef function;
                    if (call.Function == "main")
                    {
                        function = LLVMApi.GetNamedFunction(_module, "__main");
                    }
                    else
                    {
                        var functionName = GetFunctionName(call.Function, call.FunctionIndex, functions.Count);
                        function = LLVMApi.GetNamedFunction(_module, functionName);
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
                    var constant = false;
                    var (variableType, pointer) = changeByOne.Value switch
                    {
                        IdentifierAst identifier => localVariables[identifier.Name],
                        StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out constant),
                        IndexAst index => GetListPointer(index, localVariables, out _),
                        // TODO Test unary deref
                        // @Cleanup This branch should never be hit
                        _ => (null, new LLVMValueRef())
                    };

                    var value = constant ? pointer : LLVMApi.BuildLoad(_builder, pointer, "tmpvalue");
                    if (variableType.Type == Lang.Translation.Type.Pointer)
                    {
                        variableType = variableType.Generics[0];
                    }
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

                    if (!constant) // Values are either readonly or constants, so don't store
                    {
                        LLVMApi.BuildStore(_builder, newValue, pointer);
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
                            // TODO Test unary deref?
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
                    var (elementType, elementValue) = GetListPointer(index, localVariables, out var loaded);
                    if (!loaded)
                    {
                        elementValue = LLVMApi.BuildLoad(_builder, elementValue, "tmpindex");
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
                    return (_intTypeDefinition, LLVMApi.ConstInt(LLVMTypeRef.Int32Type(), (uint)type.TypeIndex, false));
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
            return functionCount == 1 ? name : $"{name}_{functionIndex}";
        }

        private string GetOperatorOverloadName(TypeDefinition type, Operator op)
        {
            return GetOperatorOverloadName(type.GenericName, op);
        }

        private string GetOperatorOverloadName(string typeName, Operator op)
        {
            return $"operator.{op}.{typeName}";
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
                function = WriteFunctionDefinition("llvm.stacksave", new List<DeclarationAst>(), _stackPointerType);
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
                function = WriteFunctionDefinition("llvm.stackrestore", new List<DeclarationAst> { new()
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
                case IntegerType integerType:
                    if (integerType.Bytes == 8 && !integerType.Signed)
                    {
                        return LLVMApi.ConstInt(type, ulong.Parse(constant.Value), false);
                    }
                    return LLVMApi.ConstInt(type, (ulong)long.Parse(constant.Value), false);
                case FloatType:
                    return LLVMApi.ConstRealOfStringAndSize(type, constant.Value, (uint)constant.Value.Length);
            }

            switch (constant.Type.Name)
            {
                case "bool":
                    return LLVMApi.ConstInt(type, constant.Value == "true" ? (ulong)1 : 0, false);
                case "string":
                    return LLVMApi.BuildGlobalStringPtr(_builder, constant.Value, "str");
                default:
                    return _zeroInt;
            }
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
                        LLVMApi.BuildStore(_builder, indexValue, value);
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
                        LLVMApi.BuildStore(_builder, callValue, value);
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
                        value = LLVMApi.BuildLoad(_builder, value, "pointerval");
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
                                value = LLVMApi.BuildGEP(_builder, value, new []{_zeroInt, _zeroInt}, "dataPtr");
                            }
                            break;
                        case IndexAst index:
                            var (_, indexValue) = WriteExpression(index.Index, localVariables);
                            value = LLVMApi.BuildGEP(_builder, value, new []{_zeroInt, indexValue}, "indexptr");
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
                        value = LLVMApi.BuildStructGEP(_builder, value, (uint)structField.ValueIndices[i-1], identifier.Name);
                        break;
                    case IndexAst index:
                        var (_, indexValue) = WriteExpression(index.Index, localVariables);
                        value = LLVMApi.BuildStructGEP(_builder, value, (uint)structField.ValueIndices[i-1], index.Name);

                        if (index.CallsOverload)
                        {
                            var overloadName = GetOperatorOverloadName(type, Operator.Subscript);
                            var overload = LLVMApi.GetNamedFunction(_module, overloadName);
                            var overloadDef = _programGraph.OperatorOverloads[type.GenericName][Operator.Subscript];

                            skipPointer = true;
                            type = overloadDef.ReturnType;
                            value = LLVMApi.BuildLoad(_builder, value, "value");
                            var callValue = LLVMApi.BuildCall(_builder, overload, new []{value, indexValue}, string.Empty);
                            if (i < structField.Pointers.Length && !structField.Pointers[i])
                            {
                                value = _allocationQueue.Dequeue();
                                LLVMApi.BuildStore(_builder, callValue, value);
                            }
                            else
                            {
                                value = callValue;
                            }

                            if (i == structField.Pointers.Length)
                            {
                                loaded = true;
                            }
                        }
                        else
                        {
                            if (type.CArray)
                            {
                                value = LLVMApi.BuildGEP(_builder, value, new []{_zeroInt, indexValue}, "indexptr");
                            }
                            else
                            {
                                var listData = LLVMApi.BuildStructGEP(_builder, value, 1, "listdata");
                                var dataPointer = LLVMApi.BuildLoad(_builder, listData, "dataptr");
                                value = LLVMApi.BuildGEP(_builder, dataPointer, new [] {indexValue}, "indexptr");
                            }
                            type = type.Generics[0];
                        }
                        break;
                }
            }

            return (type, value);
        }

        private (TypeDefinition type, LLVMValueRef value) GetListPointer(IndexAst index, IDictionary<string, (TypeDefinition type, LLVMValueRef value)> localVariables, out bool loaded)
        {
            // 1. Get the variable pointer
            var (type, variable) = localVariables[index.Name];

            // 2. Determine the index
            var (_, indexValue) = WriteExpression(index.Index, localVariables);

            // 3. Call the overload if needed
            if (index.CallsOverload)
            {
                var overloadName = GetOperatorOverloadName(type, Operator.Subscript);
                var overload = LLVMApi.GetNamedFunction(_module, overloadName);
                var overloadDef = _programGraph.OperatorOverloads[type.GenericName][Operator.Subscript];

                loaded = true;
                return (overloadDef.ReturnType, LLVMApi.BuildCall(_builder, overload, new []{LLVMApi.BuildLoad(_builder, variable, index.Name), indexValue}, string.Empty));
            }

            // 4. Build the pointer with the first index of 0
            var elementType = type.Generics[0];
            LLVMValueRef listPointer;
            if (type.CArray)
            {
                listPointer = LLVMApi.BuildGEP(_builder, variable, new []{_zeroInt, indexValue}, "dataptr");
            }
            else
            {
                var listData = LLVMApi.BuildStructGEP(_builder, variable, 1, "listdata");
                var dataPointer = LLVMApi.BuildLoad(_builder, listData, "dataptr");
                listPointer = LLVMApi.BuildGEP(_builder, dataPointer, new [] {indexValue}, "indexptr");
            }
            loaded = false;
            return (elementType, listPointer);
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

            // 2. Handle compares and shifts, since the lhs and rhs should not be cast to the target type
            switch (op)
            {
                case Operator.And:
                    return lhs.type.Name == "bool" ? LLVMApi.BuildAnd(_builder, lhs.value, rhs.value, "tmpand")
                        : BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, Operator.And);
                case Operator.Or:
                    return lhs.type.Name == "bool" ? LLVMApi.BuildOr(_builder, lhs.value, rhs.value, "tmpor")
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
            return BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, op);
        }

        private LLVMValueRef BuildShift((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, bool right = false)
        {
            if (lhs.type.PrimitiveType is IntegerType)
            {
                var result = right ? LLVMApi.BuildAShr(_builder, lhs.value, rhs.value, "tmpshr")
                    : LLVMApi.BuildShl(_builder, lhs.value, rhs.value, "tmpshl");

                return result;
            }

            return BuildOperatorOverloadCall(lhs.type, lhs.value, rhs.value, right ? Operator.ShiftRight : Operator.ShiftLeft);
        }

        private LLVMValueRef BuildRotate((TypeDefinition type, LLVMValueRef value) lhs, (TypeDefinition type, LLVMValueRef value) rhs, bool right = false)
        {
            if (lhs.type.PrimitiveType is IntegerType)
            {
                var result = BuildShift(lhs, rhs, right);

                var maskSize = LLVMApi.ConstInt(ConvertTypeDefinition(lhs.type), (uint)(lhs.type.PrimitiveType?.Bytes * 8 ?? 32), false);
                var maskShift = LLVMApi.BuildSub(_builder, maskSize, rhs.value, "mask");

                var mask = right ? LLVMApi.BuildShl(_builder, lhs.value, maskShift, "tmpshl")
                    : LLVMApi.BuildAShr(_builder, lhs.value, maskShift, "tmpshr");

                return LLVMApi.IsUndef(result) ? mask : LLVMApi.BuildOr(_builder, result, mask, "tmpmask");
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
            var overload = LLVMApi.GetNamedFunction(_module, overloadName);

            return LLVMApi.BuildCall(_builder, overload, new []{lhs, rhs}, string.Empty);
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
            switch (op)
            {
                case Operator.Add: return LLVMApi.BuildFAdd(_builder, lhs, rhs, "tmpadd");
                case Operator.Subtract: return LLVMApi.BuildFSub(_builder, lhs, rhs, "tmpsub");
                case Operator.Multiply: return LLVMApi.BuildFMul(_builder, lhs, rhs, "tmpmul");
                case Operator.Divide: return LLVMApi.BuildFDiv(_builder, lhs, rhs, "tmpdiv");
                case Operator.Modulus:
                    _programGraph.Dependencies.Add("m");
                    return LLVMApi.BuildFRem(_builder, lhs, rhs, "tmpmod");
                // @Cleanup This branch should never be hit
                default: return new LLVMValueRef();
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
                IntegerType integerType => GetIntegerType(integerType),
                FloatType floatType => floatType.Bytes == 8 ? LLVMApi.DoubleType() : LLVMTypeRef.FloatType(),
                EnumType enumType => GetIntegerType(enumType),
                _ => type.Name switch
                {
                    "bool" => LLVMTypeRef.Int1Type(),
                    "void" => pointer ? LLVMTypeRef.Int8Type() : LLVMTypeRef.VoidType(),
                    "List" => GetListType(type),
                    "Params" => GetListType(type),
                    "string" => LLVMTypeRef.PointerType(LLVMTypeRef.Int8Type(), 0),
                    "Type" => LLVMTypeRef.Int32Type(),
                    _ => GetStructType(type)
                }
            };
        }

        private LLVMTypeRef GetIntegerType(IPrimitive primitive)
        {
            return primitive.Bytes switch
            {
                1 => LLVMTypeRef.Int8Type(),
                2 => LLVMTypeRef.Int16Type(),
                4 => LLVMTypeRef.Int32Type(),
                8 => LLVMTypeRef.Int64Type(),
                _ => LLVMTypeRef.Int32Type()
            };
        }

        private LLVMTypeRef GetListType(TypeDefinition type)
        {
            var listType = type.Generics[0];

            if (type.CArray)
            {
                var elementType = ConvertTypeDefinition(listType);
                return LLVMApi.ArrayType(elementType, type.ConstCount.Value);
            }

            return LLVMApi.GetTypeByName(_module, $"List.{listType.GenericName}");
        }

        private LLVMTypeRef GetStructType(TypeDefinition type)
        {
            if (_programGraph.Types.TryGetValue(type.Name, out var typeDef) && typeDef is EnumAst)
            {
                return LLVMTypeRef.Int32Type();
            }

            return LLVMApi.GetTypeByName(_module, type.GenericName);
        }
    }
}
