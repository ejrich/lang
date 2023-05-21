using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;

namespace ol;

public static class TypeChecker
{
    public static GlobalScope GlobalScope;
    public static List<PrivateScope> PrivateScopes;
    public static StructAst BaseArrayType;

    private static ConcurrentDictionary<string, Dictionary<Operator, OperatorOverloadAst>> _operatorOverloads;
    private static ConcurrentDictionary<string, Dictionary<Operator, OperatorOverloadAst>> _polymorphicOperatorOverloads;
    private static ConcurrentDictionary<string, Library> _libraries;

    private static Queue<IAst> _astCompleteQueue = new();

    private static int _generatedCodeFileIndex;
    private static uint _generatedCodeLineCount;
    private static StreamWriter _generatedCodeWriter;

    public static void Init()
    {
        GlobalScope = new();
        PrivateScopes = new();

        _operatorOverloads = new();
        _polymorphicOperatorOverloads = new();
        _libraries = new();

        // Add primitive types to global identifiers
        TypeTable.VoidType = AddPrimitive("void", TypeKind.Void, 1);
        TypeTable.BoolType = AddPrimitive("bool", TypeKind.Boolean, 1);
        TypeTable.S8Type = AddPrimitive("s8", TypeKind.Integer, 1, true);
        TypeTable.U8Type = AddPrimitive("u8", TypeKind.Integer, 1);
        TypeTable.S16Type = AddPrimitive("s16", TypeKind.Integer, 2, true);
        TypeTable.U16Type = AddPrimitive("u16", TypeKind.Integer, 2);
        TypeTable.S32Type = AddPrimitive("s32", TypeKind.Integer, 4, true);
        TypeTable.U32Type = AddPrimitive("u32", TypeKind.Integer, 4);
        TypeTable.S64Type = AddPrimitive("s64", TypeKind.Integer, 8, true);
        TypeTable.U64Type = AddPrimitive("u64", TypeKind.Integer, 8);
        TypeTable.FloatType = AddPrimitive("float", TypeKind.Float, 4, true);
        TypeTable.Float64Type = AddPrimitive("float64", TypeKind.Float, 8, true);
        TypeTable.TypeType = AddPrimitive("Type", TypeKind.Type, 4, true);
    }

    private static PrimitiveAst AddPrimitive(string name, TypeKind typeKind, uint size, bool signed = false)
    {
        var primitiveAst = new PrimitiveAst {Name = name, TypeKind = typeKind, Size = size, Alignment = size, Signed = signed};
        GlobalScope.Identifiers[name] = primitiveAst;
        GlobalScope.Types[name] = primitiveAst;
        TypeTable.Add(primitiveAst);
        TypeTable.CreateTypeInfo(primitiveAst);
        return primitiveAst;
    }

    public static void CheckTypes()
    {
        VerifyStruct(TypeTable.StringType);
        VerifyStruct(TypeTable.AnyType);

        TypeTable.RawStringType = GlobalScope.Types["u8*"];
        TypeTable.TypeInfoPointerType = GlobalScope.Types["TypeInfo*"];
        TypeTable.VoidPointerType = GlobalScope.Types["void*"];

        // 1. Verify and run top-level directives
        ClearDirectiveQueue();

        // 2. Execute any other compiler directives
        while (Parser.RunDirectives.TryDequeue(out var runDirective))
        {
            var scope = GetFileScope(runDirective);
            var scopeAst = (ScopeAst)runDirective.Value;
            scopeAst.Parent = scope;

            var runDirectiveFunction = new RunDirectiveFunction { ReturnType = TypeTable.VoidType };
            VerifyScope(scopeAst, runDirectiveFunction);

            ClearAstQueue();
            if (!ErrorReporter.Errors.Any())
            {
                var function = ProgramIRBuilder.CreateRunnableFunction(scopeAst, runDirectiveFunction);

                if (ThreadPool.ExecuteRunDirective(function, scopeAst))
                {
                    ProgramRunner.RunProgram(function, scopeAst);
                }
            }
        }

        // 3. Verify the rest of the types
        while (Parser.Asts.TryDequeue(out var ast))
        {
            VerifyAst(ast);
        }

        ClearAstQueue();

        if (GlobalScope.Functions.TryGetValue("main", out var functions))
        {
            if (functions.Count > 1)
            {
                ErrorReporter.Report("Only one main function can be defined", functions[1]);
            }
        }
        else
        {
            ErrorReporter.Report("'main' function of the program is not defined");
        }

        foreach (var (name, _) in BuildSettings.InputVariables.Where(v => !v.Value.Used))
        {
            ErrorReporter.Report($"Input variable '{name}' was not found as a global constant");
        }

        _generatedCodeWriter?.Close();
    }

    public static void ClearDirectiveQueue()
    {
        while (Parser.Directives.TryDequeue(out var directive))
        {
            var parsingAdditional = false;
            do
            {
                switch (directive.Type)
                {
                    case DirectiveType.If:
                        var conditional = directive.Value as ConditionalAst;
                        if (VerifyCondition(conditional.Condition, null, GetFileScope(conditional), true))
                        {
                            var condition = ProgramIRBuilder.CreateRunnableCondition(conditional.Condition);

                            if (ProgramRunner.ExecuteCondition(condition, conditional.Condition))
                            {
                                if (conditional.IfBlock.Children.Any())
                                {
                                    foreach (var ast in conditional.IfBlock.Children)
                                    {
                                        AddAdditionalAst(ast, ref parsingAdditional);
                                    }
                                }
                            }
                            else if (conditional.ElseBlock != null && conditional.ElseBlock.Children.Any())
                            {
                                foreach (var ast in conditional.ElseBlock.Children)
                                {
                                    AddAdditionalAst(ast, ref parsingAdditional);
                                }
                            }
                        }
                        break;
                    case DirectiveType.Assert:
                        if (VerifyCondition(directive.Value, null, GetFileScope(directive), true))
                        {
                            var condition = ProgramIRBuilder.CreateRunnableCondition(directive.Value);

                            if (!ProgramRunner.ExecuteCondition(condition, directive.Value))
                            {
                                var message = directive.AssertMessage == null ? "Assertion failed" : $"Assertion failed, {directive.AssertMessage}";
                                ErrorReporter.Report(message, directive.Value);
                            }
                        }
                        break;
                }
            } while (Parser.Directives.TryDequeue(out directive));

            if (parsingAdditional)
            {
                ThreadPool.CompleteWork(ThreadPool.ParseQueue);
            }
        }
    }

    private static void ClearAstQueue()
    {
        while (_astCompleteQueue.TryDequeue(out var ast))
        {
            VerifyAst(ast);
        }
    }

    private static void VerifyAst(IAst ast)
    {
        switch (ast)
        {
            case StructAst structAst:
                if (!structAst.Verified)
                {
                    VerifyStruct(structAst);
                }
                break;
            case DeclarationAst globalVariable:
                if (!globalVariable.Verified)
                {
                    VerifyGlobalVariable(globalVariable);
                }
                break;
            case UnionAst union:
                if (!union.Verified)
                {
                    VerifyUnion(union);
                }
                break;
            case InterfaceAst interfaceAst:
                if (!interfaceAst.Verified)
                {
                    VerifyInterface(interfaceAst);
                }
                break;
            case FunctionAst function:
                if (!function.Flags.HasFlag(FunctionFlags.Verified))
                {
                    VerifyFunction(function);
                }
                break;
            case OperatorOverloadAst overload:
                if (!overload.Flags.HasFlag(FunctionFlags.Verified))
                {
                    VerifyOperatorOverload(overload);
                }
                break;
        }
    }

    private static void AddAdditionalAst(IAst ast, ref bool parsingAdditional)
    {
        switch (ast)
        {
            case FunctionAst function:
                AddFunction(function);
                break;
            case OperatorOverloadAst overload:
                AddOverload(overload);
                break;
            case EnumAst enumAst:
                VerifyEnum(enumAst);
                return;
            case StructAst structAst:
                if (structAst.Generics != null)
                {
                    AddPolymorphicStruct(structAst);
                    return;
                }
                structAst.TypeKind = TypeKind.Struct;
                AddStruct(structAst);
                break;
            case UnionAst union:
                AddUnion(union);
                break;
            case InterfaceAst interfaceAst:
                AddInterface(interfaceAst);
                break;
            case DeclarationAst globalVariable:
                AddGlobalVariable(globalVariable);
                break;
            case CompilerDirectiveAst directive:
                switch (directive.Type)
                {
                    case DirectiveType.Run:
                        Parser.RunDirectives.Enqueue(directive);
                        break;
                    case DirectiveType.ImportModule:
                        Parser.AddModule(directive);
                        parsingAdditional = true;
                        break;
                    case DirectiveType.ImportFile:
                        Parser.AddFile(directive);
                        parsingAdditional = true;
                        break;
                    case DirectiveType.Library:
                        AddLibrary(directive);
                        break;
                    case DirectiveType.SystemLibrary:
                        AddSystemLibrary(directive);
                        break;
                    default:
                        Parser.Directives.Enqueue(directive);
                        break;
                }
                return;
        }
        Parser.Asts.Enqueue(ast);
    }

    private static IScope GetFileScope(IAst ast)
    {
        var privateScope = PrivateScopes[ast.FileIndex];
        return privateScope == null ? GlobalScope : privateScope;
    }

    private static void AddType(string name, IType type)
    {
        AddType(name, type, type.FileIndex);
    }

    private static void AddType(string name, IType type, int fileIndex)
    {
        if (type.Private)
        {
            var privateScope = PrivateScopes[fileIndex];

            if (privateScope.Types.ContainsKey(name) || GlobalScope.Types.ContainsKey(name))
            {
                return;
            }

            privateScope.Types[name] = type;
        }
        else
        {
            if (!GlobalScope.Types.TryAdd(name, type))
            {
                return;
            }
        }

        TypeTable.Add(type);
    }

    private static bool AddTypeAndIdentifier(string name, IType type, IAst ast)
    {
        if (type.Private)
        {
            var privateScope = PrivateScopes[type.FileIndex];

            if (privateScope.Types.ContainsKey(name) || GlobalScope.Types.ContainsKey(name))
            {
                ErrorReporter.Report($"Multiple definitions of type '{type.Name}'", ast);
                return false;
            }

            privateScope.Types[name] = type;
            privateScope.Identifiers[name] = ast;
        }
        else
        {
            if (!GlobalScope.Types.TryAdd(name, type))
            {
                ErrorReporter.Report($"Multiple definitions of type '{type.Name}'", ast);
                return false;
            }

            GlobalScope.Identifiers[name] = ast;
        }

        TypeTable.Add(type);
        return true;
    }

    private static bool GetType(string name, int fileIndex, out IType type)
    {
        var privateScope = PrivateScopes[fileIndex];

        if (privateScope == null)
        {
            return GlobalScope.Types.TryGetValue(name, out type);
        }

        return privateScope.Types.TryGetValue(name, out type) || GlobalScope.Types.TryGetValue(name, out type);
    }

    private static bool GetPolymorphicStruct(string name, int fileIndex, out StructAst type)
    {
        var privateScope = PrivateScopes[fileIndex];

        if (privateScope == null)
        {
            return GlobalScope.PolymorphicStructs.TryGetValue(name, out type);
        }

        return privateScope.PolymorphicStructs.TryGetValue(name, out type) || GlobalScope.PolymorphicStructs.TryGetValue(name, out type);
    }

    private static bool GetExistingFunction(string name, int fileIndex, out FunctionAst function, out int functionCount)
    {
        var privateScope = PrivateScopes[fileIndex];

        if (privateScope == null)
        {
            if (GlobalScope.Functions.TryGetValue(name, out var functions))
            {
                functionCount = functions.Count;
                function = functions[0];
                return true;
            }
        }
        else
        {
            if (privateScope.Functions.TryGetValue(name, out var functions))
            {
                functionCount = functions.Count;

                if (GlobalScope.Functions.TryGetValue(name, out var globalFunctions))
                {
                    functionCount += globalFunctions.Count;
                }

                function = functions[0];
                return true;
            }
            else if (GlobalScope.Functions.TryGetValue(name, out var globalFunctions))
            {
                functionCount = globalFunctions.Count;
                function = globalFunctions[0];
                return true;
            }
        }

        function = null;
        functionCount = 0;
        return false;
    }

    private static bool GetExistingPolymorphicFunction(string name, int fileIndex, out FunctionAst function)
    {
        function = null;
        var privateScope = PrivateScopes[fileIndex];

        if (privateScope == null)
        {
            if (GlobalScope.PolymorphicFunctions.TryGetValue(name, out var functions))
            {
                function = functions[0];
                return true;
            }
        }
        else
        {
            if (privateScope.PolymorphicFunctions.TryGetValue(name, out var functions))
            {
                function = functions[0];
                return true;
            }
            if (GlobalScope.PolymorphicFunctions.TryGetValue(name, out var globalFunctions))
            {
                function = globalFunctions[0];
                return true;
            }
        }

        return false;
    }

    public static void AddFunction(FunctionAst function)
    {
        if (function.Generics.Any())
        {
            // if (!function.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) && function.Arguments.All(arg => !arg.HasGenerics))
            // {
            //     ErrorReporter.Report($"Function '{function.Name}' has generic(s), but the generic(s) are not used in the argument(s) or the return type", function);
            // }

            if (OverloadExistsForPolymorphicFunction(function))
            {
                ErrorReporter.Report($"Function '{function.Name}' has multiple overloads with arguments ({string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.TypeDefinition)))})", function);
            }

            if (function.Private)
            {
                var privateScope = PrivateScopes[function.FileIndex];

                if (!privateScope.PolymorphicFunctions.TryGetValue(function.Name, out var functions))
                {
                    privateScope.PolymorphicFunctions[function.Name] = functions = new List<FunctionAst>();
                }
                functions.Add(function);
            }
            else
            {
                var functions = GlobalScope.PolymorphicFunctions.GetOrAdd(function.Name, _ => new List<FunctionAst>());
                functions.Add(function);
            }
        }
        else
        {
            if (function.Flags.HasFlag(FunctionFlags.Extern))
            {
                if (function.Private)
                {
                    ErrorReporter.Report($"Extern function '{function.Name}' must be public to avoid linking failures", function);
                }
                else if (GetExistingFunction(function.Name, function.FileIndex, out _, out _))
                {
                    ErrorReporter.Report($"Multiple definitions of extern function '{function.Name}'", function);
                }
            }
            else if (OverloadExistsForFunction(function))
            {
                ErrorReporter.Report($"Function '{function.Name}' has multiple overloads with arguments ({string.Join(", ", function.Arguments.Select(arg => PrintTypeDefinition(arg.TypeDefinition)))})", function);
            }

            AddFunction(function.Name, function.FileIndex, function);
        }
    }

    private static void AddFunction(string name, int fileIndex, FunctionAst function)
    {
        if (name == null) return;

        function.FunctionIndex = ProgramIRBuilder.AddFunction(function);

        if (function.Private)
        {
            var privateScope = PrivateScopes[fileIndex];

            if (!privateScope.Functions.TryGetValue(name, out var functions))
            {
                privateScope.Functions[name] = functions = new List<FunctionAst>();
            }
            functions.Add(function);
        }
        else
        {
            var functions = GlobalScope.Functions.GetOrAdd(function.Name, _ => new List<FunctionAst>());
            functions.Add(function);
        }

        Messages.Submit(MessageType.ReadyToBeTypeChecked, function);
    }

    public static void AddOverload(OperatorOverloadAst overload)
    {
        if (overload.Generics.Any())
        {
            if (!_polymorphicOperatorOverloads.TryGetValue(overload.Type.Name, out var overloads))
            {
                _polymorphicOperatorOverloads[overload.Type.Name] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
            }
            if (overloads.ContainsKey(overload.Operator))
            {
                ErrorReporter.Report($"Multiple definitions of overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", overload);
            }
            overloads[overload.Operator] = overload;
        }
        else
        {
            overload.Name = $"operator {PrintOperator(overload.Operator)} {overload.Type.Name}";
            overload.FunctionIndex = ProgramIRBuilder.AddFunction(overload);
            if (!_operatorOverloads.TryGetValue(overload.Type.Name, out var overloads))
            {
                _operatorOverloads[overload.Type.Name] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
            }
            if (overloads.ContainsKey(overload.Operator))
            {
                ErrorReporter.Report($"Multiple definitions of overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'", overload);
            }
            overloads[overload.Operator] = overload;

            Messages.Submit(MessageType.ReadyToBeTypeChecked, overload);
        }
    }

    public static bool AddPolymorphicStruct(StructAst structAst)
    {
        if (structAst.Private)
        {
            var privateScope = PrivateScopes[structAst.FileIndex];

            if (privateScope.PolymorphicStructs.ContainsKey(structAst.Name) || GlobalScope.PolymorphicStructs.ContainsKey(structAst.Name))
            {
                ErrorReporter.Report($"Multiple definitions of polymorphic struct '{structAst.Name}'", structAst);
                return false;
            }

            privateScope.PolymorphicStructs[structAst.Name] = structAst;
        }
        else
        {
            if (!GlobalScope.PolymorphicStructs.TryAdd(structAst.Name, structAst))
            {
                ErrorReporter.Report($"Multiple definitions of polymorphic struct '{structAst.Name}'", structAst);
                return false;
            }
        }

        return true;
    }

    public static bool AddStruct(StructAst structAst)
    {
        Messages.Submit(MessageType.ReadyToBeTypeChecked, structAst);
        return AddTypeAndIdentifier(structAst.Name, structAst, structAst);
    }

    public static bool AddGlobalVariable(DeclarationAst variable)
    {
        if (variable.Private)
        {
            var privateScope = PrivateScopes[variable.FileIndex];

            if (privateScope.Identifiers.ContainsKey(variable.Name) || GlobalScope.Identifiers.ContainsKey(variable.Name))
            {
                ErrorReporter.Report($"Identifier '{variable.Name}' already defined", variable);
                return false;
            }

            privateScope.Identifiers[variable.Name] = variable;
        }
        else
        {
            if (!GlobalScope.Identifiers.TryAdd(variable.Name, variable))
            {
                ErrorReporter.Report($"Identifier '{variable.Name}' already defined", variable);
                return false;
            }
        }

        Messages.Submit(MessageType.ReadyToBeTypeChecked, variable);
        return true;
    }

    public static void AddUnion(UnionAst union)
    {
        Messages.Submit(MessageType.ReadyToBeTypeChecked, union);
        AddTypeAndIdentifier(union.Name, union, union);
    }

    public static void AddInterface(InterfaceAst interfaceAst)
    {
        Messages.Submit(MessageType.ReadyToBeTypeChecked, interfaceAst);
        AddTypeAndIdentifier(interfaceAst.Name, interfaceAst, interfaceAst);
    }

    public static void AddLibrary(CompilerDirectiveAst directive)
    {
        var library = directive.Library;
#if _LINUX
        library.HasLib = File.Exists($"{library.AbsolutePath}.a");
        library.HasDll = File.Exists($"{library.AbsolutePath}.so");
        if (!library.HasLib && !library.HasDll)
        {
            ErrorReporter.Report($"Unable to find .a/.so '{library.Path}' of library '{library.Name}'", directive);
        }
#elif _WINDOWS
        library.HasLib = File.Exists($"{library.AbsolutePath}.lib");
        library.HasDll = File.Exists($"{library.AbsolutePath}.dll");
        if (!library.HasLib && !library.HasDll)
        {
            ErrorReporter.Report($"Unable to find .lib/.dll '{library.Path}' of library '{library.Name}'", directive);
        }
#endif

        if (!_libraries.TryAdd(library.Name, library))
        {
            ErrorReporter.Report($"Library '{library.Name}' already defined", directive);
        }
    }

    public static void AddSystemLibrary(CompilerDirectiveAst directive)
    {
        var library = directive.Library;
        if (library.LibPath != null && !Directory.Exists(library.LibPath))
        {
            ErrorReporter.Report($"Libary path '{library.LibPath}' of library '{library.Name}' does not exist", directive);
        }

        if (!_libraries.TryAdd(library.Name, library))
        {
            ErrorReporter.Report($"Library '{library.Name}' already defined", directive);
        }
    }

    public static void VerifyEnum(EnumAst enumAst)
    {
        Messages.Submit(MessageType.ReadyToBeTypeChecked, enumAst);
        if (!string.IsNullOrEmpty(enumAst.Name) && AddTypeAndIdentifier(enumAst.Name, enumAst, enumAst))
        {
            TypeTable.CreateTypeInfo(enumAst);
            Messages.Submit(MessageType.TypeCheckSuccessful, enumAst);
        }
    }

    private static void VerifyStruct(StructAst structAst)
    {
        // Verify struct fields have valid types
        var fieldNames = new HashSet<string>();
        structAst.Verifying = true;
        var i = 0;
        var scope = GetFileScope(structAst);

        if (structAst.BaseTypeDefinition != null)
        {
            var baseType = VerifyType(structAst.BaseTypeDefinition, scope, out var isGeneric, out var isVarargs, out var isParams);

            if (isVarargs || isParams || isGeneric)
            {
                ErrorReporter.Report($"Struct base type must be a struct", structAst.BaseTypeDefinition);
            }
            else if (baseType == null)
            {
                ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(structAst.BaseTypeDefinition)}' as the base type of struct '{structAst.Name}'", structAst.BaseTypeDefinition);
            }
            else if (baseType is not StructAst baseTypeStruct)
            {
                ErrorReporter.Report($"Base type '{PrintTypeDefinition(structAst.BaseTypeDefinition)}' of struct '{structAst.Name}' is not a struct", structAst.BaseTypeDefinition);
            }
            else
            {
                // Copy fields from the base struct into the new struct
                if (baseTypeStruct.Verified)
                {
                    foreach (var field in baseTypeStruct.Fields)
                    {
                        fieldNames.Add(field.Name);
                        structAst.Fields.Insert(i++, field);
                    }

                    structAst.Size = baseTypeStruct.Size;
                    structAst.Alignment = baseTypeStruct.Alignment;
                }
                else
                {
                    foreach (var field in baseTypeStruct.Fields)
                    {
                        fieldNames.Add(field.Name);
                        structAst.Fields.Insert(i++, field);

                        if (field.Type == null)
                        {
                            field.Type = VerifyType(field.TypeDefinition, scope);
                        }

                        if (field.Type != null)
                        {
                            if (field.Type.Alignment > structAst.Alignment)
                            {
                                structAst.Alignment = field.Type.Alignment;
                            }

                            var alignmentOffset = structAst.Size % field.Type.Alignment;
                            if (alignmentOffset > 0)
                            {
                                structAst.Size += field.Type.Alignment - alignmentOffset;
                            }

                            field.Offset = structAst.Size;
                            structAst.Size += field.Type.Size;
                        }
                    }
                }

                structAst.BaseStruct = baseTypeStruct;
            }
        }

        for (; i < structAst.Fields.Count; i++)
        {
            var structField = structAst.Fields[i];
            // Check if the field has been previously defined
            if (!fieldNames.Add(structField.Name))
            {
                ErrorReporter.Report($"Struct '{structAst.Name}' already contains field '{structField.Name}'", structField);
            }

            if (structField.TypeDefinition != null)
            {
                structField.Type = VerifyType(structField.TypeDefinition, scope, out var isGeneric, out var isVarargs, out var isParams);

                if (isVarargs || isParams)
                {
                    ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be varargs or Params", structField.TypeDefinition);
                }
                else if (structField.Type == null)
                {
                    ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(structField.TypeDefinition)}' in struct field '{structAst.Name}.{structField.Name}'", structField.TypeDefinition);
                }
                else if (structField.Type.TypeKind == TypeKind.Void)
                {
                    ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be assigned type 'void'", structField.TypeDefinition);
                }
                else if (structField.Type.TypeKind == TypeKind.Array)
                {
                    var arrayStruct = (StructAst)structField.Type;
                    structField.ArrayElementType = arrayStruct.GenericTypes[0];
                }
                else if (structField.Type.TypeKind == TypeKind.CArray)
                {
                    var arrayType = (ArrayType)structField.Type;
                    structField.ArrayElementType = arrayType.ElementType;
                }

                if (structField.Value != null)
                {
                    if (structField.Value is NullAst nullAst)
                    {
                        if (structField.Type != null && structField.Type.TypeKind != TypeKind.Pointer)
                        {
                            ErrorReporter.Report("Cannot assign null to non-pointer type", structField.Value);
                        }

                        nullAst.TargetType = structField.Type;
                    }
                    else
                    {
                        var valueType = VerifyExpression(structField.Value, null, scope, out var isConstant);

                        // Verify the type is correct
                        if (valueType != null)
                        {
                            if (!TypeEquals(structField.Type, valueType))
                            {
                                ErrorReporter.Report($"Expected struct field value to be type '{PrintTypeDefinition(structField.TypeDefinition)}', but got '{valueType.Name}'", structField.Value);
                            }
                            else if (!isConstant)
                            {
                                ErrorReporter.Report("Default values in structs must be constant", structField.Value);
                            }
                            else
                            {
                                VerifyConstantIfNecessary(structField.Value, structField.Type);
                            }
                        }
                    }
                }
                else if (structField.Assignments != null)
                {
                    if (structField.Type != null && structField.Type.TypeKind != TypeKind.Struct && structField.Type.TypeKind != TypeKind.String)
                    {
                        ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(structField.TypeDefinition)}'", structField.TypeDefinition);
                    }
                    // Catch circular references
                    else if (structField.Type != null && structField.Type != structAst)
                    {
                        var structDef = structField.Type as StructAst;
                        if (structDef != null && !structDef.Verified)
                        {
                            VerifyStruct(structDef);
                        }
                        foreach (var (name, assignment) in structField.Assignments)
                        {
                            VerifyFieldAssignment(structDef, name, assignment, null, scope, structField: true);
                        }
                    }
                }
                else if (structField.ArrayValues != null)
                {
                    if (structField.Type != null && structField.Type.TypeKind != TypeKind.Array && structField.Type.TypeKind != TypeKind.CArray)
                    {
                        ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(structField.TypeDefinition)}'", structField.TypeDefinition);
                    }
                    else
                    {
                        structField.TypeDefinition.ConstCount = (uint)structField.ArrayValues.Count;
                        var elementType = structField.ArrayElementType;
                        foreach (var value in structField.ArrayValues)
                        {
                            var valueType = VerifyExpression(value, null, scope, out var isConstant);
                            if (valueType != null)
                            {
                                if (!TypeEquals(elementType, valueType))
                                {
                                    ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                                }
                                else if (!isConstant)
                                {
                                    ErrorReporter.Report("Default values in structs array initializers should be constant", value);
                                }
                                else
                                {
                                    VerifyConstantIfNecessary(value, elementType);
                                }
                            }
                        }
                    }
                }

                // Check type count
                if (structField.Type?.TypeKind == TypeKind.Array && structField.TypeDefinition.Count != null)
                {
                    // Verify the count is a constant
                    var countType = VerifyExpression(structField.TypeDefinition.Count, null, scope, out var isConstant, out uint arrayLength);

                    if (countType?.TypeKind != TypeKind.Integer || !isConstant || arrayLength < 0)
                    {
                        ErrorReporter.Report($"Expected size of '{structAst.Name}.{structField.Name}' to be a constant, positive integer", structField.TypeDefinition.Count);
                    }
                    else
                    {
                        structField.TypeDefinition.ConstCount = arrayLength;
                    }
                }
            }
            else
            {
                if (structField.Value != null)
                {
                    if (structField.Value is NullAst)
                    {
                        ErrorReporter.Report("Cannot assign null value without declaring a type", structField.Value);
                    }
                    else
                    {
                        var valueType = VerifyExpression(structField.Value, null, scope, out var isConstant);

                        if (!isConstant)
                        {
                            ErrorReporter.Report("Default values in structs must be constant", structField.Value);
                        }
                        if (valueType?.TypeKind == TypeKind.Void)
                        {
                            ErrorReporter.Report($"Struct field '{structAst.Name}.{structField.Name}' cannot be assigned type 'void'", structField.Value);
                        }
                        structField.Type = valueType;
                    }
                }
                else if (structField.Assignments != null)
                {
                    ErrorReporter.Report("Struct literals are not yet supported", structField);
                }
                else if (structField.ArrayValues != null)
                {
                    ErrorReporter.Report($"Declaration for struct field '{structAst.Name}.{structField.Name}' with array initializer must have the type declared", structField);
                }
            }

            // Check for circular dependencies and set the size and offset
            if (structAst == structField.Type)
            {
                ErrorReporter.Report($"Struct '{structAst.Name}' contains circular reference in field '{structField.Name}'", structField);
            }
            else if (structField.Type != null)
            {
                if (structField.Type is StructAst fieldStruct)
                {
                    if (!fieldStruct.Verified)
                    {
                        VerifyStruct(fieldStruct);
                    }
                }
                else if (structField.Type is UnionAst fieldUnion)
                {
                    if (!fieldUnion.Verified)
                    {
                        VerifyUnion(fieldUnion);
                    }
                }

                if (structField.Type.Alignment > 0)
                {
                    if (structField.Type.Alignment > structAst.Alignment)
                    {
                        structAst.Alignment = structField.Type.Alignment;
                    }

                    var alignmentOffset = structAst.Size % structField.Type.Alignment;
                    if (alignmentOffset > 0)
                    {
                        structAst.Size += structField.Type.Alignment - alignmentOffset;
                    }
                }

                structField.Offset = structAst.Size;
                structAst.Size += structField.Type.Size;
            }
        }

        if (structAst.Size > 0)
        {
            var alignmentOffset = structAst.Size % structAst.Alignment;
            if (alignmentOffset > 0)
            {
                structAst.Size += structAst.Alignment - alignmentOffset;
            }
        }

        if (!ErrorReporter.Errors.Any())
        {
            TypeTable.CreateTypeInfo(structAst);
            Messages.Submit(MessageType.TypeCheckSuccessful, structAst);
        }
        structAst.Verified = true;
    }

    private static void VerifyUnion(UnionAst union)
    {
        var fieldNames = new HashSet<string>();
        union.Verifying = true;
        var scope = GetFileScope(union);

        if (union.Fields.Count == 0)
        {
            ErrorReporter.Report($"Union '{union.Name}' must have 1 or more fields", union);
        }
        else
        {
            for (var i = 0; i < union.Fields.Count; i++)
            {
                var field = union.Fields[i];
                // Check if the field has been previously defined
                if (!fieldNames.Add(field.Name))
                {
                    ErrorReporter.Report($"Union '{union.Name}' already contains field '{field.Name}'", field);
                }

                field.Type = VerifyType(field.TypeDefinition, scope, out var isGeneric, out var isVarargs, out var isParams);

                if (isVarargs || isParams)
                {
                    ErrorReporter.Report($"Union field '{union.Name}.{field.Name}' cannot be varargs or Params", field.TypeDefinition);
                }
                else if (field.Type == null)
                {
                    ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(field.TypeDefinition)}' in union field '{union.Name}.{field.Name}'", field.TypeDefinition);
                }
                else if (field.Type.TypeKind == TypeKind.Void)
                {
                    ErrorReporter.Report($"Union field '{union.Name}.{field.Name}' cannot be assigned type 'void'", field.TypeDefinition);
                }

                // Check type count
                if (field.Type?.TypeKind == TypeKind.Array && field.TypeDefinition.Count != null)
                {
                    // Verify the count is a constant
                    var countType = VerifyExpression(field.TypeDefinition.Count, null, scope, out var isConstant, out uint arrayLength);

                    if (countType?.TypeKind != TypeKind.Integer || !isConstant)
                    {
                        ErrorReporter.Report($"Expected size of '{union.Name}.{field.Name}' to be a constant, positive integer", field.TypeDefinition.Count);
                    }
                    else
                    {
                        field.TypeDefinition.ConstCount = arrayLength;
                    }
                }

                // Check for circular dependencies and set the size
                if (union == field.Type)
                {
                    ErrorReporter.Report($"Union '{union.Name}' contains circular reference in field '{field.Name}'", field);
                }
                else if (field.Type != null)
                {
                    if (field.Type is StructAst fieldStruct)
                    {
                        if (!fieldStruct.Verified)
                        {
                            VerifyStruct(fieldStruct);
                        }
                    }
                    else if (field.Type is UnionAst fieldUnion)
                    {
                        if (!fieldUnion.Verified)
                        {
                            VerifyUnion(fieldUnion);
                        }
                    }
                    if (field.Type.Alignment > union.Alignment)
                    {
                        union.Alignment = field.Type.Alignment;
                    }
                    if (field.Type.Size > union.Size)
                    {
                        union.Size = field.Type.Size;
                    }
                }
            }
        }

        if (union.Size > 0)
        {
            var alignmentOffset = union.Size % union.Alignment;
            if (alignmentOffset > 0)
            {
                union.Size += union.Alignment - alignmentOffset;
            }
        }

        if (!ErrorReporter.Errors.Any())
        {
            TypeTable.CreateTypeInfo(union);
            Messages.Submit(MessageType.TypeCheckSuccessful, union);
        }
        union.Verified = true;
    }

    private static bool VerifyFunctionDefinition(FunctionAst function)
    {
        function.Flags |= FunctionFlags.DefinitionVerified;
        var scope = GetFileScope(function);

        // 1. Verify the return type of the function is valid
        if (function.ReturnTypeDefinition == null)
        {
            function.ReturnType = TypeTable.VoidType;
        }
        else
        {
            function.ReturnType = VerifyType(function.ReturnTypeDefinition, scope, out var isGeneric, out var isVarargs, out var isParams);
            if (isVarargs || isParams)
            {
                ErrorReporter.Report($"Return type of function '{function.Name}' cannot be varargs or Params", function.ReturnTypeDefinition);
            }
            else if (function.ReturnType == null && !isGeneric)
            {
                ErrorReporter.Report($"Return type '{PrintTypeDefinition(function.ReturnTypeDefinition)}' of function '{function.Name}' is not defined", function.ReturnTypeDefinition);
            }
            else if (function.ReturnType?.TypeKind == TypeKind.Array && function.ReturnTypeDefinition.Count != null)
            {
                ErrorReporter.Report($"Size of Array does not need to be specified for return type of function '{function.Name}'", function.ReturnTypeDefinition.Count);
            }
            else if (function.Flags.HasFlag(FunctionFlags.Extern) && function.ReturnType?.TypeKind == TypeKind.String)
            {
                function.ReturnType = TypeTable.RawStringType;
            }
        }

        // 2. Verify the argument types
        var argumentNames = new HashSet<string>();
        foreach (var argument in function.Arguments)
        {
            // 3a. Check if the argument has been previously defined
            if (!argumentNames.Add(argument.Name))
            {
                ErrorReporter.Report($"Function '{function.Name}' already contains argument '{argument.Name}'", argument);
            }

            // 3b. Check for errored or undefined field types
            argument.Type = VerifyType(argument.TypeDefinition, scope, out var isGeneric, out var isVarargs, out var isParams, allowParams: true);

            if (isVarargs)
            {
                if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
                {
                    ErrorReporter.Report($"Function '{function.Name}' cannot have multiple varargs", argument.TypeDefinition);
                }
                function.Flags |= FunctionFlags.Varargs;
            }
            else if (isParams)
            {
                if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
                {
                    ErrorReporter.Report($"Function '{function.Name}' cannot have multiple varargs", argument.TypeDefinition);
                }
                function.Flags |= FunctionFlags.Params;
                if (!isGeneric)
                {
                    var paramsArrayType = (StructAst)argument.Type;
                    function.ParamsElementType = paramsArrayType.GenericTypes[0];
                }
            }
            else if (argument.Type == null && !isGeneric)
            {
                ErrorReporter.Report($"Type '{PrintTypeDefinition(argument.TypeDefinition)}' of argument '{argument.Name}' in function '{function.Name}' is not defined", argument.TypeDefinition);
            }
            else if (argument.Type?.TypeKind == TypeKind.Void)
            {
                ErrorReporter.Report($"Argument '{argument.Name}' in function '{function.Name}' is cannot be void", argument.TypeDefinition);
            }
            else
            {
                if (function.Flags.HasFlag(FunctionFlags.Varargs))
                {
                    ErrorReporter.Report($"Cannot declare argument '{argument.Name}' following varargs", argument);
                }
                else if (function.Flags.HasFlag(FunctionFlags.Params))
                {
                    ErrorReporter.Report($"Cannot declare argument '{argument.Name}' following params", argument);
                }
                else if (function.Flags.HasFlag(FunctionFlags.Extern) && argument.Type?.TypeKind == TypeKind.String)
                {
                    argument.Type = TypeTable.RawStringType;
                }
            }

            // 3c. Check for default arguments
            if (argument.Value != null)
            {
                var defaultType = VerifyExpression(argument.Value, null, scope, out var isConstant);

                if (argument.HasGenerics)
                {
                    ErrorReporter.Report($"Argument '{argument.Name}' in function '{function.Name}' cannot have default value if the argument has a generic type", argument.Value);
                }
                else if (argument.Value is NullAst nullAst && argument.Type != null)
                {
                    if (argument.Type.TypeKind is TypeKind.Pointer or TypeKind.Interface)
                    {
                        nullAst.TargetType = argument.Type;
                    }
                    else
                    {
                        ErrorReporter.Report($"Type of argument '{argument.Name}' in function '{function.Name}' is '{PrintTypeDefinition(argument.TypeDefinition)}', but default value is 'null'", argument.Value);
                    }
                }
                else if (defaultType != null)
                {
                    if (!isConstant)
                    {
                        ErrorReporter.Report($"Expected default value of argument '{argument.Name}' in function '{function.Name}' to be a constant value", argument.Value);

                    }
                    else if (argument.Type != null && !TypeEquals(argument.Type, defaultType))
                    {
                        ErrorReporter.Report($"Type of argument '{argument.Name}' in function '{function.Name}' is '{argument.Type.Name}', but default value is type '{defaultType.Name}'", argument.Value);
                    }
                    else
                    {
                        VerifyConstantIfNecessary(argument.Value, argument.Type);
                    }
                }
            }
        }

        if (function.Flags.HasFlag(FunctionFlags.Varargs) || function.Flags.HasFlag(FunctionFlags.Params))
        {
            function.ArgumentCount = function.Arguments.Count - 1;
        }
        else
        {
            function.ArgumentCount = function.Arguments.Count;
        }

        if (function.Flags.HasFlag(FunctionFlags.PassCallLocation))
        {
            function.Arguments.Add(new DeclarationAst { Name = "file", Type = TypeTable.StringType });
            function.Arguments.Add(new DeclarationAst { Name = "line", Type = TypeTable.S32Type });
            function.Arguments.Add(new DeclarationAst { Name = "column", Type = TypeTable.S32Type });
        }

        if (function.Flags.HasFlag(FunctionFlags.Inline))
        {
            if (function.Flags.HasFlag(FunctionFlags.Extern))
            {
                ErrorReporter.Report("Extern functions cannot be inlined, remove #inline directive from function definition", function);
            }
            else if (function.Flags.HasFlag(FunctionFlags.Compiler))
            {
                ErrorReporter.Report("Functions that call the compiler cannot be inlined, remove #inline directive from function definition", function);
            }
            else if (function.Flags.HasFlag(FunctionFlags.Syscall))
            {
                ErrorReporter.Report("System calls are already inlined, remove #inline directive", function);
            }
        }

        // 3. Verify main function return type and arguments
        if (function.Name == "main")
        {
            if (function.ReturnType?.TypeKind != TypeKind.Void)
            {
                ErrorReporter.Report("The main function should return type 'void'", function);
            }

            if (function.Arguments.Count != 0)
            {
                ErrorReporter.Report("The main function should have 0 arguments", function);
            }

            if (function.Generics.Any())
            {
                ErrorReporter.Report("The main function cannot have generics", function);
            }

            if (function.Flags.HasFlag(FunctionFlags.Extern))
            {
                ErrorReporter.Report("The main function cannot be extern", function);
            }
        }

        // 4. Create the type info and verify the function if possible
        if (!function.Generics.Any())
        {
            TypeTable.Add(function);
            TypeTable.CreateTypeInfo(function);

            if (function.Flags.HasFlag(FunctionFlags.Extern))
            {
                if (function.LibraryName != null)
                {
                    if (_libraries.TryGetValue(function.LibraryName, out var library))
                    {
                        function.Library = library;
                    }
                    else
                    {
                        ErrorReporter.Report($"Function '{function.Name}' references undefined library '{function.LibraryName}'", function);
                    }
                }

                Messages.Submit(MessageType.TypeCheckSuccessful, function);
                function.Flags |= FunctionFlags.Verified;
            }
            else if (function.Flags.HasFlag(FunctionFlags.Compiler) || function.Flags.HasFlag(FunctionFlags.Syscall))
            {
                Messages.Submit(MessageType.TypeCheckSuccessful, function);
                function.Flags |= FunctionFlags.Verified;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static bool OverloadExistsForFunction(FunctionAst currentFunction)
    {
        if (currentFunction.Name == null) return false;

        var privateScope = PrivateScopes[currentFunction.FileIndex];

        if (privateScope == null)
        {
            if (GlobalScope.Functions.TryGetValue(currentFunction.Name, out var functions))
            {
                return OverloadExists(currentFunction, functions);
            }
        }
        else
        {
            if (privateScope.Functions.TryGetValue(currentFunction.Name, out var functions))
            {
                if (OverloadExists(currentFunction, functions))
                {
                    return true;
                }
            }

            if (GlobalScope.Functions.TryGetValue(currentFunction.Name, out var globalFunctions))
            {
                return OverloadExists(currentFunction, globalFunctions);
            }
        }

        return false;
    }

    private static bool OverloadExistsForPolymorphicFunction(FunctionAst currentFunction)
    {
        var privateScope = PrivateScopes[currentFunction.FileIndex];

        if (privateScope == null)
        {
            if (GlobalScope.PolymorphicFunctions.TryGetValue(currentFunction.Name, out var functions))
            {
                return OverloadExists(currentFunction, functions, true);
            }
        }
        else
        {
            if (privateScope.PolymorphicFunctions.TryGetValue(currentFunction.Name, out var functions))
            {
                if (OverloadExists(currentFunction, functions, true))
                {
                    return true;
                }
            }

            if (GlobalScope.PolymorphicFunctions.TryGetValue(currentFunction.Name, out var globalFunctions))
            {
                return OverloadExists(currentFunction, globalFunctions, true);
            }
        }

        return false;
    }

    private static bool OverloadExists(FunctionAst currentFunction, List<FunctionAst> functions, bool checkGenerics = false)
    {
        for (var function = 0; function < functions.Count; function++)
        {
            var existingFunction = functions[function];
            if (currentFunction.Arguments.Count == existingFunction.Arguments.Count)
            {
                var match = true;
                for (var i = 0; i < currentFunction.Arguments.Count; i++)
                {
                    if (!TypeDefinitionEquals(currentFunction.Arguments[i].TypeDefinition, existingFunction.Arguments[i].TypeDefinition))
                    {
                        match = false;
                        break;
                    }
                }
                if (match && (!checkGenerics || currentFunction.Generics.Count == existingFunction.Generics.Count))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool VerifyOperatorOverloadDefinition(OperatorOverloadAst overload)
    {
        overload.Flags |= FunctionFlags.DefinitionVerified;
        var scope = GetFileScope(overload);

        // 1. Verify the operator type exists and is a struct
        if (overload.Generics.Any())
        {
            if (GetPolymorphicStruct(overload.Type.Name, overload.FileIndex, out var structDef))
            {
                if (structDef.Generics.Count != overload.Generics.Count)
                {
                    ErrorReporter.Report($"Expected type '{overload.Type.Name}' to have {structDef.Generics.Count} generic(s), but got {overload.Generics.Count}", overload.Type);
                }
            }
            else
            {
                ErrorReporter.Report($"No polymorphic structs of type '{overload.Type.Name}'", overload.Type);
            }
        }
        else
        {
            var targetType = VerifyType(overload.Type, scope, out var isGeneric, out var isVarargs, out var isParams);
            if (isVarargs || isParams)
            {
                ErrorReporter.Report($"Cannot overload operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}'", overload.Type);
            }
            else
            {
                switch (targetType?.TypeKind)
                {
                    case TypeKind.Struct:
                    case TypeKind.String when overload.Operator != Operator.Subscript:
                    case null:
                        break;
                    default:
                        ErrorReporter.Report($"Cannot overload operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}'", overload.Type);
                        break;
                }
            }
        }

        // 2. Verify the argument types
        if (overload.Arguments.Count != 2)
        {
            ErrorReporter.Report($"Overload of operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}' should contain exactly 2 arguments to represent the l-value and r-value of the expression", overload);
        }
        var argumentNames = new HashSet<string>();
        for (var i = 0; i < overload.Arguments.Count; i++)
        {
            var argument = overload.Arguments[i];
            // 2a. Check if the argument has been previously defined
            if (!argumentNames.Add(argument.Name))
            {
                ErrorReporter.Report($"Overload of operator '{PrintOperator(overload.Operator)}' for type '{PrintTypeDefinition(overload.Type)}' already contains argument '{argument.Name}'", argument);
            }

            // 2b. Check the argument is the same type as the overload type
            argument.Type = VerifyType(argument.TypeDefinition, scope);
            if (i > 0)
            {
                if (overload.Operator == Operator.Subscript)
                {
                    if (argument.Type?.TypeKind != TypeKind.Integer)
                    {
                        ErrorReporter.Report($"Expected second argument of overload of operator '{PrintOperator(overload.Operator)}' to be an integer, but got '{PrintTypeDefinition(argument.TypeDefinition)}'", argument.TypeDefinition);
                    }
                }
                else if (!TypeDefinitionEquals(overload.Type, argument.TypeDefinition))
                {
                    ErrorReporter.Report($"Expected overload of operator '{PrintOperator(overload.Operator)}' argument type to be '{PrintTypeDefinition(overload.Type)}', but got '{PrintTypeDefinition(argument.TypeDefinition)}'", argument.TypeDefinition);
                }
            }
        }
        overload.ReturnType = VerifyType(overload.ReturnTypeDefinition, scope);

        return !overload.Generics.Any();
    }

    private static void VerifyInterface(InterfaceAst interfaceAst)
    {
        interfaceAst.Verifying = true;
        var scope = GetFileScope(interfaceAst);

        // 1. Verify the return type of the function is valid
        if (interfaceAst.ReturnTypeDefinition == null)
        {
            interfaceAst.ReturnType = TypeTable.VoidType;
        }
        else
        {
            interfaceAst.ReturnType = VerifyType(interfaceAst.ReturnTypeDefinition, scope, out _, out var isVarargs, out var isParams);
            if (isVarargs || isParams)
            {
                ErrorReporter.Report($"Return type of interface '{interfaceAst.Name}' cannot be varargs or Params", interfaceAst.ReturnTypeDefinition);
            }
            else if (interfaceAst.ReturnType == null)
            {
                ErrorReporter.Report($"Return type '{PrintTypeDefinition(interfaceAst.ReturnTypeDefinition)}' of interface '{interfaceAst.Name}' is not defined", interfaceAst.ReturnTypeDefinition);
            }
            else if (interfaceAst.ReturnType.TypeKind == TypeKind.Array && interfaceAst.ReturnTypeDefinition.Count != null)
            {
                ErrorReporter.Report($"Size of Array does not need to be specified for return type of interface '{interfaceAst.Name}'", interfaceAst.ReturnTypeDefinition.Count);
            }
        }

        // 2. Verify the argument types
        var argumentNames = new HashSet<string>();
        foreach (var argument in interfaceAst.Arguments)
        {
            // 3a. Check if the argument has been previously defined
            if (!argumentNames.Add(argument.Name))
            {
                ErrorReporter.Report($"Interface '{interfaceAst.Name}' already contains argument '{argument.Name}'", argument);
            }

            // 3b. Check for errored or undefined field types
            argument.Type = VerifyType(argument.TypeDefinition, scope, out _, out var isVarargs, out var isParams);

            if (isVarargs)
            {
                ErrorReporter.Report($"Interface '{interfaceAst.Name}' cannot have varargs arguments", argument.TypeDefinition);
            }
            else if (isParams)
            {
                ErrorReporter.Report($"Interface '{interfaceAst.Name}' cannot have params", argument.TypeDefinition);
            }
            else if (argument.Type == null)
            {
                ErrorReporter.Report($"Type '{PrintTypeDefinition(argument.TypeDefinition)}' of argument '{argument.Name}' in interface '{interfaceAst.Name}' is not defined", argument.TypeDefinition);
            }
        }
        interfaceAst.ArgumentCount = interfaceAst.Arguments.Count;

        // 3. Load the interface into the type table
        if (!ErrorReporter.Errors.Any())
        {
            TypeTable.CreateTypeInfo(interfaceAst);
            Messages.Submit(MessageType.TypeCheckSuccessful, interfaceAst);
        }
        interfaceAst.Verified = true;
    }

    private static bool GetScopeIdentifier(IScope scope, string name, out IAst ast)
    {
        return GetScopeIdentifier(scope, name, out ast, out _);
    }

    private static bool GetScopeIdentifier(IScope scope, string name, out IAst ast, out bool global)
    {
        do {
            if (scope.Identifiers.TryGetValue(name, out ast))
            {
                global = scope is not ScopeAst;
                return true;
            }
            scope = scope.Parent;
        } while (scope != null);
        global = false;
        return false;
    }

    public static void VerifyFunction(FunctionAst function, bool queueBuild = true)
    {
        if (!function.Flags.HasFlag(FunctionFlags.DefinitionVerified))
        {
            if (!VerifyFunctionDefinition(function))
            {
                return;
            }
        }
        else if (function.Generics.Any())
        {
            return;
        }

        // Set the function as verified to prevent multiple verifications
        function.Flags |= FunctionFlags.Verified;
        Debug.Assert(function.Body != null, "Should not verify function without body");
        var scope = GetFileScope(function);

        // 1. Initialize local variables
        foreach (var argument in function.Arguments)
        {
            // Arguments with the same name as a global variable will be used instead of the global
            if (GetScopeIdentifier(scope, argument.Name, out var identifier))
            {
                if (identifier is not DeclarationAst)
                {
                    ErrorReporter.Report($"Argument '{argument.Name}' already exists as a type", argument);
                }
            }
            function.Body.Identifiers[argument.Name] = argument;
        }

        // 2. Loop through function body and verify all ASTs
        function.Body.Parent = scope;
        VerifyScope(function.Body, function);

        // 3. Verify the main function doesn't call the compiler
        if (function.Name == "main" && function.Flags.HasFlag(FunctionFlags.CallsCompiler))
        {
            ErrorReporter.Report("The main function cannot call the compiler", function);
        }

        // 4. Verify the function returns on all paths
        if (!function.Body.Returns)
        {
            if (function.ReturnType?.TypeKind != TypeKind.Void)
            {
                ErrorReporter.Report($"Function '{function.Name}' does not return type '{PrintTypeDefinition(function.ReturnTypeDefinition)}' on all paths", function);
            }
            else
            {
                function.Flags |= FunctionFlags.ReturnVoidAtEnd;
            }
        }

        if (queueBuild && !ErrorReporter.Errors.Any() && !function.Flags.HasFlag(FunctionFlags.Inline))
        {
            Messages.Submit(MessageType.TypeCheckSuccessful, function);
            ProgramIRBuilder.QueueBuildFunction(function);
        }
    }

    public static void VerifyOperatorOverload(OperatorOverloadAst overload, bool queueBuild = true)
    {
        if (!VerifyOperatorOverloadDefinition(overload))
        {
            return;
        }

        // Set the overload as verified to prevent multiple verifications
        overload.Flags |= FunctionFlags.Verified;
        Debug.Assert(overload.Body != null, "Should not verify overload without body");
        var scope = GetFileScope(overload);

        // 1. Initialize local variables
        foreach (var argument in overload.Arguments)
        {
            // Arguments with the same name as a global variable will be used instead of the global
            if (GetScopeIdentifier(scope, argument.Name, out var identifier))
            {
                if (identifier is not DeclarationAst)
                {
                    ErrorReporter.Report($"Argument '{argument.Name}' already exists as a type", argument);
                }
            }
            overload.Body.Identifiers[argument.Name] = argument;
        }

        // 2. Loop through body and verify all ASTs
        overload.Body.Parent = scope;
        VerifyScope(overload.Body, overload);

        // 3. Verify the body returns on all paths
        if (!overload.Body.Returns)
        {
            ErrorReporter.Report($"Overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}' does not return type '{PrintTypeDefinition(overload.ReturnTypeDefinition)}' on all paths", overload);
        }

        if (queueBuild && !ErrorReporter.Errors.Any())
        {
            Messages.Submit(MessageType.TypeCheckSuccessful, overload);
            ProgramIRBuilder.QueueBuildOperatorOverload(overload);
        }
    }

    public static void VerifyScope(ScopeAst scope, IFunction function, bool inDefer = false, bool canBreak = false, int? endIndex = null)
    {
        var end = endIndex ?? scope.Children.Count;
        for (var i = 0; i < end; i++)
        {
            var ast = scope.Children[i];
            switch (ast)
            {
                case ScopeAst childScope:
                    childScope.Parent = scope;
                    VerifyScope(childScope, function, inDefer, canBreak);
                    if (childScope.Returns)
                    {
                        scope.Returns = true;
                    }
                    if (childScope.Breaks)
                    {
                        scope.Breaks = true;
                    }
                    break;
                case WhileAst whileAst:
                    whileAst.Body.Parent = scope;
                    VerifyCondition(whileAst.Condition, function, scope);
                    VerifyScope(whileAst.Body, function, inDefer, true);
                    break;
                case EachAst each:
                    each.Body.Parent = scope;
                    VerifyEach(each, function, scope);
                    VerifyScope(each.Body, function, inDefer, true);
                    break;
                case ConditionalAst conditional:
                    VerifyCondition(conditional.Condition, function, scope);
                    conditional.IfBlock.Parent = scope;
                    VerifyScope(conditional.IfBlock, function, inDefer, canBreak);
                    if (conditional.ElseBlock != null)
                    {
                        conditional.ElseBlock.Parent = scope;
                        VerifyScope(conditional.ElseBlock, function, inDefer, canBreak);

                        if (conditional.IfBlock.Returns && conditional.ElseBlock.Returns)
                        {
                            scope.Returns = true;
                        }
                        if (conditional.IfBlock.Breaks && conditional.ElseBlock.Breaks)
                        {
                            scope.Breaks = true;
                        }
                    }
                    break;
                case CompilerDirectiveAst directive:
                    switch (directive.Type)
                    {
                        case DirectiveType.If:
                            scope.Children.RemoveAt(i);
                            end--;
                            var conditional = directive.Value as ConditionalAst;
                            if (VerifyCondition(conditional.Condition, null, scope, true))
                            {
                                var condition = ProgramIRBuilder.CreateRunnableCondition(conditional.Condition);

                                if (ProgramRunner.ExecuteCondition(condition, conditional.Condition))
                                {
                                    end += conditional.IfBlock.Children.Count;
                                    scope.Children.InsertRange(i, conditional.IfBlock.Children);
                                }
                                else if (conditional.ElseBlock != null)
                                {
                                    end += conditional.ElseBlock.Children.Count;
                                    scope.Children.InsertRange(i, conditional.ElseBlock.Children);
                                }
                            }
                            i--;
                            break;
                        case DirectiveType.Assert:
                            scope.Children.RemoveAt(i--);
                            end--;
                            if (VerifyCondition(directive.Value, null, scope, true))
                            {
                                var condition = ProgramIRBuilder.CreateRunnableCondition(directive.Value);

                                if (!ProgramRunner.ExecuteCondition(condition, directive.Value))
                                {
                                    if (function is FunctionAst functionAst)
                                    {
                                        var message = $"Assertion failed in function '{functionAst.Name}'";
                                        if (directive.AssertMessage != null)
                                            message += $", {directive.AssertMessage}";
                                        ErrorReporter.Report(message, directive.Value);
                                    }
                                    else if (function is OperatorOverloadAst overload)
                                    {
                                        var message = $"Assertion failed in overload for operator '{PrintOperator(overload.Operator)}' of type '{PrintTypeDefinition(overload.Type)}'";
                                        if (directive.AssertMessage != null)
                                            message += $", {directive.AssertMessage}";
                                        ErrorReporter.Report(message, directive.Value);
                                    }
                                }
                            }
                            break;
                        case DirectiveType.Insert:
                            scope.Children.RemoveAt(i);
                            end--;
                            var insertDirectiveFunction = new RunDirectiveFunction { ReturnType = TypeTable.StringType };
                            var insertScope = (ScopeAst)directive.Value;
                            VerifyScope(insertScope, insertDirectiveFunction);
                            if (!insertScope.Returns)
                            {
                                ErrorReporter.Report($"Expected #insert block to return a string", insertScope);
                            }
                            else
                            {
                                ClearAstQueue();

                                if (!ErrorReporter.Errors.Any())
                                {
                                    var insertFunction = ProgramIRBuilder.CreateRunnableFunction(insertScope, insertDirectiveFunction);
                                    var code = ProgramRunner.ExecuteInsert(insertFunction, insertScope);
                                    end += InsertCode(code, function, scope, directive.FileIndex, directive.Line, directive.Column, i);
                                }
                            }
                            i--;
                            break;
                    }
                    break;
                case ReturnAst returnAst:
                    if (inDefer)
                    {
                        ErrorReporter.Report("Cannot return in a defer statement", returnAst);
                    }
                    VerifyReturnStatement(returnAst, function, scope);
                    scope.Returns = true;
                    break;
                case DeclarationAst declaration:
                    VerifyDeclaration(declaration, function, scope);
                    break;
                case CompoundDeclarationAst compoundDeclaration:
                    VerifyCompoundDeclaration(compoundDeclaration, function, scope);
                    break;
                case AssignmentAst assignment:
                    VerifyAssignment(assignment, function, scope);
                    break;
                case AssemblyAst assembly:
                    VerifyInlineAssembly(assembly, scope);
                    break;
                case SwitchAst switchAst:
                    VerifySwitch(switchAst, function, scope, inDefer, canBreak);
                    break;
                case DeferAst defer:
                    defer.Statement.Parent = scope;
                    VerifyScope(defer.Statement, function, true);
                    break;
                case BreakAst:
                    scope.Breaks = true;
                    if (!canBreak || inDefer)
                    {
                        ErrorReporter.Report("No parent loop to break", ast);
                    }
                    break;
                case ContinueAst:
                    scope.Breaks = true;
                    if (!canBreak || inDefer)
                    {
                        ErrorReporter.Report("No parent loop to continue", ast);
                    }
                    break;
                default:
                    VerifyExpression(ast, function, scope);
                    break;
            }
        }
    }

    public static void AddCode(string code, int fileIndex, uint line, uint column)
    {
        var starting_line = AddCodeString(code, null, fileIndex, line, column);
        Parser.ParseCode(code, _generatedCodeFileIndex, starting_line);
        ClearDirectiveQueue();
    }

    public static int InsertCode(string code, IFunction function, ScopeAst scope, int fileIndex, uint line, uint column, int index = 0)
    {
        var starting_line = AddCodeString(code, function, fileIndex, line, column);
        return Parser.ParseInsertedCode(code, scope, _generatedCodeFileIndex, starting_line, index);
    }

    private static uint AddCodeString(string code, IFunction function, int fileIndex, uint line, uint column)
    {
        if (_generatedCodeWriter == null)
        {
            _generatedCodeWriter = new StreamWriter(BuildSettings.GeneratedCodeFile);
            _generatedCodeLineCount = 1;
            _generatedCodeFileIndex = BuildSettings.AddFile(BuildSettings.GeneratedCodeFile);
        }
        else if (!_generatedCodeWriter.BaseStream.CanSeek)
        {
            _generatedCodeWriter = new StreamWriter(BuildSettings.GeneratedCodeFile, true);
        }

        string header;
        if (function != null)
        {
            header = $"\n// Generated code for {function.Name} in {BuildSettings.FileName(fileIndex)} at line {line}:{column}\n\n";
        }
        else
        {
            header = $"\n// Generated code from call to add_code in {BuildSettings.FileName(fileIndex)} at line {line}:{column}\n\n";
        }

        _generatedCodeWriter.Write(header);

        _generatedCodeLineCount += 3;
        var initialLine = _generatedCodeLineCount;

        foreach (var c in code)
        {
            if (c == '\n')
                _generatedCodeLineCount++;
        }
        _generatedCodeLineCount++;
        _generatedCodeWriter.WriteLine(code);

        return initialLine;
    }

    private static void VerifyReturnStatement(ReturnAst returnAst, IFunction currentFunction, IScope scope)
    {
        var returnType = currentFunction.ReturnType;

        if (returnType != null)
        {
            if (returnAst.Value == null)
            {
                if (returnType.TypeKind != TypeKind.Void)
                {
                    ErrorReporter.Report($"Expected to return type '{returnType.Name}'", returnAst);
                }
            }
            else if (returnAst.Value is NullAst nullAst)
            {
                if (returnType.TypeKind == TypeKind.Pointer)
                {
                    nullAst.TargetType = returnType;
                }
                else
                {
                    ErrorReporter.Report($"Expected to return type '{returnType.Name}'", returnAst);
                }
            }
            else if (returnAst.Value is CompoundExpressionAst compoundExpression)
            {
                if (returnType is CompoundType compoundType && compoundType.Types.Length == compoundExpression.Children.Count)
                {
                    var error = false;
                    for (var i = 0; i < compoundType.Types.Length; i++)
                    {
                        var type = compoundType.Types[i];
                        if (compoundExpression.Children[i] is NullAst nullAstChild)
                        {
                            if (type.TypeKind == TypeKind.Pointer)
                            {
                                nullAstChild.TargetType = type;
                            }
                            else
                            {
                                error = true;
                                break;
                            }
                        }
                        else
                        {
                            var valueType = VerifyExpression(compoundExpression.Children[i], currentFunction, scope);
                            if (!TypeEquals(type, valueType))
                            {
                                error = true;
                                break;
                            }
                        }
                    }
                    if (error)
                    {
                        ErrorReporter.Report($"Expected to return type '{returnType.Name}'", returnAst);
                    }
                }
                else
                {
                    VerifyExpression(returnAst.Value, currentFunction, scope);
                    ErrorReporter.Report($"Expected to return type '{returnType.Name}'", returnAst);
                }
            }
            else
            {
                var returnValueType = VerifyExpression(returnAst.Value, currentFunction, scope);
                // if (returnValueType == null)
                // {
                //     ErrorReporter.Report($"Expected to return type '{returnType.Name}'", returnAst);
                // }
                // else if (!TypeEquals(returnType, returnValueType))
                if (returnValueType != null && !TypeEquals(returnType, returnValueType))
                {
                    ErrorReporter.Report($"Expected to return type '{returnType.Name}', but returned type '{returnValueType.Name}'", returnAst.Value);
                }
            }
        }
        else
        {
            VerifyExpression(returnAst.Value, currentFunction, scope);
        }
    }

    private static void VerifyGlobalVariable(DeclarationAst declaration)
    {
        declaration.Verified = true;
        var scope = GetFileScope(declaration);

        if (declaration.TypeDefinition != null)
        {
            declaration.Type = VerifyType(declaration.TypeDefinition, scope, out _, out var isVarargs, out var isParams, initialArrayLength: declaration.ArrayValues?.Count);
            if (isVarargs || isParams)
            {
                ErrorReporter.Report($"Variable '{declaration.Name}' cannot be varargs or Params", declaration.TypeDefinition);
            }
            else if (declaration.Type == null)
            {
                ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(declaration.TypeDefinition)}' in declaration", declaration.TypeDefinition);
            }
            else if (declaration.Type.TypeKind == TypeKind.Void)
            {
                ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
            }
            else if (declaration.Type.TypeKind == TypeKind.Array)
            {
                var arrayStruct = (StructAst)declaration.Type;
                declaration.ArrayElementType = arrayStruct.GenericTypes[0];
            }
            else if (declaration.Type.TypeKind == TypeKind.CArray)
            {
                var arrayType = (ArrayType)declaration.Type;
                declaration.ArrayElementType = arrayType.ElementType;
            }
        }

        // 2. Verify the null values
        if (declaration.Value is NullAst nullAst)
        {
            // 2a. Verify null can be assigned
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report("Cannot assign null value without declaring a type", declaration.Value);
            }
            else
            {
                if (declaration.Type != null && declaration.Type.TypeKind != TypeKind.Pointer && declaration.Type.TypeKind != TypeKind.Interface)
                {
                    ErrorReporter.Report($"Cannot assign null to non-pointer type '{declaration.Type.Name}'", declaration.Value);
                }

                nullAst.TargetType = declaration.Type;
            }
        }
        // 3. Verify declaration values
        else if (declaration.Value != null)
        {
            VerifyGlobalVariableValue(declaration);
        }
        // 4. Verify object initializers
        else if (declaration.Assignments != null)
        {
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report("Struct literals are not yet supported", declaration);
            }
            else
            {
                if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Struct && declaration.Type.TypeKind != TypeKind.String))
                {
                    ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else
                {
                    var structDef = declaration.Type as StructAst;
                    foreach (var (name, assignment) in declaration.Assignments)
                    {
                        VerifyFieldAssignment(structDef, name, assignment, null, scope, true);
                    }
                }
            }
        }
        // 5. Verify array initializer
        else if (declaration.ArrayValues != null)
        {
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report($"Declaration for variable '{declaration.Name}' with array initializer must have the type declared", declaration);
            }
            else
            {
                if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Array && declaration.Type.TypeKind != TypeKind.CArray))
                {
                    ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else
                {
                    declaration.TypeDefinition.ConstCount = (uint)declaration.ArrayValues.Count;
                    var elementType = declaration.ArrayElementType;
                    foreach (var value in declaration.ArrayValues)
                    {
                        var valueType = VerifyExpression(value, null, scope, out var isConstant);
                        if (valueType != null)
                        {
                            if (!TypeEquals(elementType, valueType))
                            {
                                ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                            }
                            else if (!isConstant)
                            {
                                ErrorReporter.Report($"Global variables can only be initialized with constant values", value);
                            }
                            else
                            {
                                VerifyConstantIfNecessary(value, elementType);
                            }
                        }
                    }
                }
            }
        }
        // 6. Verify compiler constants
        else
        {
            switch (declaration.Name)
            {
                case "os":
                    declaration.Value = GetOSVersion();
                    VerifyGlobalVariableValue(declaration);
                    break;
                case "build_env":
                    declaration.Value = GetBuildEnv();
                    VerifyGlobalVariableValue(declaration);
                    break;
            }
        }

        // 7. Verify the type definition count if necessary
        if (declaration.Type?.TypeKind == TypeKind.Array && declaration.TypeDefinition.Count != null)
        {
            var countType = VerifyExpression(declaration.TypeDefinition.Count, null, scope, out var isConstant, out uint arrayLength);

            if (countType?.TypeKind != TypeKind.Integer || !isConstant || arrayLength < 0)
            {
                ErrorReporter.Report($"Expected Array length of global variable '{declaration.Name}' to be a constant, positive integer", declaration.TypeDefinition.Count);
            }
            else
            {
                declaration.TypeDefinition.ConstCount = arrayLength;
            }
        }

        // 8. Check if the variable was set from input variables
        if (BuildSettings.InputVariables.TryGetValue(declaration.Name, out var inputVariable))
        {
            if (!declaration.Constant)
            {
                ErrorReporter.Report($"Cannot set value from input of non-constant variable '{declaration.Name}'", declaration);
            }
            else
            {
                inputVariable.Used = true;
                var constantValue = Parser.ParseConstant(inputVariable.Token, -1);
                if (constantValue != null)
                {
                    if (!TypeEquals(declaration.Type, constantValue.Type))
                    {
                        ErrorReporter.Report($"Expected type from input variable '{declaration.Name}' to be '{declaration.Type}', but got '{constantValue.Type.Name}'", constantValue);
                    }
                    else
                    {
                        VerifyConstantIfNecessary(constantValue, declaration.Type);
                        declaration.Value = constantValue;
                    }
                }
            }
        }

        // 9. Verify constant values
        if (declaration.Constant)
        {
            if (declaration.Value == null)
            {
                ErrorReporter.Report($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
            }
        }

        if (!ErrorReporter.Errors.Any())
        {
            ProgramIRBuilder.EmitGlobalVariable(declaration, scope);
            Messages.Submit(MessageType.TypeCheckSuccessful, declaration);
        }
    }

    private static void VerifyGlobalVariableValue(DeclarationAst declaration)
    {
        var scope = GetFileScope(declaration);
        var valueType = VerifyExpression(declaration.Value, null, scope, out var isConstant);
        if (!isConstant)
        {
            ErrorReporter.Report($"Global variables can only be initialized with constant values", declaration.Value);
        }

        // Verify the assignment value matches the type definition if it has been defined
        if (declaration.TypeDefinition == null)
        {
            if (valueType?.TypeKind == TypeKind.Void)
            {
                ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
            }
            declaration.Type = valueType;
        }
        else
        {
            // Verify the type is correct
            if (valueType != null)
            {
                if (!TypeEquals(declaration.Type, valueType))
                {
                    ErrorReporter.Report($"Expected declaration value to be type '{declaration.Name}', but got '{valueType.Name}'", declaration.Value);
                }
                else
                {
                    VerifyConstantIfNecessary(declaration.Value, declaration.Type);
                }
            }
        }
    }

    private static void VerifyDeclaration(DeclarationAst declaration, IFunction currentFunction, IScope scope)
    {
        // 1. Verify the variable is already defined
        declaration.Verified = true;
        if (GetScopeIdentifier(scope, declaration.Name, out _))
        {
            ErrorReporter.Report($"Identifier '{declaration.Name}' already defined", declaration);
        }
        else
        {
            scope.Identifiers.TryAdd(declaration.Name, declaration);
        }

        if (declaration.TypeDefinition != null)
        {
            declaration.Type = VerifyType(declaration.TypeDefinition, scope, out _, out var isVarargs, out var isParams, initialArrayLength: declaration.ArrayValues?.Count);
            if (isVarargs || isParams)
            {
                ErrorReporter.Report($"Variable '{declaration.Name}' cannot be varargs or Params", declaration.TypeDefinition);
            }
            else if (declaration.Type == null)
            {
                ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(declaration.TypeDefinition)}' in declaration", declaration.TypeDefinition);
            }
            else if (declaration.Type.TypeKind == TypeKind.Void)
            {
                ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.TypeDefinition);
            }
            else if (declaration.Type.TypeKind == TypeKind.Array)
            {
                var arrayStruct = (StructAst)declaration.Type;
                declaration.ArrayElementType = arrayStruct.GenericTypes[0];
            }
            else if (declaration.Type.TypeKind == TypeKind.CArray)
            {
                var arrayType = (ArrayType)declaration.Type;
                declaration.ArrayElementType = arrayType.ElementType;
            }
        }

        // 2. Verify the null values
        if (declaration.Value is NullAst nullAst)
        {
            // 2a. Verify null can be assigned
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report("Cannot assign null value without declaring a type", declaration.Value);
            }
            else
            {
                if (declaration.Type != null && declaration.Type.TypeKind != TypeKind.Pointer && declaration.Type.TypeKind != TypeKind.Interface)
                {
                    ErrorReporter.Report($"Cannot assign null to non-pointer type '{declaration.Type.Name}'", declaration.Value);
                }

                nullAst.TargetType = declaration.Type;
            }
        }
        // 3. Verify declaration values
        else if (declaration.Value != null)
        {
            var valueType = VerifyExpression(declaration.Value, currentFunction, scope);

            // Verify the assignment value matches the type definition if it has been defined
            if (declaration.Type == null)
            {
                if (valueType?.TypeKind == TypeKind.Void)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned type 'void'", declaration.Value);
                }
                else if (valueType?.TypeKind == TypeKind.Function)
                {
                    ErrorReporter.Report($"Variable '{declaration.Name}' cannot be assigned function '{valueType.Name}' without specifying interface", declaration.Value);
                }
                declaration.Type = valueType;
            }
            else
            {
                // Verify the type is correct
                if (valueType != null)
                {
                    if (!TypeEquals(declaration.Type, valueType))
                    {
                        ErrorReporter.Report($"Expected declaration value to be type '{declaration.Type.Name}', but got '{valueType.Name}'", declaration.Value);
                    }
                    else
                    {
                        VerifyConstantIfNecessary(declaration.Value, declaration.Type);
                    }
                }
            }
        }
        // 4. Verify object initializers
        else if (declaration.Assignments != null)
        {
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report("Struct literals are not yet supported", declaration);
            }
            else
            {
                if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Struct && declaration.Type.TypeKind != TypeKind.String))
                {
                    ErrorReporter.Report($"Can only use object initializer with struct type, got '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else
                {
                    var structDef = declaration.Type as StructAst;
                    foreach (var (name, assignment) in declaration.Assignments)
                    {
                        VerifyFieldAssignment(structDef, name, assignment, currentFunction, scope);
                    }
                }
            }
        }
        // 5. Verify array initializer
        else if (declaration.ArrayValues != null)
        {
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report($"Declaration for variable '{declaration.Name}' with array initializer must have the type declared", declaration);
            }
            else
            {
                if (declaration.Type == null || (declaration.Type.TypeKind != TypeKind.Array && declaration.Type.TypeKind != TypeKind.CArray))
                {
                    ErrorReporter.Report($"Cannot use array initializer to declare non-array type '{PrintTypeDefinition(declaration.TypeDefinition)}'", declaration.TypeDefinition);
                }
                else
                {
                    declaration.TypeDefinition.ConstCount = (uint)declaration.ArrayValues.Count;
                    var elementType = declaration.ArrayElementType;
                    foreach (var value in declaration.ArrayValues)
                    {
                        var valueType = VerifyExpression(value, currentFunction, scope);
                        if (valueType != null)
                        {
                            if (!TypeEquals(elementType, valueType))
                            {
                                ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                            }
                            else
                            {
                                VerifyConstantIfNecessary(value, elementType);
                            }
                        }
                    }
                }
            }
        }

        // 6. Verify the type definition count if necessary
        if (declaration.Type?.TypeKind == TypeKind.Array && declaration.TypeDefinition?.Count != null)
        {
            var countType = VerifyExpression(declaration.TypeDefinition.Count, currentFunction, scope, out var isConstant, out uint arrayLength);

            if (countType != null)
            {
                if (countType.TypeKind != TypeKind.Integer)
                {
                    ErrorReporter.Report($"Expected count of variable '{declaration.Name}' to be a positive integer", declaration.TypeDefinition.Count);
                }
                if (isConstant)
                {
                    declaration.TypeDefinition.ConstCount = arrayLength;
                }
            }
        }

        // 7. Verify constant values
        if (declaration.Constant)
        {
            declaration.ConstantIndex = currentFunction.ConstantCount++;
            switch (declaration.Value)
            {
                case ConstantAst:
                case StructFieldRefAst { IsEnum: true }:
                    break;
                default:
                    ErrorReporter.Report($"Constant variable '{declaration.Name}' should be assigned a constant value", declaration);
                    break;
            }
        }
    }

    private static void VerifyCompoundDeclaration(CompoundDeclarationAst declaration, IFunction currentFunction, IScope scope)
    {
        // 1. Verify the variables are already defined
        for (var i = 0; i < declaration.Variables.Length; i++)
        {
            var variable = declaration.Variables[i];
            if (GetScopeIdentifier(scope, variable.Name, out _))
            {
                ErrorReporter.Report($"Identifier '{variable.Name}' already defined", variable);
            }
            else
            {
                scope.Identifiers.TryAdd(variable.Name, variable);
            }
        }

        if (declaration.TypeDefinition != null)
        {
            declaration.Type = VerifyType(declaration.TypeDefinition, scope, out _, out var isVarargs, out var isParams, initialArrayLength: declaration.ArrayValues?.Count);
            if (isVarargs || isParams)
            {
                ErrorReporter.Report("Variable type cannot be varargs or Params", declaration.TypeDefinition);
            }
            else if (declaration.Type == null)
            {
                ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(declaration.TypeDefinition)}' in declaration", declaration.TypeDefinition);
            }
            else if (declaration.Type.TypeKind == TypeKind.Void)
            {
                ErrorReporter.Report($"Variables '{string.Join(", ", declaration.Variables.Select(v => v.Name))}' cannot be assigned type 'void'", declaration.TypeDefinition);
            }
            else
            {
                if (declaration.Type.TypeKind == TypeKind.Array)
                {
                    var arrayStruct = (StructAst)declaration.Type;
                    declaration.ArrayElementType = arrayStruct.GenericTypes[0];
                }
                else if (declaration.Type.TypeKind == TypeKind.CArray)
                {
                    var arrayType = (ArrayType)declaration.Type;
                    declaration.ArrayElementType = arrayType.ElementType;
                }

                foreach (var variable in declaration.Variables)
                {
                    variable.Type = declaration.Type;
                }
            }
        }

        // 2. Verify the null values
        if (declaration.Value is NullAst nullAst)
        {
            // 2a. Verify null can be assigned
            if (declaration.TypeDefinition == null)
            {
                ErrorReporter.Report("Cannot assign null value without declaring a type", declaration.Value);
            }
            else
            {
                if (declaration.Type != null && declaration.Type.TypeKind != TypeKind.Pointer)
                {
                    ErrorReporter.Report("Cannot assign null to non-pointer type", declaration.Value);
                }

                nullAst.TargetType = declaration.Type;
            }
        }
        // 3. Verify declaration values
        else if (declaration.Value != null)
        {
            var valueType = VerifyExpression(declaration.Value, currentFunction, scope);

            if (valueType != null)
            {
                // Verify the assignment value matches the type definition if it has been defined
                if (declaration.TypeDefinition == null)
                {
                    if (valueType.TypeKind == TypeKind.Void)
                    {
                        ErrorReporter.Report($"Variables '{string.Join(", ", declaration.Variables.Select(v => v.Name))}' cannot be assigned type 'void'", declaration.Value);
                    }
                    else if (valueType.TypeKind == TypeKind.Compound)
                    {
                        var compoundType = (CompoundType)valueType;
                        var count = declaration.Variables.Length;

                        if (declaration.Value is CompoundExpressionAst)
                        {
                            if (count != compoundType.Types.Length)
                            {
                                ErrorReporter.Report($"Compound declaration expected to have {compoundType.Types.Length} variables", declaration);
                                if (count > compoundType.Types.Length)
                                {
                                    count = compoundType.Types.Length;
                                }
                            }
                        }
                        else
                        {
                            if (count > compoundType.Types.Length)
                            {
                                ErrorReporter.Report($"Compound declaration expected to have {compoundType.Types.Length} or fewer variables", declaration);
                                count = compoundType.Types.Length;
                            }
                        }

                        for (var i = 0; i < count; i++)
                        {
                            var type = compoundType.Types[i];
                            var variable = declaration.Variables[i];
                            if (type.TypeKind == TypeKind.Void)
                            {
                                ErrorReporter.Report($"Variable '{variable.Name}' cannot be assigned type 'void'", declaration.Value);
                            }
                            else
                            {
                                variable.Type = type;
                            }
                        }
                    }
                    else
                    {
                        foreach (var variable in declaration.Variables)
                        {
                            variable.Type = valueType;
                        }
                    }
                    declaration.Type = valueType;
                }
                else if (declaration.Type != null)
                {
                    if (!TypeEquals(declaration.Type, valueType))
                    {
                        ErrorReporter.Report($"Expected declaration value to be type '{declaration.Type.Name}', but got '{valueType.Name}'", declaration.Value);
                    }
                    else
                    {
                        VerifyConstantIfNecessary(declaration.Value, declaration.Type);
                    }
                }
            }
        }
        else if (declaration.Assignments != null)
        {
            ErrorReporter.Report("Compound declarations cannot have struct assignments", declaration);
        }
        else if (declaration.ArrayValues != null)
        {
            ErrorReporter.Report("Compound declarations cannot have array initializations", declaration);
        }

        // 4. Verify the type definition count if necessary
        if (declaration.Type?.TypeKind == TypeKind.Array && declaration.TypeDefinition?.Count != null)
        {
            var countType = VerifyExpression(declaration.TypeDefinition.Count, currentFunction, scope, out var isConstant, out uint arrayLength);

            if (countType != null)
            {
                if (countType.TypeKind != TypeKind.Integer)
                {
                    ErrorReporter.Report($"Expected count of variables '{string.Join(", ", declaration.Variables.Select(v => v.Name))}' to be a positive integer", declaration.TypeDefinition.Count);
                }
                if (isConstant)
                {
                    declaration.TypeDefinition.ConstCount = arrayLength;
                }
            }
        }
    }

    private static StructFieldRefAst GetOSVersion()
    {
        return new StructFieldRefAst
        {
            Children = {
                new IdentifierAst {Name = "OS"},
                new IdentifierAst
                {
                    Name = Environment.OSVersion.Platform switch
                    {
                        PlatformID.Unix => "Linux",
                        PlatformID.Win32NT => "Windows",
                        PlatformID.MacOSX => "Mac",
                        _ => "None"
                    }
                }
            }
        };
    }

    private static StructFieldRefAst GetBuildEnv()
    {
        return new StructFieldRefAst
        {
            Children = {
                new IdentifierAst {Name = "BuildEnv"},
                new IdentifierAst {Name = BuildSettings.Release ? "Release" : "Debug"}
            }
        };
    }

    private static void VerifyAssignment(AssignmentAst assignment, IFunction currentFunction, IScope scope)
    {
        if (assignment.Reference is CompoundExpressionAst compoundReference)
        {
            if (assignment.Assignments != null)
            {
                ErrorReporter.Report("Unable to assign fields for to a compound reference", assignment);
                return;
            }

            if (assignment.ArrayValues != null)
            {
                ErrorReporter.Report("Unable to assign array values to a compound reference", assignment);
                return;
            }

            var valueType = VerifyExpression(assignment.Value, currentFunction, scope);

            var referenceTypes = new IType[compoundReference.Children.Count];
            for (var i = 0; i < referenceTypes.Length; i++)
            {
                referenceTypes[i] = GetReference(compoundReference.Children[i], currentFunction, scope, out _);
            }

            if (assignment.Operator != Operator.None)
            {
                ErrorReporter.Report("Unable to perform operator assignment on a compound expression", assignment);
            }

            if (assignment.Value is NullAst)
            {
                for (var i = 0; i < referenceTypes.Length; i++)
                {
                    var referenceType = referenceTypes[i];
                    if (referenceType != null && referenceType.TypeKind != TypeKind.Pointer)
                    {
                        ErrorReporter.Report("Cannot assign null to non-pointer type", compoundReference.Children[i]);
                    }
                }
            }
            else if (valueType != null)
            {
                if (valueType.TypeKind == TypeKind.Compound)
                {
                    var compoundType = (CompoundType)valueType;
                    var count = referenceTypes.Length;

                    if (assignment.Value is CompoundExpressionAst)
                    {
                        if (count != compoundType.Types.Length)
                        {
                            ErrorReporter.Report($"Compound assignment expected to have {compoundType.Types.Length} references", compoundReference);
                            if (count > compoundType.Types.Length)
                            {
                                count = compoundType.Types.Length;
                            }
                        }
                    }
                    else
                    {
                        if (count > compoundType.Types.Length)
                        {
                            ErrorReporter.Report($"Compound assignment expected to have {compoundType.Types.Length} or fewer references", compoundReference);
                            count = compoundType.Types.Length;
                        }
                    }

                    for (var i = 0; i < count; i++)
                    {
                        var type = compoundType.Types[i];
                        var referenceType = referenceTypes[i];
                        if (referenceType != null)
                        {
                            if (!TypeEquals(referenceType, type, true))
                            {
                                ErrorReporter.Report($"Expected assignment value to be type '{referenceType.Name}', but got '{type.Name}'", compoundReference.Children[i]);
                            }
                        }
                    }
                }
                else
                {
                    foreach (var referenceType in referenceTypes)
                    {
                        if (referenceType != null)
                        {
                            if (!TypeEquals(referenceType, valueType, true))
                            {
                                ErrorReporter.Report($"Expected assignment value to be type '{referenceType.Name}', but got '{valueType.Name}'", assignment.Value);
                            }
                            else
                            {
                                VerifyConstantIfNecessary(assignment.Value, referenceType);
                            }
                        }
                    }
                }
            }

            return;
        }

        // 1. Verify the variable is already defined, that it is not a constant, and the r-value
        var variableType = GetReference(assignment.Reference, currentFunction, scope, out _);

        if (variableType == null) return;

        VerifyAssignmentValue(assignment, variableType, currentFunction, scope);
    }

    private static void VerifyAssignmentValue(AssignmentAst assignment, IType type, IFunction currentFunction, IScope scope, bool disallowOperators = false, bool global = false, bool structField = false)
    {
        if (assignment.Value != null)
        {
            if (assignment.Value is NullAst nullAst)
            {
                if (assignment.Operator != Operator.None)
                {
                    ErrorReporter.Report("Cannot assign null value with operator assignment", assignment.Value);
                }
                if (type.TypeKind != TypeKind.Pointer && type.TypeKind != TypeKind.Interface)
                {
                    ErrorReporter.Report($"Cannot assign null to non-pointer type '{type.Name}'", assignment.Value);
                }
                nullAst.TargetType = type;
                return;
            }

            var valueType = VerifyExpression(assignment.Value, currentFunction, scope, out var isConstant);

            if (valueType != null)
            {
                // Verify the operator is valid
                if (assignment.Operator != Operator.None)
                {
                    if (disallowOperators)
                    {
                        ErrorReporter.Report("Cannot have operator assignments in object initializers", assignment);
                        return;
                    }

                    var lhs = type.TypeKind;
                    var rhs = valueType.TypeKind;
                    if ((lhs == TypeKind.Struct && rhs == TypeKind.Struct) ||
                        (lhs == TypeKind.String && rhs == TypeKind.String))
                    {
                        if (TypeEquals(type, valueType, true))
                        {
                            var overload = VerifyOperatorOverloadType((StructAst)type, assignment.Operator, currentFunction, assignment.Value, scope);
                            if (overload != null)
                            {
                                if (overload.ReturnType != type)
                                {
                                    ErrorReporter.Report($"Overload for operator {PrintOperator(assignment.Operator)} of type '{type.Name}' returned '{overload.ReturnType}', when assignment expected the same type", assignment.Value);
                                    return;
                                }
                                assignment.OperatorOverload = overload;
                            }
                        }
                        else
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(assignment.Operator)} not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                        }
                    }
                    else
                    {
                        switch (assignment.Operator)
                        {
                            // Both need to be bool and returns bool
                            case Operator.And:
                            case Operator.Or:
                                if (lhs != TypeKind.Boolean || rhs != TypeKind.Boolean)
                                {
                                    ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                                }
                                break;
                                // Invalid assignment operators
                            case Operator.Equality:
                            case Operator.GreaterThan:
                            case Operator.LessThan:
                            case Operator.GreaterThanEqual:
                            case Operator.LessThanEqual:
                                ErrorReporter.Report($"Invalid operator '{PrintOperator(assignment.Operator)}' in assignment", assignment);
                                break;
                                // Requires same types and returns more precise type
                            case Operator.Add:
                            case Operator.Subtract:
                            case Operator.Multiply:
                            case Operator.Divide:
                                if (!(lhs == TypeKind.Integer && rhs == TypeKind.Integer) &&
                                    !(lhs == TypeKind.Float && (rhs == TypeKind.Float || rhs == TypeKind.Integer)))
                                {
                                    ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                                }
                                break;
                            case Operator.Modulus:
                                if (lhs != TypeKind.Integer || rhs != TypeKind.Integer)
                                {
                                    ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                                }
                                break;
                                // Requires both integer or bool types and returns more same type
                            case Operator.BitwiseAnd:
                            case Operator.BitwiseOr:
                            case Operator.Xor:
                                if (lhs == TypeKind.Enum && rhs == TypeKind.Enum)
                                {
                                    if (type != valueType)
                                    {
                                        ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                                    }
                                }
                                else if (!(lhs == TypeKind.Boolean && rhs == TypeKind.Boolean) && !(lhs == TypeKind.Integer && rhs == TypeKind.Integer))
                                {
                                    ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                                }
                                break;
                                // Requires both to be integers
                            case Operator.ShiftLeft:
                            case Operator.ShiftRight:
                            case Operator.RotateLeft:
                            case Operator.RotateRight:
                                if (lhs != TypeKind.Integer || rhs != TypeKind.Integer)
                                {
                                    ErrorReporter.Report($"Operator '{PrintOperator(assignment.Operator)}' not applicable to types '{type.Name}' and '{valueType.Name}'", assignment.Value);
                                }
                                else if (assignment.Value is ConstantAst constantAst)
                                {
                                    constantAst.Type = type;
                                }
                                break;
                        }
                    }
                }
                else if (!TypeEquals(type, valueType))
                {
                    ErrorReporter.Report($"Expected assignment value to be type '{type.Name}', but got '{valueType.Name}'", assignment.Value);
                }
                else if (isConstant)
                {
                    VerifyConstantIfNecessary(assignment.Value, type);
                }
                else if (global)
                {
                    ErrorReporter.Report($"Global variables can only be initialized with constant values", assignment.Value);
                }
                else if (structField)
                {
                    ErrorReporter.Report("Default values in structs should be constant", assignment.Value);
                }
            }
        }
        else if (assignment.Assignments != null)
        {
            if (type.TypeKind != TypeKind.Struct && type.TypeKind != TypeKind.String)
            {
                ErrorReporter.Report("Can only use field assignments with struct type", assignment);
                return;
            }

            var structDef = type as StructAst;
            foreach (var (name, assignmentValue) in assignment.Assignments)
            {
                VerifyFieldAssignment(structDef, name, assignmentValue, currentFunction, scope);
            }
        }
        else if (assignment.ArrayValues != null)
        {
            if (type.TypeKind == TypeKind.Array)
            {
                var arrayStruct = (StructAst)type;
                VerifyArrayValues(arrayStruct.GenericTypes[0], assignment, currentFunction, scope, global, structField);
            }
            else if (type.TypeKind == TypeKind.CArray)
            {
                var arrayType = (ArrayType)type;
                if (assignment.ArrayValues.Count > arrayType.Length)
                {
                    ErrorReporter.Report($"Expected {arrayType.Length} or fewer array values, but got {assignment.ArrayValues.Count}", assignment);
                }

                VerifyArrayValues(arrayType.ElementType, assignment, currentFunction, scope, global, structField);
            }
            else
            {
                ErrorReporter.Report("Can only use field array assignment with Array types", assignment);
            }
        }
    }

    private static void VerifyFieldAssignment(StructAst structDef, string name, AssignmentAst assignment, IFunction currentFunction, IScope scope, bool global = false, bool structField = false)
    {
        foreach (var field in structDef.Fields)
        {
            if (name == field.Name)
            {
                if (field.Type != null)
                {
                    VerifyAssignmentValue(assignment, field.Type, currentFunction, scope, true, global, structField);
                }
                return;
            }
        }
        ErrorReporter.Report($"Field '{name}' not present in struct '{structDef.Name}'", assignment.Reference);
    }

    private static void VerifyArrayValues(IType elementType, AssignmentAst assignment, IFunction currentFunction, IScope scope, bool global, bool structField)
    {
        foreach (var value in assignment.ArrayValues)
        {
            var valueType = VerifyExpression(value, currentFunction, scope, out var isConstant);
            if (valueType != null)
            {
                if (!TypeEquals(elementType, valueType))
                {
                    ErrorReporter.Report($"Expected array value to be type '{elementType.Name}', but got '{valueType.Name}'", value);
                }
                else if (isConstant)
                {
                    VerifyConstantIfNecessary(value, elementType);
                }
                else if (global)
                {
                    ErrorReporter.Report($"Global variables can only be initialized with constant values", value);
                }
                else if (structField)
                {
                    ErrorReporter.Report("Default values in structs array initializers should be constant", value);
                }
            }
        }
    }

    private static IType GetReference(IAst ast, IFunction currentFunction, IScope scope, out bool hasPointer, bool fromUnaryReference = false)
    {
        hasPointer = true;
        switch (ast)
        {
            case IdentifierAst identifier:
            {
                var variableType = GetVariable(identifier.Name, identifier, scope, out var constant);
                if (constant)
                {
                    ErrorReporter.Report($"Cannot reassign value of constant variable '{identifier.Name}'", identifier);
                }
                return variableType;
            }
            case IndexAst index:
            {
                var variableType = GetVariable(index.Name, index, scope, out var constant);
                if (variableType == null) return null;
                if (constant)
                {
                    ErrorReporter.Report($"Cannot reassign value of constant variable '{index.Name}'", index);
                }
                var type = VerifyIndex(index, variableType, currentFunction, scope, out var overloaded);
                if (type != null && overloaded)
                {
                    if (type.TypeKind != TypeKind.Pointer)
                    {
                        ErrorReporter.Report($"Overload [] for type '{variableType.Name}' must be a pointer to be able to set the value", index);
                        return null;
                    }
                    hasPointer = false;
                    var pointerType = (PointerType)type;
                    return pointerType.PointedType;
                }
                return type;
            }
            case StructFieldRefAst structFieldRef:
            {
                var childCount = structFieldRef.Children.Count - 1;
                structFieldRef.Pointers = new bool[childCount];
                structFieldRef.Types = new IType[childCount];
                structFieldRef.ValueIndices = new int[childCount];

                IType refType;
                switch (structFieldRef.Children[0])
                {
                    case IdentifierAst identifier:
                    {
                        refType = GetVariable(identifier.Name, identifier, scope, out var constant, true);
                        if (refType == null) return null;
                        if (constant)
                        {
                            ErrorReporter.Report($"Cannot reassign value of constant variable '{identifier.Name}'", identifier);
                            return null;
                        }
                        break;
                    }
                    case IndexAst index:
                    {
                        var variableType = GetVariable(index.Name, index, scope, out var constant);
                        if (variableType == null) return null;
                        if (constant)
                        {
                            ErrorReporter.Report($"Cannot reassign value of constant variable '{index.Name}'", index);
                            return null;
                        }
                        refType = VerifyIndex(index, variableType, currentFunction, scope, out var overloaded);
                        if (refType != null && overloaded && refType.TypeKind != TypeKind.Pointer)
                        {
                            ErrorReporter.Report($"Overload [] for type '{variableType.Name}' must be a pointer to be able to set the value", index);
                            return null;
                        }
                        break;
                    }
                    default:
                        ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", structFieldRef.Children[0]);
                        return null;
                }
                if (refType == null)
                {
                    return null;
                }

                for (var i = 1; i < structFieldRef.Children.Count; i++)
                {
                    switch (structFieldRef.Children[i])
                    {
                        case IdentifierAst identifier:
                            refType = VerifyStructField(identifier.Name, refType, structFieldRef, i-1, identifier);
                            if (structFieldRef.IsConstant)
                            {
                                ErrorReporter.Report($"Cannot reassign value of constant field '{identifier.Name}'", identifier);
                                return null;
                            }
                            break;
                        case IndexAst index:
                            var fieldType = VerifyStructField(index.Name, refType, structFieldRef, i-1, index);
                            if (fieldType == null) return null;
                            refType = VerifyIndex(index, fieldType, currentFunction, scope, out var overloaded);
                            if (refType != null && overloaded)
                            {
                                if (refType.TypeKind == TypeKind.Pointer)
                                {
                                    if (i == structFieldRef.Children.Count - 1)
                                    {
                                        hasPointer = false;
                                        var pointerType = (PointerType)refType;
                                        refType = pointerType.PointedType;
                                    }
                                }
                                else
                                {
                                    ErrorReporter.Report($"Overload [] for type '{fieldType.Name}' must be a pointer to be able to set the value", index);
                                    return null;
                                }
                            }
                            break;
                        default:
                            ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", structFieldRef.Children[i]);
                            return null;
                    }
                    if (refType == null)
                    {
                        return null;
                    }
                }

                return refType;
            }
            case UnaryAst { Operator: UnaryOperator.Dereference } unary:
            {
                if (fromUnaryReference)
                {
                    ErrorReporter.Report("Operators '*' and '&' cancel each other out", unary);
                    return null;
                }
                var reference = GetReference(unary.Value, currentFunction, scope, out var canDereference);
                if (reference == null)
                {
                    return null;
                }
                if (!canDereference)
                {
                    ErrorReporter.Report("Cannot dereference pointer to assign value", unary.Value);
                    return null;
                }

                if (reference.TypeKind != TypeKind.Pointer)
                {
                    ErrorReporter.Report("Expected to get pointer to dereference", unary.Value);
                    return null;
                }
                var pointerType = (PointerType)reference;
                return unary.Type = pointerType.PointedType;
            }
            default:
                ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", ast);
                return null;
        }
    }

    private static IType GetVariable(string name, IAst ast, IScope scope, out bool constant, bool allowEnums = false)
    {
        constant = false;
        if (!GetScopeIdentifier(scope, name, out var identifier))
        {
            ErrorReporter.Report($"Variable '{name}' not defined", ast);
            return null;
        }
        switch (identifier)
        {
            case EnumAst enumAst when allowEnums:
                constant = true;
                return enumAst;
            case DeclarationAst declaration:
                if (declaration.Global && !declaration.Verified)
                {
                    VerifyGlobalVariable(declaration);
                }
                constant = declaration.Constant;
                return declaration.Type;
            case VariableAst variable:
                return variable.Type;
            default:
                ErrorReporter.Report($"Identifier '{name}' is not a variable", ast);
                return null;
        }
    }

    private static IType VerifyStructFieldRef(StructFieldRefAst structField, IFunction currentFunction, IScope scope, out bool isConstant)
    {
        IType refType;
        isConstant = false;
        switch (structField.Children[0])
        {
            case IdentifierAst identifier:
                if (!GetScopeIdentifier(scope, identifier.Name, out var value, out var global))
                {
                    ErrorReporter.Report($"Identifier '{identifier.Name}' not defined", structField);
                    return null;
                }
                switch (value)
                {
                    case EnumAst enumAst:
                        return VerifyEnumValue(enumAst, structField, out isConstant);
                    case DeclarationAst declaration:
                        if (declaration.Global && !declaration.Verified)
                        {
                            VerifyGlobalVariable(declaration);
                        }
                        refType = declaration.Type;
                        if (declaration.Constant && refType?.TypeKind == TypeKind.String)
                        {
                            structField.GlobalConstant = global;
                            structField.ConstantIndex = declaration.ConstantIndex;
                            return VerifyConstantStringField(structField, out isConstant);
                        }
                        break;
                    case VariableAst variable:
                        refType = variable.Type;
                        break;
                    default:
                        ErrorReporter.Report($"Cannot reference field of type '{identifier.Name}'", structField);
                        return null;
                }
                break;
            case ConstantAst constant:
                if (constant.String == null)
                {
                    isConstant = true;
                    refType = constant.Type;
                }
                else
                {
                    constant.Type ??= TypeTable.StringType;
                    structField.String = constant.String;
                    return VerifyConstantStringField(structField, out isConstant);
                }
                break;
            default:
                refType = VerifyExpression(structField.Children[0], currentFunction, scope);
                break;
        }
        if (refType == null)
        {
            return null;
        }
        var childCount = structField.Children.Count - 1;
        structField.Pointers = new bool[childCount];
        structField.Types = new IType[childCount];
        structField.ValueIndices = new int[childCount];

        for (var i = 1; i < structField.Children.Count; i++)
        {
            switch (structField.Children[i])
            {
                case IdentifierAst identifier:
                    refType = VerifyStructField(identifier.Name, refType, structField, i-1, identifier);
                    break;
                case IndexAst index:
                    var fieldType = VerifyStructField(index.Name, refType, structField, i-1, index);
                    if (fieldType == null) return null;
                    refType = VerifyIndex(index, fieldType, currentFunction, scope, out _);
                    break;
                case CallAst call:
                    var callType = VerifyStructField(call.Name, refType, structField, i-1, call, true);
                    if (callType == null) return null;
                    refType = VerifyCall(call, currentFunction, scope, (InterfaceAst)callType);
                    break;
                default:
                    ErrorReporter.Report("Expected to have a reference to a variable, field, or pointer", structField.Children[i]);
                    return null;
            }
            if (refType == null)
            {
                return null;
            }
        }
        return refType;
    }

    private static IType VerifyConstantStringField(StructFieldRefAst structField, out bool isConstant)
    {
        isConstant = false;
        if (structField.Children.Count > 2)
        {
            ErrorReporter.Report("Type 'string' does not contain field", structField);
            return null;
        }

        switch (structField.Children[1])
        {
            case IdentifierAst identifier:
                if (identifier.Name == "length")
                {
                    isConstant = true;
                    structField.ConstantStringLength = true;
                    return TypeTable.S64Type;
                }
                if (identifier.Name == "data")
                {
                    isConstant = true;
                    structField.RawConstantString = true;
                    return TypeTable.RawStringType;
                }
                ErrorReporter.Report($"Type 'string' does not contain field '{identifier.Name}'", identifier);
                break;
        }
        return null;
    }

    private static IType VerifyStructField(string fieldName, IType structType, StructFieldRefAst structField, int fieldIndex, IAst ast, bool call = false)
    {
        if (structType.TypeKind == TypeKind.Pointer)
        {
            var pointerType = (PointerType)structType;
            structType = pointerType.PointedType;
            structField.Pointers[fieldIndex] = true;
        }

        if (structType is ArrayType arrayType && fieldName == "length")
        {
            structField.IsConstant = true;
            structField.ConstantValue = arrayType.Length;
            return TypeTable.S64Type;
        }

        structField.Types[fieldIndex] = structType;
        if (structType is StructAst structDefinition)
        {
            StructFieldAst field = null;
            for (var i = 0; i < structDefinition.Fields.Count; i++)
            {
                if (structDefinition.Fields[i].Name == fieldName)
                {
                    structField.ValueIndices[fieldIndex] = i;
                    field = structDefinition.Fields[i];
                    break;
                }
            }
            if (field == null)
            {
                ErrorReporter.Report($"Struct '{structType.Name}' does not contain field '{fieldName}'", ast);
                return null;
            }
            if (call && field.Type is not InterfaceAst)
            {
                ErrorReporter.Report($"Expected field to be an interface, but get '{field.Type.Name}'", ast);
                return null;
            }

            return field.Type;
        }
        if (structType is UnionAst union)
        {
            UnionFieldAst field = null;
            for (var i = 0; i < union.Fields.Count; i++)
            {
                if (union.Fields[i].Name == fieldName)
                {
                    structField.ValueIndices[fieldIndex] = i;
                    field = union.Fields[i];
                    break;
                }
            }
            if (field == null)
            {
                ErrorReporter.Report($"Struct '{structType.Name}' does not contain field '{fieldName}'", ast);
                return null;
            }

            return field.Type;
        }

        ErrorReporter.Report($"Type '{structType.Name}' does not contain field '{fieldName}'", ast);
        return null;
    }

    private static bool VerifyCondition(IAst ast, IFunction currentFunction, IScope scope, bool willRun = false)
    {
        var conditionalType = VerifyExpression(ast, currentFunction, scope, out var constant);

        if (willRun && !constant)
        {
            ClearAstQueue();
        }

        switch (conditionalType?.TypeKind)
        {
            case TypeKind.Boolean:
            case TypeKind.Integer:
            case TypeKind.Float:
            case TypeKind.Pointer:
            case TypeKind.Enum:
                // Valid types
                return constant || !ErrorReporter.Errors.Any();
            case null:
                return false;
            default:
                ErrorReporter.Report($"Expected condition to be bool, int, float, or pointer, but got '{conditionalType.Name}'", ast);
                return false;
        }
    }

    private static void VerifyEach(EachAst each, IFunction currentFunction, IScope scope)
    {
        if (GetScopeIdentifier(scope, each.IterationVariable.Name, out _))
        {
            ErrorReporter.Report($"Iteration variable '{each.IterationVariable.Name}' already exists in scope", each);
        };
        if (each.Iteration != null)
        {
            var iterationType = VerifyExpression(each.Iteration, currentFunction, scope);
            if (iterationType == null) return;

            if (each.IndexVariable != null)
            {
                each.IndexVariable.Type = TypeTable.S64Type;
                each.Body.Identifiers.TryAdd(each.IndexVariable.Name, each.IndexVariable);
            }

            switch (iterationType.TypeKind)
            {
                case TypeKind.CArray:
                    // Same logic as below with special case for constant length
                    var arrayType = (ArrayType)iterationType;
                    each.IterationVariable.Type = arrayType.ElementType;
                    each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
                    break;
                case TypeKind.Array:
                    var arrayStruct = (StructAst)iterationType;
                    each.IterationVariable.Type = arrayStruct.GenericTypes[0];
                    each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
                    break;
                default:
                    ErrorReporter.Report($"Type {iterationType.Name} cannot be used as an iterator", each.Iteration);
                    break;
            }
        }
        else
        {
            var beginType = VerifyExpression(each.RangeBegin, currentFunction, scope);
            if (beginType != null && beginType.TypeKind != TypeKind.Integer)
            {
                ErrorReporter.Report($"Expected range to begin with 'int', but got '{beginType.Name}'", each.RangeBegin);
            }

            var endType = VerifyExpression(each.RangeEnd, currentFunction, scope);
            if (endType != null && endType.TypeKind != TypeKind.Integer)
            {
                ErrorReporter.Report($"Expected range to end with 'int', but got '{endType.Name}'", each.RangeEnd);
            }

            each.IterationVariable.Type = TypeTable.S32Type;
            each.Body.Identifiers.TryAdd(each.IterationVariable.Name, each.IterationVariable);
        }
    }

    private static void VerifyInlineAssembly(AssemblyAst assembly, IScope scope)
    {
        // Verify the instructions for capturing values
        foreach (var (register, input) in assembly.InRegisters)
        {
            if (Assembly.Registers.TryGetValue(register, out var registerDefinition))
            {
                input.RegisterDefinition = registerDefinition;
            }
            else
            {
                ErrorReporter.Report($"Expected a target register, but got '{register}'", input);
            }

            var inputType = VerifyExpression(input.Ast, null, scope);

            if (registerDefinition != null && inputType != null)
            {
                if (registerDefinition.Type != RegisterType.General)
                {
                    if (inputType.TypeKind == TypeKind.Float)
                    {
                        assembly.FindStagingInputRegister = true;
                    }
                    else
                    {
                        ErrorReporter.Report($"Unable to assign type '{inputType.Name}' to register '{register}'", input);
                    }
                }
                else if (inputType is StructAst || inputType is UnionAst)
                {
                    input.GetPointer = true;
                }
            }
        }

        // Verify the instructions in the body of the assembly block
        foreach (var instruction in assembly.Instructions)
        {
            if (!Assembly.Instructions.TryGetValue(instruction.Instruction, out var definitions))
            {
                ErrorReporter.Report($"Unknown instruction '{instruction.Instruction}'", instruction);
            }

            var valid = true;
            if (instruction.Value1?.Register != null)
            {
                if (Assembly.Registers.TryGetValue(instruction.Value1.Register, out var register))
                {
                    instruction.Value1.RegisterDefinition = register;
                }
                else
                {
                    valid = false;
                    ErrorReporter.Report($"Unknown register '{instruction.Value1.Register}'", instruction.Value1);
                }
            }

            if (instruction.Value2?.Register != null)
            {
                if (Assembly.Registers.TryGetValue(instruction.Value2.Register, out var register))
                {
                    instruction.Value2.RegisterDefinition = register;
                }
                else
                {
                    valid = false;
                    ErrorReporter.Report($"Unknown register '{instruction.Value2.Register}'", instruction.Value2);
                }
            }

            if (valid && definitions != null)
            {
                foreach (var definition in definitions)
                {
                    if (definition.Value1 == null)
                    {
                        if (instruction.Value1 == null)
                        {
                            instruction.Definition = definition;
                            break;
                        }
                        continue;
                    }

                    if (!AssemblyArgumentMatches(instruction.Value1, definition.Value1))
                    {
                        continue;
                    }

                    if (definition.Value2 == null)
                    {
                        if (instruction.Value2 == null)
                        {
                            instruction.Definition = definition;
                            break;
                        }
                        continue;
                    }

                    if (AssemblyArgumentMatches(instruction.Value2, definition.Value2))
                    {
                        instruction.Definition = definition;
                        break;
                    }
                }

                if (instruction.Definition == null)
                {
                    ErrorReporter.Report($"No instruction found for '{instruction.Instruction}' with given arguments", instruction);
                }
            }
        }

        // Verify the out instructions for getting values from the registers
        foreach (var output in assembly.OutValues)
        {
            var outputType = GetReference(output.Ast, null, scope, out var hasPointer);
            if (!hasPointer)
            {
                ErrorReporter.Report("Cannot dereference pointer to assign value", output.Ast);
            }

            if (Assembly.Registers.TryGetValue(output.Register, out var registerDefinition))
            {
                output.RegisterDefinition = registerDefinition;

                if (outputType?.TypeKind != TypeKind.Float && registerDefinition.Type != RegisterType.General)
                {
                    ErrorReporter.Report($"Unable to assign from register '{output.Register}' to type '{outputType.Name}'", output);
                }
            }
            else
            {
                ErrorReporter.Report($"Expected a source register, but got '{output.Register}'", output);
            }
        }
    }

    private static bool AssemblyArgumentMatches(AssemblyValueAst value, InstructionArgument arg)
    {
        if (arg.Constant)
        {
            return value.Constant != null;
        }

        if (value.Constant != null)
        {
            return false;
        }

        return value.Dereference == arg.Memory && value.RegisterDefinition.Type == arg.Type && value.RegisterDefinition.Size == arg.RegisterSize;
    }

    private static void VerifySwitch(SwitchAst switchAst, IFunction currentFunction, IScope scope, bool inDefer, bool canBreak)
    {
        var valueType = VerifyExpression(switchAst.Value, currentFunction, scope);
        if (valueType != null)
        {
            switch (valueType.TypeKind)
            {
                case TypeKind.Boolean:
                case TypeKind.Integer:
                case TypeKind.Float:
                case TypeKind.Enum:
                    // Valid types
                    break;
                default:
                    ErrorReporter.Report($"Expected switch value to be bool, int, float, or enum but got '{valueType.Name}'", switchAst.Value);
                    break;
            }
        }

        foreach (var (cases, body) in switchAst.Cases)
        {
            foreach (var switchCase in cases)
            {
                var caseType = VerifyExpression(switchCase, currentFunction, scope, out var constant);

                if (caseType != null && valueType != null)
                {
                    if (!TypeEquals(valueType, caseType))
                    {
                        ErrorReporter.Report($"Expected case value to be type '{valueType.Name}', but got '{caseType.Name}'", switchCase);
                    }
                    else if (!constant)
                    {
                        ErrorReporter.Report("Expected switch case to be a constant", switchCase);
                    }
                    else
                    {
                        VerifyConstantIfNecessary(switchCase, valueType);
                    }
                }
            }

            body.Parent = scope;
            VerifyScope(body, currentFunction, inDefer, canBreak);
        }

        if (switchAst.DefaultCase != null)
        {
            switchAst.DefaultCase.Parent = scope;
            VerifyScope(switchAst.DefaultCase, currentFunction, inDefer, canBreak);
        }
    }

    private static void VerifyFunctionIfNecessary(FunctionAst function, IFunction currentFunction)
    {
        if (function.Flags.HasFlag(FunctionFlags.Extern))
        {
            if (!function.Flags.HasFlag(FunctionFlags.Verified))
            {
                VerifyFunctionDefinition(function);
            }
        }
        else if (!function.Flags.HasFlag(FunctionFlags.Verified) && function != currentFunction && !function.Flags.HasFlag(FunctionFlags.Queued))
        {
            if (!function.Flags.HasFlag(FunctionFlags.DefinitionVerified))
            {
                VerifyFunctionDefinition(function);
            }
            function.Flags |= FunctionFlags.Queued;
            _astCompleteQueue.Enqueue(function);
        }
    }

    private static IType VerifyExpression(IAst ast, IFunction currentFunction, IScope scope)
    {
        return VerifyExpression(ast, currentFunction, scope, out _, out _, false, out _);
    }

    private static IType VerifyExpression(IAst ast, IFunction currentFunction, IScope scope, out bool isConstant)
    {
        return VerifyExpression(ast, currentFunction, scope, out isConstant, out _, false, out _);
    }

    private static IType VerifyExpression(IAst ast, IFunction currentFunction, IScope scope, out bool isConstant, out uint arrayLength)
    {
        return VerifyExpression(ast, currentFunction, scope, out isConstant, out arrayLength, true, out _);
    }

    private static IType VerifyExpression(IAst ast, IFunction currentFunction, IScope scope, out bool isConstant, out bool isType)
    {
        return VerifyExpression(ast, currentFunction, scope, out isConstant, out _, false, out isType);
    }

    private static IType VerifyExpression(IAst ast, IFunction currentFunction, IScope scope, out bool isConstant, out uint arrayLength, bool getArrayLength, out bool isType)
    {
        // 1. Verify the expression value
        isConstant = false;
        arrayLength = 0;
        isType = false;
        switch (ast)
        {
            case ConstantAst constant:
                isConstant = true;
                if (getArrayLength && constant.Type?.TypeKind == TypeKind.Integer)
                {
                    arrayLength = (uint)constant.Value.UnsignedInteger;
                }
                else if (constant.Type == null && constant.String != null)
                {
                    constant.Type = TypeTable.StringType;
                }
                return constant.Type;
            case NullAst:
                isConstant = true;
                return null;
            case StructFieldRefAst structField:
                return VerifyStructFieldRef(structField, currentFunction, scope, out isConstant);
            case IdentifierAst identifierAst:
                if (identifierAst.BakedType != null)
                {
                    isType = true;
                    return identifierAst.BakedType;
                }
                if (!GetScopeIdentifier(scope, identifierAst.Name, out var identifier))
                {
                    if (GetExistingFunction(identifierAst.Name, identifierAst.FileIndex, out var function, out var functionCount))
                    {
                        if (functionCount > 1)
                        {
                            ErrorReporter.Report($"Cannot determine type for function '{identifierAst.Name}' that has multiple overloads", identifierAst);
                            return null;
                        }
                        VerifyFunctionIfNecessary(function, currentFunction);
                        identifierAst.FunctionTypeIndex = function.TypeIndex;
                        isConstant = true;
                        return function;
                    }
                    ErrorReporter.Report($"Identifier '{identifierAst.Name}' not defined", identifierAst);
                }
                switch (identifier)
                {
                    case DeclarationAst declaration:
                        if (declaration.Global && !declaration.Verified)
                        {
                            VerifyGlobalVariable(declaration);
                        }
                        isConstant = declaration.Constant;
                        if (getArrayLength && isConstant && declaration.Type.TypeKind == TypeKind.Integer)
                        {
                            if (declaration.Value is ConstantAst constValue)
                            {
                                arrayLength = (uint)constValue.Value.UnsignedInteger;
                            }
                        }
                        return declaration.Type;
                    case VariableAst variable:
                        return variable.Type;
                    case StructAst structAst:
                        if (structAst.Generics != null)
                        {
                            ErrorReporter.Report($"Cannot reference polymorphic type '{structAst.Name}' without specifying generics", identifierAst);
                        }
                        else if (!structAst.Verified)
                        {
                            VerifyStruct(structAst);
                        }
                        isConstant = true;
                        isType = true;
                        identifierAst.TypeIndex = structAst.TypeIndex;
                        return structAst;
                    case UnionAst union:
                        if (!union.Verified)
                        {
                            VerifyUnion(union);
                        }
                        isConstant = true;
                        isType = true;
                        identifierAst.TypeIndex = union.TypeIndex;
                        return union;
                    case InterfaceAst interfaceAst:
                        if (!interfaceAst.Verified)
                        {
                            VerifyInterface(interfaceAst);
                        }
                        isConstant = true;
                        isType = true;
                        identifierAst.TypeIndex = interfaceAst.TypeIndex;
                        return interfaceAst;
                    case IType type:
                        isConstant = true;
                        isType = true;
                        identifierAst.TypeIndex = type.TypeIndex;
                        return type;
                    default:
                        return null;
                }
            case ChangeByOneAst changeByOne:
                var op = changeByOne.Positive ? "increment" : "decrement";
                switch (changeByOne.Value)
                {
                    case IdentifierAst:
                    case StructFieldRefAst:
                    case IndexAst:
                        var expressionType = GetReference(changeByOne.Value, currentFunction, scope, out _);
                        if (expressionType != null)
                        {
                            if (expressionType.TypeKind != TypeKind.Integer && expressionType.TypeKind != TypeKind.Float)
                            {
                                ErrorReporter.Report($"Expected to {op} int or float, but got type '{expressionType.Name}'", changeByOne.Value);
                                return null;
                            }
                            changeByOne.Type = expressionType;
                        }

                        return expressionType;
                    default:
                        ErrorReporter.Report($"Expected to {op} variable", changeByOne);
                        return null;
                }
            case UnaryAst unary:
            {
                if (unary.Operator == UnaryOperator.Reference)
                {
                    var referenceType = GetReference(unary.Value, currentFunction, scope, out var hasPointer, true);
                    if (!hasPointer)
                    {
                        ErrorReporter.Report("Unable to get reference of unary value", unary.Value);
                        return null;
                    }

                    if (referenceType == null)
                    {
                        return null;
                    }

                    if (referenceType.TypeKind == TypeKind.CArray)
                    {
                        var arrayType = (ArrayType)referenceType;
                        var name = $"{arrayType.ElementType.Name}*";
                        if (!GetType(name, arrayType.ElementType.FileIndex, out var pointerType))
                        {
                            pointerType = CreatePointerType(name, arrayType.ElementType);
                        }
                        unary.Type = pointerType;
                    }
                    else
                    {
                        var name = $"{referenceType.Name}*";
                        if (!GetType(name, referenceType.FileIndex, out var pointerType))
                        {
                            pointerType = CreatePointerType(name, referenceType);
                        }
                        unary.Type = pointerType;
                    }
                    return unary.Type;
                }

                var valueType = VerifyExpression(unary.Value, currentFunction, scope);
                if (valueType == null)
                {
                    return null;
                }
                switch (unary.Operator)
                {
                    case UnaryOperator.Not:
                        if (valueType.TypeKind == TypeKind.Boolean)
                        {
                            return valueType;
                        }

                        ErrorReporter.Report($"Expected type 'bool', but got type '{valueType.Name}'", unary.Value);
                        return null;
                    case UnaryOperator.Negate:
                        if (valueType.TypeKind == TypeKind.Integer || valueType.TypeKind == TypeKind.Float)
                        {
                            return valueType;
                        }

                        ErrorReporter.Report($"Negation not compatible with type '{valueType.Name}'", unary.Value);
                        return null;
                    case UnaryOperator.Dereference:
                        if (valueType.TypeKind == TypeKind.Pointer)
                        {
                            var pointerType = (PointerType)valueType;
                            return unary.Type = pointerType.PointedType;
                        }

                        ErrorReporter.Report($"Cannot dereference type '{valueType.Name}'", unary.Value);
                        return null;
                    default:
                        ErrorReporter.Report($"Unexpected unary operator '{unary.Operator}'", unary.Value);
                        return null;
                }
            }
            case CallAst call:
                return VerifyCall(call, currentFunction, scope);
            case ExpressionAst expression:
                return VerifyExpressionType(expression, currentFunction, scope, out isConstant);
            case IndexAst index:
                var indexType = GetVariable(index.Name, index, scope, out _);
                return VerifyIndex(index, indexType, currentFunction, scope, out _);
            case TypeDefinition typeDef:
            {
                var type = VerifyType(typeDef, scope);
                if (type == null)
                {
                    return null;
                }
                isConstant = true;
                isType = true;
                typeDef.TypeIndex = type.TypeIndex;
                return type;
            }
            case CastAst cast:
            {
                cast.TargetType = VerifyType(cast.TargetTypeDefinition, scope);
                var valueType = VerifyExpression(cast.Value, currentFunction, scope);
                switch (cast.TargetType?.TypeKind)
                {
                    case TypeKind.Integer:
                    case TypeKind.Float:
                        switch (valueType?.TypeKind)
                        {
                            case TypeKind.Integer:
                            case TypeKind.Float:
                            case TypeKind.Enum:
                            case null:
                                break;
                            case TypeKind.Pointer when cast.TargetType.TypeKind == TypeKind.Integer && cast.TargetType.Size == 8:
                                break;
                            default:
                                ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast.Value);
                                break;
                        }
                        break;
                    case TypeKind.Pointer:
                        if (valueType != null && valueType.TypeKind != TypeKind.Pointer && valueType != TypeTable.U64Type)
                        {
                            ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast.Value);
                        }
                        break;
                    case TypeKind.Enum:
                        if (valueType != null && valueType.TypeKind != TypeKind.Integer)
                        {
                            ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast.Value);
                        }
                        break;
                    case null:
                        ErrorReporter.Report($"Unable to cast to invalid type '{PrintTypeDefinition(cast.TargetTypeDefinition)}'", cast);
                        break;
                    default:
                        if (valueType != null)
                        {
                            ErrorReporter.Report($"Unable to cast type '{valueType.Name}' to '{cast.TargetType.Name}'", cast);
                        }
                        break;
                }
                return cast.TargetType;
            }
            case CompoundExpressionAst compoundExpression:
                var types = new IType[compoundExpression.Children.Count];

                var error = false;
                var privateType = false;
                uint size = 0;
                for (var i = 0; i < types.Length; i++)
                {
                    var type = VerifyExpression(compoundExpression.Children[i], currentFunction, scope);
                    if (type == null)
                    {
                        error = true;
                    }
                    else
                    {
                        size += type.Size;
                        types[i] = type;
                        if (type.Private)
                        {
                            privateType = true;
                        }
                    }
                }
                if (error)
                {
                    return null;
                }

                var compoundTypeName = string.Join(", ", types.Select(t => t.Name));
                if (GetType(compoundTypeName, compoundExpression.FileIndex, out var compoundType))
                {
                    return compoundType;
                }

                return CreateCompoundType(types, compoundTypeName, size, privateType, compoundExpression.FileIndex);
            case null:
                return null;
            default:
                ErrorReporter.Report($"Invalid expression", ast);
                return null;
        }
    }

    private static void VerifyConstantIfNecessary(IAst ast, IType targetType)
    {
        if (ast is ConstantAst constant && constant.Type != targetType && (constant.Type.TypeKind == TypeKind.Integer || constant.Type.TypeKind == TypeKind.Float) && targetType.TypeKind != TypeKind.Any)
        {
            switch (targetType.TypeKind)
            {
                case TypeKind.Integer:
                    if (constant.Type.TypeKind == TypeKind.Integer)
                    {
                        var currentInt = (PrimitiveAst)constant.Type;
                        var newInt = (PrimitiveAst)targetType;

                        if (!newInt.Signed)
                        {
                            if (currentInt.Signed && constant.Value.Integer < 0)
                            {
                                ErrorReporter.Report($"Unsigned type '{targetType.Name}' cannot be negative", constant);
                            }
                            else
                            {
                                var largestAllowedValue = Math.Pow(2, 8 * newInt.Size) - 1;
                                if (constant.Value.UnsignedInteger > largestAllowedValue)
                                {
                                    ErrorReporter.Report($"Value '{constant.Value.UnsignedInteger}' out of range for type '{targetType.Name}'", constant);
                                }
                            }
                        }
                        else
                        {
                            var largestAllowedValue = Math.Pow(2, 8 * newInt.Size - 1) - 1;
                            if (currentInt.Signed)
                            {
                                var lowestAllowedValue = -Math.Pow(2, 8 * newInt.Size - 1);
                                if (constant.Value.Integer < lowestAllowedValue || constant.Value.Integer > largestAllowedValue)
                                {
                                    ErrorReporter.Report($"Value '{constant.Value.Integer}' out of range for type '{targetType.Name}'", constant);
                                }
                            }
                            else
                            {
                                if (constant.Value.UnsignedInteger > largestAllowedValue)
                                {
                                    ErrorReporter.Report($"Value '{constant.Value.Integer}' out of range for type '{targetType.Name}'", constant);
                                }
                            }
                        }
                    }
                    // Convert float to int, shelved for now
                    /*else if (constant.Type.TypeKind == TypeKind.Float)
                    {
                        if (type.Size < constant.Type.Size && (constant.Value.Double > float.MaxValue || constant.Value.Double < float.MinValue))
                        {
                            ErrorReporter.Report($"Value '{constant.Value.Double}' out of range for type '{type.Name}'", constant);
                        }
                    }*/
                    else
                    {
                        ErrorReporter.Report($"Type '{constant.Type.Name}' cannot be converted to '{targetType.Name}'", constant);
                    }
                    break;
                case TypeKind.Float:
                    // Convert int to float
                    if (constant.Type.TypeKind == TypeKind.Integer)
                    {
                        var currentInt = (PrimitiveAst)constant.Type;
                        constant.Value = currentInt.Signed ? new Constant {Double = constant.Value.Integer} : new Constant {Double = constant.Value.UnsignedInteger};
                    }
                    else if (constant.Type.TypeKind == TypeKind.Float)
                    {
                        if (targetType.Size < constant.Type.Size && (constant.Value.Double > float.MaxValue || constant.Value.Double < float.MinValue))
                        {
                            ErrorReporter.Report($"Value '{constant.Value.Double}' out of range for type '{targetType.Name}'", constant);
                        }
                    }
                    else
                    {
                        ErrorReporter.Report($"Type '{constant.Type.Name}' cannot be converted to '{targetType.Name}'", constant);
                    }
                    break;
            }

            constant.Type = targetType;
        }
    }

    private static IType VerifyEnumValue(EnumAst enumAst, StructFieldRefAst structField, out bool isConstant)
    {
        isConstant = false;
        structField.IsEnum = true;
        structField.Types = new []{enumAst};

        if (structField.Children.Count > 2)
        {
            ErrorReporter.Report("Cannot get a value of an enum value", structField.Children[2]);
            return null;
        }

        if (structField.Children[1] is not IdentifierAst value)
        {
            ErrorReporter.Report($"Value of enum '{enumAst.Name}' should be an identifier", structField.Children[1]);
            return null;
        }

        if (!enumAst.Values.TryGetValue(value.Name, out var enumValue))
        {
            ErrorReporter.Report($"Enum '{enumAst.Name}' does not contain value '{value.Name}'", value);
            return null;
        }

        structField.ConstantValue = enumValue.Value;
        isConstant = true;
        return enumAst;
    }

    private static IType VerifyCall(CallAst call, IFunction currentFunction, IScope scope, IInterface function = null)
    {
        var argumentTypes = new IType[call.Arguments.Count];
        var argumentsError = false;

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            var argumentType = VerifyExpression(argument, currentFunction, scope);
            if (argumentType == null && argument is not NullAst)
            {
                argumentsError = true;
            }
            argumentTypes[i] = argumentType;
        }

        var specifiedArguments = new Dictionary<string, IType>();
        if (call.SpecifiedArguments != null)
        {
            foreach (var (name, argument) in call.SpecifiedArguments)
            {
                var argumentType = VerifyExpression(argument, currentFunction, scope);
                if (argumentType == null && argument is not NullAst)
                {
                    argumentsError = true;
                }
                specifiedArguments[name] = argumentType;
            }
        }

        if (call.Generics != null)
        {
            foreach (var generic in call.Generics)
            {
                var genericType = VerifyType(generic, scope);
                if (genericType == null)
                {
                    ErrorReporter.Report($"Undefined generic type '{PrintTypeDefinition(generic)}'", generic);
                    argumentsError = true;
                }
            }
        }

        if (argumentsError)
        {
            if (function == null)
            {
                if (GetExistingFunction(call.Name, call.FileIndex, out var existingFunction, out _))
                {
                    return existingFunction.ReturnType;
                }
                if (GetExistingPolymorphicFunction(call.Name, call.FileIndex, out existingFunction))
                {
                    return existingFunction.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) ? null : existingFunction.ReturnType;
                }

                ErrorReporter.Report($"Call to undefined function '{call.Name}'", call);
                return null;
            }
            return function.ReturnType;
        }

        if (function == null)
        {
            function = DetermineCallingFunction(call, argumentTypes, specifiedArguments, scope);

            if (function == null)
            {
                return null;
            }
        }
        else
        {
            if (call.Arguments.Count != function.Arguments.Count)
            {
                ErrorReporter.Report($"Expected call to function '{function.Name}' to have {function.Arguments.Count} arguments", call);
                return null;
            }

            if (!VerifyArguments(call, argumentTypes, specifiedArguments, function))
            {
                ReportNoFunctionForCall(function.Name, call, argumentTypes, specifiedArguments);
                return null;
            }
        }

        if (function is FunctionAst functionAst)
        {
            VerifyFunctionIfNecessary(functionAst, currentFunction);

            if (call.Name == "type_of" || call.Name == "size_of")
            {
                IType type;
                if (argumentTypes.Length > 0)
                {
                    type = argumentTypes[0];
                }
                else
                {
                    type = specifiedArguments["type"];
                }

                if (type.TypeKind != TypeKind.Type)
                {
                    call.TypeInfo = type;
                    return function.ReturnType;
                }
            }

            if (currentFunction != null && !currentFunction.Flags.HasFlag(FunctionFlags.CallsCompiler) && (functionAst.Flags.HasFlag(FunctionFlags.Compiler) || functionAst.Flags.HasFlag(FunctionFlags.CallsCompiler)))
            {
                currentFunction.Flags |= FunctionFlags.CallsCompiler;
            }

            call.Function = functionAst;
            if (call.Inline)
            {
                if (!functionAst.Flags.HasFlag(FunctionFlags.Verified) && function != currentFunction)
                {
                    VerifyFunction(functionAst);
                }
            }
            else if (functionAst.Flags.HasFlag(FunctionFlags.Inline))
            {
                if (!functionAst.Flags.HasFlag(FunctionFlags.Verified) && function != currentFunction)
                {
                    VerifyFunction(functionAst);
                }
                call.Inline = true;
            }

            OrderCallArguments(call, function, argumentTypes);

            // Verify varargs call arguments
            if (functionAst.Flags.HasFlag(FunctionFlags.Params))
            {
                var paramsType = functionAst.ParamsElementType;

                if (paramsType != null)
                {
                    for (var i = function.ArgumentCount; i < argumentTypes.Length; i++)
                    {
                        var argumentAst = call.Arguments[i];
                        if (argumentAst is NullAst nullAst)
                        {
                            nullAst.TargetType = functionAst.ParamsElementType;
                        }
                        else
                        {
                            var argument = argumentTypes[i];
                            if (argument != null)
                            {
                                if (paramsType.TypeKind == TypeKind.Type)
                                {
                                    if (argument.TypeKind != TypeKind.Type)
                                    {
                                        argument.Used = true;
                                        var typeIndex = new ConstantAst {Type = TypeTable.S32Type, Value = new Constant {Integer = argument.TypeIndex}};
                                        call.Arguments[i] = typeIndex;
                                        argumentTypes[i] = typeIndex.Type;
                                    }
                                }
                                else
                                {
                                    VerifyConstantIfNecessary(argumentAst, paramsType);
                                }
                            }
                        }
                    }
                }
            }
        }
        else
        {
            call.Interface = function;
            OrderCallArguments(call, function, argumentTypes);
        }

        return function.ReturnType;
    }

    private static IInterface DetermineCallingFunction(CallAst call, IType[] arguments, Dictionary<string, IType> specifiedArguments, IScope scope)
    {
        var privateScope = PrivateScopes[call.FileIndex];
        var functionCount = 0;

        if (privateScope == null)
        {
            if (GlobalScope.Functions.TryGetValue(call.Name, out var functions))
            {
                if (FindFunctionThatMatchesCall(call, arguments, specifiedArguments, scope, functions, out var function))
                {
                    return function;
                }
                functionCount += functions.Count;
            }

            if (GlobalScope.PolymorphicFunctions.TryGetValue(call.Name, out functions))
            {
                if (FindPolymorphicFunctionThatMatchesCall(call, arguments, specifiedArguments, scope, functions, out var function))
                {
                    return function;
                }
                functionCount += functions.Count;
            }

            if (GetScopeIdentifier(scope, call.Name, out var ast))
            {
                var interfaceAst = VerifyInterfaceCall(call, ast, arguments, specifiedArguments);
                if (interfaceAst != null)
                {
                    return interfaceAst;
                }
            }
        }
        else
        {
            if (privateScope.Functions.TryGetValue(call.Name, out var functions))
            {
                if (FindFunctionThatMatchesCall(call, arguments, specifiedArguments, scope, functions, out var function))
                {
                    return function;
                }
                functionCount += functions.Count;
            }

            if (GlobalScope.Functions.TryGetValue(call.Name, out functions))
            {
                if (FindFunctionThatMatchesCall(call, arguments, specifiedArguments, scope, functions, out var function))
                {
                    return function;
                }
                functionCount += functions.Count;
            }

            if (privateScope.PolymorphicFunctions.TryGetValue(call.Name, out functions))
            {
                if (FindPolymorphicFunctionThatMatchesCall(call, arguments, specifiedArguments, scope, functions, out var function))
                {
                    return function;
                }
                functionCount += functions.Count;
            }

            if (GlobalScope.PolymorphicFunctions.TryGetValue(call.Name, out functions))
            {
                if (FindPolymorphicFunctionThatMatchesCall(call, arguments, specifiedArguments, scope, functions, out var function))
                {
                    return function;
                }
                functionCount += functions.Count;
            }

            if (GetScopeIdentifier(scope, call.Name, out var ast))
            {
                var interfaceAst = VerifyInterfaceCall(call, ast, arguments, specifiedArguments);
                if (interfaceAst != null)
                {
                    return interfaceAst;
                }
            }
        }

        if (functionCount == 0)
        {
            ErrorReporter.Report($"Call to undefined function '{call.Name}'", call);
        }
        else
        {
            ReportNoFunctionForCall(call.Name, call, arguments, specifiedArguments);
        }
        return null;
    }

    private static void ReportNoFunctionForCall(string callName, CallAst call, IType[] arguments, Dictionary<string, IType> specifiedArguments)
    {
        if (arguments.Length > 0 || specifiedArguments.Any())
        {
            var argumentTypes = string.Join(", ", arguments.Select(type => type == null ? "null" : type.Name));
            if (arguments.Length > 0 && specifiedArguments.Any())
            {
                argumentTypes += ", ";
            }
            argumentTypes += string.Join(", ", specifiedArguments.Select(arg => arg.Value == null ? $"{arg.Key}: null" : $"{arg.Key}: {arg.Value.Name}"));
            ErrorReporter.Report($"No overload of function '{callName}' found with arguments ({argumentTypes})", call);
        }
        else
        {
            ErrorReporter.Report($"No overload of function '{callName}' found with 0 arguments", call);
        }
    }

    private static bool FindFunctionThatMatchesCall(CallAst call, IType[] arguments, Dictionary<string, IType> specifiedArguments, IScope scope, List<FunctionAst> functions, out FunctionAst matchedFunction)
    {
        foreach (var function in functions)
        {
            if (!function.Flags.HasFlag(FunctionFlags.DefinitionVerified))
            {
                VerifyFunctionDefinition(function);
            }

            if (VerifyArguments(call, arguments, specifiedArguments, function, function.Flags.HasFlag(FunctionFlags.Varargs), function.Flags.HasFlag(FunctionFlags.Params), function.ParamsElementType, function.Flags.HasFlag(FunctionFlags.Extern)))
            {
                matchedFunction = function;
                return true;
            }
        }
        matchedFunction = null;
        return false;
    }

    private static bool FindPolymorphicFunctionThatMatchesCall(CallAst call, IType[] arguments, Dictionary<string, IType> specifiedArguments, IScope scope, List<FunctionAst> polymorphicFunctions, out FunctionAst polymorphedFunction)
    {
        for (var i = 0; i < polymorphicFunctions.Count; i++)
        {
            var function = polymorphicFunctions[i];
            if (!function.Flags.HasFlag(FunctionFlags.DefinitionVerified))
            {
                VerifyFunctionDefinition(function);
            }

            if (call.Generics != null && call.Generics.Count != function.Generics.Count)
            {
                continue;
            }

            var match = true;
            var callArgIndex = 0;
            var functionArgCount = function.ArgumentCount;
            var genericTypes = new IType[function.Generics.Count];

            if (call.Generics != null)
            {
                var genericsError = false;
                for (var index = 0; index < call.Generics.Count; index++)
                {
                    var generic = call.Generics[index];
                    genericTypes[index] = VerifyType(generic, scope);
                    if (genericTypes[index] == null)
                    {
                        genericsError = true;
                        ErrorReporter.Report($"Undefined type '{PrintTypeDefinition(generic)}' in generic function call", generic);
                    }
                }
                if (genericsError)
                {
                    continue;
                }
            }

            if (call.SpecifiedArguments != null)
            {
                var specifiedArgsMatch = true;
                foreach (var (name, argument) in call.SpecifiedArguments)
                {
                    var found = false;
                    for (var argIndex = 0; argIndex < functionArgCount; argIndex++)
                    {
                        var functionArg = function.Arguments[argIndex];
                        if (functionArg.Name == name)
                        {
                            if (functionArg.HasGenerics)
                            {
                                found = VerifyPolymorphicArgument(argument, specifiedArguments[name], functionArg.TypeDefinition, genericTypes);
                            }
                            else
                            {
                                found = VerifyArgument(argument, specifiedArguments[name], functionArg.Type);
                            }
                            break;
                        }
                    }
                    if (!found)
                    {
                        specifiedArgsMatch = false;
                        break;
                    }
                }
                if (!specifiedArgsMatch)
                {
                    continue;
                }
            }

            for (var arg = 0; arg < functionArgCount; arg++)
            {
                var functionArg = function.Arguments[arg];
                if (specifiedArguments.ContainsKey(functionArg.Name))
                {
                    continue;
                }

                var argumentAst = call.Arguments.ElementAtOrDefault(callArgIndex);
                if (argumentAst == null)
                {
                    if (functionArg.Value == null)
                    {
                        match = false;
                        break;
                    }
                }
                else
                {
                    if (functionArg.HasGenerics)
                    {
                        if (!VerifyPolymorphicArgument(argumentAst, arguments[callArgIndex], functionArg.TypeDefinition, genericTypes))
                        {
                            match = false;
                            break;
                        }
                    }
                    else
                    {
                        if (!VerifyArgument(argumentAst, arguments[callArgIndex],functionArg.Type))
                        {
                            match = false;
                            break;
                        }
                    }
                    callArgIndex++;
                }
            }

            var passArrayToParams = false;
            if (match && function.Flags.HasFlag(FunctionFlags.Params))
            {
                var paramsArgument = function.Arguments[^1];
                var paramsType = paramsArgument.TypeDefinition.Generics.FirstOrDefault();

                if (paramsType != null)
                {
                    if (paramsArgument.HasGenerics)
                    {
                        for (; callArgIndex < arguments.Length; callArgIndex++)
                        {
                            if (!VerifyPolymorphicArgument(call.Arguments[callArgIndex], arguments[callArgIndex], paramsType, genericTypes))
                            {
                                match = false;
                                break;
                            }
                        }
                    }
                    else if (callArgIndex == arguments.Length - 1)
                    {
                        var argument = arguments[^1];
                        if (argument?.TypeKind == TypeKind.Array)
                        {
                            var arrayStruct = (StructAst)argument;
                            if (arrayStruct.GenericTypes[0] == function.ParamsElementType)
                            {
                                passArrayToParams = true;
                            }
                            else
                            {
                                match = VerifyArgument(call.Arguments[callArgIndex], argument, function.ParamsElementType);
                            }
                        }
                        else
                        {
                            match = VerifyArgument(call.Arguments[callArgIndex], argument, function.ParamsElementType);
                        }
                    }
                    else
                    {
                        for (; callArgIndex < arguments.Length; callArgIndex++)
                        {
                            if (!VerifyArgument(call.Arguments[callArgIndex], arguments[callArgIndex], function.ParamsElementType))
                            {
                                match = false;
                                break;
                            }
                        }
                    }
                }
            }

            var privateGenericTypes = false;
            if (match)
            {
                foreach (var genericType in genericTypes)
                {
                    if (genericType == null)
                    {
                        match = false;
                        break;
                    }
                    if (genericType.Private)
                    {
                        privateGenericTypes = true;
                    }
                }
            }

            if (match && (function.Flags.HasFlag(FunctionFlags.Varargs) || callArgIndex == call.Arguments.Count))
            {
                call.PassArrayToParams = passArrayToParams;
                var uniqueName = $"{function.Name}.{i}.{string.Join('.', genericTypes.Select(type => type.TypeIndex))}";

                if (GetExistingFunction(uniqueName, call.FileIndex, out polymorphedFunction, out _))
                {
                    return true;
                }

                polymorphedFunction = Polymorpher.CreatePolymorphedFunction(function, $"{function.Name}<{string.Join(", ", genericTypes.Select(type => type.Name))}>", privateGenericTypes, genericTypes);
                if (polymorphedFunction.ReturnType == null)
                {
                    polymorphedFunction.ReturnType = VerifyType(polymorphedFunction.ReturnTypeDefinition, scope);
                }

                foreach (var argument in polymorphedFunction.Arguments)
                {
                    if (argument.Type == null)
                    {
                        argument.Type = VerifyType(argument.TypeDefinition, scope, out _, out _, out var isParams, allowParams: true);
                        if (isParams)
                        {
                            var paramsArrayType = (StructAst)argument.Type;
                            polymorphedFunction.ParamsElementType = paramsArrayType.GenericTypes[0];
                        }
                    }
                }

                var fileIndex = function.FileIndex;
                if (privateGenericTypes && !function.Private)
                {
                    fileIndex = call.FileIndex;
                }

                AddFunction(uniqueName, fileIndex, polymorphedFunction);
                TypeTable.Add(polymorphedFunction);
                TypeTable.CreateTypeInfo(polymorphedFunction);

                polymorphedFunction.Flags |= FunctionFlags.Queued;
                _astCompleteQueue.Enqueue(polymorphedFunction);

                return true;
            }
        }
        polymorphedFunction = null;
        return false;
    }

    private static bool VerifyArguments(CallAst call, IType[] arguments, Dictionary<string, IType> specifiedArguments, IInterface function, bool varargs = false, bool Params = false, IType paramsElementType = null, bool Extern = false)
    {
        var match = true;
        var callArgIndex = 0;
        var functionArgCount = function.ArgumentCount;

        if (call.SpecifiedArguments != null)
        {
            foreach (var (name, argument) in call.SpecifiedArguments)
            {
                var found = false;
                for (var argIndex = 0; argIndex < function.Arguments.Count; argIndex++)
                {
                    var functionArg = function.Arguments[argIndex];
                    if (functionArg.Name == name)
                    {
                        if (VerifyArgument(argument, specifiedArguments[name], functionArg.Type, Extern))
                        {
                            found = true;
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
        }

        for (var arg = 0; arg < functionArgCount; arg++)
        {
            var functionArg = function.Arguments[arg];
            if (specifiedArguments.ContainsKey(functionArg.Name))
            {
                continue;
            }

            var argumentAst = call.Arguments.ElementAtOrDefault(callArgIndex);
            if (argumentAst == null)
            {
                if (functionArg.Value == null)
                {
                    match = false;
                    break;
                }
            }
            else
            {
                if (VerifyArgument(argumentAst, arguments[callArgIndex], functionArg.Type, Extern))
                {
                    callArgIndex++;
                }
                // If the function argument is null or the argument is the last, then break
                else if (functionArg.Value == null || callArgIndex >= call.Arguments.Count)
                {
                    match = false;
                    break;
                }
            }
        }

        if (match && Params)
        {
            if (paramsElementType != null)
            {
                if (callArgIndex == arguments.Length - 1)
                {
                    var argument = arguments[^1];
                    if (argument?.TypeKind == TypeKind.Array)
                    {
                        var arrayStruct = (StructAst)argument;
                        if (arrayStruct.GenericTypes[0] == paramsElementType)
                        {
                            call.PassArrayToParams = true;
                            return true;
                        }
                    }

                    return VerifyArgument(call.Arguments[callArgIndex], argument, paramsElementType);
                }

                for (; callArgIndex < arguments.Length; callArgIndex++)
                {
                    if (!VerifyArgument(call.Arguments[callArgIndex], arguments[callArgIndex], paramsElementType))
                    {
                        match = false;
                        break;
                    }
                }
            }
        }

        return match && (varargs || callArgIndex == call.Arguments.Count);
    }

    private static InterfaceAst VerifyInterfaceCall(CallAst call, IAst ast, IType[] arguments, Dictionary<string, IType> specifiedArguments)
    {
        InterfaceAst interfaceAst = null;
        if (ast is DeclarationAst declaration)
        {
            if (declaration.Type is InterfaceAst interfaceType)
            {
                interfaceAst = interfaceType;
            }
            else
            {
                return null;
            }
        }
        else if (ast is VariableAst variable)
        {
            if (variable.Type is InterfaceAst interfaceType)
            {
                interfaceAst = interfaceType;
            }
            else
            {
                return null;
            }
        }

        if (interfaceAst == null)
        {
            return null;
        }

        var argCount = interfaceAst.Arguments.Count;

        if (call.SpecifiedArguments != null)
        {
            foreach (var (name, argument) in call.SpecifiedArguments)
            {
                var found = false;
                for (var argIndex = 0; argIndex < argCount; argIndex++)
                {
                    var functionArg = interfaceAst.Arguments[argIndex];
                    if (functionArg.Name == name)
                    {
                        found = VerifyArgument(argument, specifiedArguments[name], functionArg.Type);
                        break;
                    }
                }
                if (!found)
                {
                    return null;
                }
            }
        }

        var match = true;
        var callArgIndex = 0;
        for (var arg = 0; arg < argCount; arg++)
        {
            var functionArg = interfaceAst.Arguments[arg];
            if (specifiedArguments.ContainsKey(functionArg.Name))
            {
                continue;
            }

            var argumentAst = call.Arguments.ElementAtOrDefault(callArgIndex);
            if (argumentAst == null)
            {
                if (functionArg.Value == null)
                {
                    match = false;
                    break;
                }
            }
            else
            {
                if (!VerifyArgument(argumentAst, arguments[callArgIndex], functionArg.Type))
                {
                    match = false;
                    break;
                }
                callArgIndex++;
            }
        }

        if (match && callArgIndex == call.Arguments.Count)
        {
            return interfaceAst;
        }

        return null;
    }

    private static void OrderCallArguments(CallAst call, IInterface function, IType[] argumentTypes)
    {
        // Verify call arguments match the types of the function arguments
        for (var i = 0; i < function.ArgumentCount; i++)
        {
            var functionArg = function.Arguments[i];
            if (call.SpecifiedArguments != null && call.SpecifiedArguments.TryGetValue(functionArg.Name, out var specifiedArgument))
            {
                VerifyConstantIfNecessary(specifiedArgument, functionArg.Type);

                call.Arguments.Insert(i, specifiedArgument);
                continue;
            }

            var argumentAst = call.Arguments.ElementAtOrDefault(i);
            if (argumentAst == null)
            {
                call.Arguments.Insert(i, functionArg.Value);
            }
            else if (argumentAst is NullAst nullAst)
            {
                nullAst.TargetType = functionArg.Type;
            }
            else if (functionArg.Type.TypeKind != TypeKind.Any)
            {
                if (i < argumentTypes.Length)
                {
                    var argument = argumentTypes[i];
                    if (argument != null)
                    {
                        if (functionArg.Type.TypeKind == TypeKind.Type)
                        {
                            if (argument.TypeKind != TypeKind.Type)
                            {
                                argument.Used = true;
                                var typeIndex = new ConstantAst {Type = TypeTable.S32Type, Value = new Constant {Integer = argument.TypeIndex}};
                                call.Arguments[i] = typeIndex;
                                argumentTypes[i] = typeIndex.Type;
                            }
                        }
                        else if (!TypeEquals(functionArg.Type, argument) && functionArg.Value != null)
                        {
                            call.Arguments.Insert(i, functionArg.Value);
                        }
                        else
                        {
                            VerifyConstantIfNecessary(call.Arguments[i], functionArg.Type);
                        }
                    }
                }
            }
        }
    }

    private static bool VerifyArgument(IAst argumentAst, IType callType, IType argumentType, bool externCall = false)
    {
        if (argumentType == null)
        {
            return false;
        }
        if (argumentAst is NullAst)
        {
            if (argumentType.TypeKind != TypeKind.Pointer)
            {
                return false;
            }
        }
        else if (argumentType.TypeKind != TypeKind.Type && argumentType.TypeKind != TypeKind.Any)
        {
            if (externCall && callType.TypeKind == TypeKind.String)
            {
                if (argumentType != TypeTable.RawStringType)
                {
                    return false;
                }
            }
            else if (!TypeEquals(argumentType, callType))
            {
                return false;
            }
        }
        return true;
    }

    private static bool VerifyPolymorphicArgument(IAst ast, IType callType, TypeDefinition argumentType, IType[] genericTypes)
    {
        if (ast is NullAst)
        {
            // Return false if the generic types have been determined,
            // the type cannot be inferred from a null argument if the generics haven't been determined yet
            return argumentType.Name == "*" || genericTypes.All(generic => generic != null);
        }

        return VerifyPolymorphicArgument(callType, argumentType, genericTypes);
    }

    private static bool VerifyPolymorphicArgument(IType callType, TypeDefinition argumentType, IType[] genericTypes)
    {
        if (argumentType.IsGeneric)
        {
            var genericType = genericTypes[argumentType.GenericIndex];
            if (genericType == null)
            {
                genericTypes[argumentType.GenericIndex] = callType;
            }
            else if (!TypeEquals(genericType, callType))
            {
                return false;
            }
        }
        else
        {
            if (argumentType.Generics.Any())
            {
                switch (callType.TypeKind)
                {
                    case TypeKind.Pointer:
                        if (argumentType.Name != "*" || argumentType.Generics.Count != 1)
                        {
                            return false;
                        }
                        var pointerType = (PointerType)callType;
                        return VerifyPolymorphicArgument(pointerType.PointedType, argumentType.Generics[0], genericTypes);
                    case TypeKind.Array:
                    case TypeKind.Struct:
                        var structDef = (StructAst)callType;
                        if (structDef.GenericTypes == null || argumentType.Generics.Count != structDef.GenericTypes.Length || argumentType.Name != structDef.BaseStructName)
                        {
                            return false;
                        }
                        for (var i = 0; i < structDef.GenericTypes.Length; i++)
                        {
                            if (!VerifyPolymorphicArgument(structDef.GenericTypes[i], argumentType.Generics[i], genericTypes))
                            {
                                return false;
                            }
                        }
                        break;
                    default:
                        return false;
                }
            }
            else if (callType.Name != argumentType.Name)
            {
                return false;
            }
        }
        return true;
    }

    private static IType VerifyExpressionType(ExpressionAst expression, IFunction currentFunction, IScope scope, out bool isConstant)
    {
        // 1. Get the type of the initial child
        expression.Type = VerifyExpression(expression.Children[0], currentFunction, scope, out isConstant, out bool isType);
        if (expression.Type == null) return null;

        for (var i = 1; i < expression.Children.Count; i++)
        {
            // 2. Get the next operator and expression type
            var op = expression.Operators[i - 1];
            var next = expression.Children[i];
            if (next is NullAst nullAst)
            {
                if ((expression.Type.TypeKind != TypeKind.Pointer && expression.Type.TypeKind != TypeKind.Interface) || (op != Operator.Equality && op != Operator.NotEqual))
                {
                    ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and null", next);
                }

                nullAst.TargetType = expression.Type;
                expression.Type = TypeTable.BoolType;
                expression.ResultingTypes.Add(expression.Type);
                continue;
            }

            var nextExpressionType = VerifyExpression(next, currentFunction, scope, out var constant, out bool nextIsType);
            if (nextExpressionType == null) return null;
            isConstant = isConstant && constant;

            // 3. Verify the operator and expression types are compatible and convert the expression type if necessary
            if (isType && nextIsType)
            {
                if (op != Operator.Equality && op != Operator.NotEqual)
                {
                    ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable when comparing types", expression.Children[i]);
                    return null;
                }
                expression.Type = TypeTable.BoolType;
                expression.ResultingTypes.Add(expression.Type);
                isType = false;
                continue;
            }

            var type = expression.Type.TypeKind;
            var nextType = nextExpressionType.TypeKind;
            if ((type == TypeKind.Struct && nextType == TypeKind.Struct) ||
                (type == TypeKind.String && nextType == TypeKind.String))
            {
                if (TypeEquals(expression.Type, nextExpressionType, true))
                {
                    var overload = VerifyOperatorOverloadType((StructAst)expression.Type, op, currentFunction, expression.Children[i], scope);
                    if (overload != null)
                    {
                        expression.OperatorOverloads[i] = overload;
                        expression.Type = overload.ReturnType;
                    }
                }
                else
                {
                    ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                }
            }
            else
            {
                switch (op)
                {
                    // Both need to be bool and returns bool
                    case Operator.And:
                    case Operator.Or:
                        if (type != TypeKind.Boolean || nextType != TypeKind.Boolean)
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            expression.Type = TypeTable.BoolType;
                        }
                        break;
                    // Requires same types and returns bool
                    case Operator.Equality:
                    case Operator.NotEqual:
                    case Operator.GreaterThan:
                    case Operator.LessThan:
                    case Operator.GreaterThanEqual:
                    case Operator.LessThanEqual:
                        if ((type == TypeKind.Enum && nextType == TypeKind.Enum)
                            || (type == TypeKind.Type && nextType == TypeKind.Type))
                        {
                            if ((op != Operator.Equality && op != Operator.NotEqual) || !TypeEquals(expression.Type, nextExpressionType))
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            }
                        }
                        else if (!((type == TypeKind.Integer && (nextType == TypeKind.Integer || nextType == TypeKind.Float)) ||
                            (type == TypeKind.Float && (nextType == TypeKind.Integer || nextType == TypeKind.Float)) ||
                            (type == TypeKind.Pointer && nextType == TypeKind.Pointer)))
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                        }
                        expression.Type = TypeTable.BoolType;
                        break;
                    // Requires same types and returns more precise type
                    case Operator.Add:
                    case Operator.Subtract:
                    case Operator.Multiply:
                    case Operator.Divide:
                        if (((type == TypeKind.Pointer && nextType == TypeKind.Integer) ||
                            (type == TypeKind.Integer && nextType == TypeKind.Pointer)) &&
                            (op == Operator.Add || op == Operator.Subtract))
                        {
                            if (nextType == TypeKind.Pointer)
                            {
                                expression.Type = nextExpressionType;
                            }
                        }
                        else if ((type == TypeKind.Integer || type == TypeKind.Float) &&
                            (nextType == TypeKind.Integer || nextType == TypeKind.Float))
                        {
                            if (expression.Type == nextExpressionType)
                                break;

                            // For integer operations, use the larger size and convert to signed if one type is signed
                            if (type == TypeKind.Integer && nextType == TypeKind.Integer)
                            {
                                expression.Type = GetNextIntegerType(expression.Type, nextExpressionType);
                            }
                            // For floating point operations, convert to the larger size
                            else if (type == TypeKind.Float && nextType == TypeKind.Float)
                            {
                                if (expression.Type.Size < nextExpressionType.Size)
                                {
                                    expression.Type = nextExpressionType;
                                }
                            }
                            // For an int lhs and float rhs, convert to the floating point type
                            // Note that float lhs and int rhs are covered since the floating point is already selected
                            else if (nextType == TypeKind.Float)
                            {
                                expression.Type = nextExpressionType;
                            }
                        }
                        else
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                        }
                        break;
                    case Operator.Modulus:
                        if (type != TypeKind.Integer || nextType != TypeKind.Integer)
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            return null;
                        }
                        else
                        {
                            expression.Type = GetNextIntegerType(expression.Type, nextExpressionType);
                        }
                        break;
                    // Requires both integer or bool types and returns more same type
                    case Operator.BitwiseAnd:
                    case Operator.BitwiseOr:
                    case Operator.Xor:
                        if (type == TypeKind.Integer && nextType == TypeKind.Integer)
                        {
                            if (expression.Type != nextExpressionType)
                            {
                                expression.Type = GetNextIntegerType(expression.Type, nextExpressionType);
                            }
                        }
                        else if (type == TypeKind.Enum && nextType == TypeKind.Enum)
                        {
                            if (expression.Type != nextExpressionType)
                            {
                                ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            }
                        }
                        else if (!(type == TypeKind.Boolean && nextType == TypeKind.Boolean))
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            if (nextType == TypeKind.Boolean || nextType == TypeKind.Integer)
                            {
                                expression.Type = nextExpressionType;
                            }
                            else if (!(type == TypeKind.Boolean || type == TypeKind.Integer))
                            {
                                // If the type can't be determined, default to int
                                expression.Type = TypeTable.S32Type;
                            }
                        }
                        break;
                    case Operator.ShiftLeft:
                    case Operator.ShiftRight:
                    case Operator.RotateLeft:
                    case Operator.RotateRight:
                        if (type != TypeKind.Integer || nextType != TypeKind.Integer)
                        {
                            ErrorReporter.Report($"Operator {PrintOperator(op)} not applicable to types '{expression.Type.Name}' and '{nextExpressionType.Name}'", expression.Children[i]);
                            if (type != TypeKind.Integer)
                            {
                                if (nextType == TypeKind.Integer)
                                {
                                    expression.Type = nextExpressionType;
                                }
                                else
                                {
                                    // If the type can't be determined, default to int
                                    expression.Type = TypeTable.S32Type;
                                }
                            }
                        }
                        else if (next is ConstantAst constantAst)
                        {
                            constantAst.Type = expression.Type;
                        }
                        break;
                }
            }
            expression.ResultingTypes.Add(expression.Type);
        }
        return expression.Type;
    }

    private static IType GetNextIntegerType(IType currentType, IType nextType)
    {
        var currentIntegerType = (PrimitiveAst)currentType;
        var nextIntegerType = (PrimitiveAst)nextType;

        var size = currentIntegerType.Size > nextIntegerType.Size ? currentIntegerType.Size : nextIntegerType.Size;
        if (currentIntegerType.Signed || nextIntegerType.Signed)
        {
            return size switch
            {
                1 => TypeTable.S8Type,
                2 => TypeTable.S16Type,
                8 => TypeTable.S64Type,
                _ => TypeTable.S32Type
            };
        }

        return size switch
        {
            1 => TypeTable.U8Type,
            2 => TypeTable.U16Type,
            8 => TypeTable.U64Type,
            _ => TypeTable.U32Type
        };
    }

    private static IType VerifyIndex(IndexAst index, IType type, IFunction currentFunction, IScope scope, out bool overloaded)
    {
        // 1. Verify the variable is an array or the operator overload exists
        overloaded = false;
        IType elementType = null;
        switch (type?.TypeKind)
        {
            case TypeKind.Struct:
                index.CallsOverload = true;
                overloaded = true;
                index.Overload = VerifyOperatorOverloadType((StructAst)type, Operator.Subscript, currentFunction, index, scope);
                elementType = index.Overload?.ReturnType;
                break;
            case TypeKind.Array:
                var arrayStruct = (StructAst)type;
                elementType = arrayStruct.GenericTypes[0];
                break;
            case TypeKind.CArray:
                var arrayType = (ArrayType)type;
                elementType = arrayType.ElementType;
                break;
            case TypeKind.Pointer:
                var pointerType = (PointerType)type;
                elementType = pointerType.PointedType;
                break;
            case TypeKind.String:
                elementType = TypeTable.U8Type;
                break;
            case null:
                break;
            default:
                ErrorReporter.Report($"Cannot index type '{type.Name}'", index);
                break;
        }

        // 2. Verify the count expression is an integer
        var indexValue = VerifyExpression(index.Index, currentFunction, scope);
        if (indexValue != null && indexValue.TypeKind != TypeKind.Integer && indexValue.TypeKind != TypeKind.Type)
        {
            ErrorReporter.Report($"Expected index to be type 'int', but got '{indexValue.Name}'", index);
        }

        return elementType;
    }

    private static OperatorOverloadAst VerifyOperatorOverloadType(StructAst type, Operator op, IFunction currentFunction, IAst ast, IScope scope)
    {
        if (_operatorOverloads.TryGetValue(type.Name, out var overloads) && overloads.TryGetValue(op, out var overload))
        {
            if (!overload.Flags.HasFlag(FunctionFlags.Verified) && overload != currentFunction && !overload.Flags.HasFlag(FunctionFlags.DefinitionVerified))
            {
                VerifyOperatorOverloadDefinition(overload);
                _astCompleteQueue.Enqueue(overload);
            }
            return overload;
        }
        if (type.BaseStructName != null && _polymorphicOperatorOverloads.TryGetValue(type.BaseStructName, out var polymorphicOverloads) && polymorphicOverloads.TryGetValue(op, out var polymorphicOverload))
        {
            var polymorphedOverload = Polymorpher.CreatePolymorphedOperatorOverload(polymorphicOverload, type.GenericTypes.ToArray());
            if (overloads == null)
            {
                _operatorOverloads[type.Name] = overloads = new Dictionary<Operator, OperatorOverloadAst>();
            }
            overloads[op] = polymorphedOverload;
            foreach (var argument in polymorphedOverload.Arguments)
            {
                if (argument.Type == null)
                {
                    argument.Type = VerifyType(argument.TypeDefinition, scope);
                }
            }

            Messages.Submit(MessageType.ReadyToBeTypeChecked, polymorphedOverload);

            VerifyOperatorOverloadDefinition(polymorphedOverload);
            _astCompleteQueue.Enqueue(polymorphedOverload);

            return polymorphedOverload;
        }

        ErrorReporter.Report($"Type '{type.Name}' does not contain an overload for operator '{PrintOperator(op)}'", ast);
        return null;
    }

    private static bool TypeEquals(IType target, IType source, bool checkPrimitives = false)
    {
        if (target == null || source == null) return false;
        if (target == source) return true;

        if (target is InterfaceAst interfaceAst)
        {
            // Cannot assign interfaces to interface types
            if (source is InterfaceAst) return false;
            if (source.TypeKind == TypeKind.Pointer)
            {
                var primitive = (PointerType)source;
                return primitive.PointedType == TypeTable.VoidType;
            }

            if (source is not FunctionAst function) return false;
            if (interfaceAst.ReturnType != function.ReturnType || interfaceAst.Arguments.Count != function.Arguments.Count) return false;

            for (var i = 0; i < interfaceAst.Arguments.Count; i++)
            {
                var interfaceArg = interfaceAst.Arguments[i];
                var functionArg = function.Arguments[i];
                if (interfaceArg.Type != functionArg.Type) return false;
            }
            return true;
        }

        switch (target.TypeKind)
        {
            case TypeKind.Integer:
                if (source.TypeKind == TypeKind.Integer)
                {
                    if (!checkPrimitives) return true;
                    var ap = (PrimitiveAst)target;
                    var bp = (PrimitiveAst)source;
                    return target.Size == source.Size && ap.Signed == bp.Signed;
                }
                return false;
            case TypeKind.Float:
                if (source.TypeKind == TypeKind.Float)
                {
                    if (!checkPrimitives) return true;
                    return target.Size == source.Size;
                }
                return false;
            case TypeKind.Pointer:
                if (source.TypeKind != TypeKind.Pointer)
                {
                    return false;
                }
                var targetPointer = (PointerType)target;
                var targetPointerType = targetPointer.PointedType;
                var sourcePointer = (PointerType)source;
                var sourcePointerType = sourcePointer.PointedType;

                if (targetPointerType == TypeTable.VoidType || sourcePointerType == TypeTable.VoidType)
                {
                    return true;
                }

                if (targetPointerType is StructAst targetStruct && sourcePointerType is StructAst sourceStruct)
                {
                    var sourceBaseStruct = sourceStruct.BaseStruct;
                    while (sourceBaseStruct != null)
                    {
                        if (targetStruct == sourceBaseStruct)
                        {
                            return true;
                        }
                        sourceBaseStruct = sourceBaseStruct.BaseStruct;
                    }
                }

                return false;
        }

        return false;
    }

    private static bool TypeDefinitionEquals(TypeDefinition a, TypeDefinition b)
    {
        if (a?.Name != b?.Name || a.Generics.Count != b.Generics.Count) return false;

        for (var i = 0; i < a.Generics.Count; i++)
        {
            if (!TypeDefinitionEquals(a.Generics[i], b.Generics[i])) return false;
        }

        return true;
    }

    public static IType VerifyType(TypeDefinition type, IScope scope, int depth = 0)
    {
        return VerifyType(type, scope, out _, out _, out _, depth);
    }

    private static IType VerifyType(TypeDefinition type, IScope scope, out bool isGeneric, out bool isVarargs, out bool isParams, int depth = 0, bool allowParams = false, int? initialArrayLength = null)
    {
        isGeneric = false;
        isVarargs = false;
        isParams = false;
        if (type == null) return null;

        if (type.BakedType != null)
        {
            return type.BakedType;
        }

        if (type.IsGeneric)
        {
            if (type.Generics.Any())
            {
                ErrorReporter.Report("Generic type cannot have additional generic types", type);
            }
            isGeneric = true;
            return null;
        }

        if (type.Compound)
        {
            var compoundTypeName = PrintCompoundType(type);
            if (GetType(compoundTypeName, type.FileIndex, out var compoundType))
            {
                return compoundType;
            }

            var types = new IType[type.Generics.Count];
            var error = false;
            var privateType = false;
            uint size = 0;
            for (var i = 0; i < types.Length; i++)
            {
                var subType = VerifyType(type.Generics[i], scope, out var hasGeneric, out _, out _);
                if (subType == null && !hasGeneric)
                {
                    error = true;
                }
                else if (hasGeneric)
                {
                    isGeneric = true;
                }
                else
                {
                    size += subType.Size;
                    types[i] = subType;
                    if (subType.Private)
                    {
                        privateType = true;
                    }
                }
            }

            if (error || isGeneric)
            {
                return null;
            }

            return CreateCompoundType(types, compoundTypeName, size, privateType, type.FileIndex);
        }

        if (type.Count != null && type.Name != "Array" && type.Name != "CArray")
        {
            ErrorReporter.Report($"Type '{PrintTypeDefinition(type)}' cannot have a count", type);
            return null;
        }

        var hasGenerics = type.Generics.Any();

        switch (type.Name)
        {
            case "bool":
                if (hasGenerics)
                {
                    ErrorReporter.Report("Type 'bool' cannot have generics", type);
                    return null;
                }
                return TypeTable.BoolType;
            case "string":
                if (hasGenerics)
                {
                    ErrorReporter.Report("Type 'string' cannot have generics", type);
                    return null;
                }
                return TypeTable.StringType;
            case "Array":
                if (type.Generics.Count != 1)
                {
                    ErrorReporter.Report($"Type 'Array' should have 1 generic type, but got {type.Generics.Count}", type);
                    return null;
                }
                return VerifyArray(type, scope, depth, out isGeneric);
            case "CArray":
                if (type.Generics.Count != 1)
                {
                    ErrorReporter.Report($"Type 'CArray' should have 1 generic type, but got {type.Generics.Count}", type);
                    return null;
                }

                var elementType = VerifyType(type.Generics[0], scope, out isGeneric, out _, out _, depth + 1, allowParams);
                if (elementType == null || isGeneric)
                {
                    return null;
                }

                uint arrayLength;
                if (initialArrayLength.HasValue)
                {
                    arrayLength = (uint)initialArrayLength.Value;
                }
                else
                {
                    var countType = VerifyExpression(type.Count, null, scope, out var isConstant, out arrayLength);
                    if (countType?.TypeKind != TypeKind.Integer || !isConstant || arrayLength < 0)
                    {
                        ErrorReporter.Report($"Expected size of C array to be a constant, positive integer", type);
                    }
                }

                var name = $"{PrintTypeDefinition(type)}[{arrayLength}]";
                if (!GetType(name, elementType.FileIndex, out var arrayType))
                {
                    arrayType = new ArrayType
                    {
                        FileIndex = elementType.FileIndex, Name = name, Size = elementType.Size * arrayLength,
                        Alignment = elementType.Alignment, Private = elementType.Private, Length = arrayLength, ElementType = elementType
                    };
                    AddType(name, arrayType);
                    TypeTable.CreateTypeInfo(arrayType);
                }
                return arrayType;
            case "void":
                if (hasGenerics)
                {
                    ErrorReporter.Report("Type 'void' cannot have generics", type);
                    return null;
                }
                return TypeTable.VoidType;
            case "*":
                if (type.Generics.Count != 1)
                {
                    ErrorReporter.Report($"pointer type should have reference to 1 type, but got {type.Generics.Count}", type);
                    return null;
                }

                var pointerTypeName = PrintTypeDefinition(type);
                if (GetType(pointerTypeName, type.FileIndex, out var pointerType))
                {
                    return pointerType;
                }

                var typeDef = type.Generics[0];
                var pointedToType = VerifyType(typeDef, scope, out isGeneric, out _, out _, depth + 1, allowParams);
                if (pointedToType == null || isGeneric)
                {
                    return null;
                }

                // There are some cases where the pointed to type is a struct that contains a field for the pointer type
                // To account for this, the type table needs to be checked for again for the type
                if (!GetType(pointerTypeName, pointedToType.FileIndex, out pointerType))
                {
                    pointerType = CreatePointerType(pointerTypeName, pointedToType);
                }
                return pointerType;
            case "...":
                if (hasGenerics)
                {
                    ErrorReporter.Report("Type 'varargs' cannot have generics", type);
                }
                isVarargs = true;
                return null;
            case "Params":
                if (!allowParams)
                {
                    return null;
                }
                if (depth != 0)
                {
                    ErrorReporter.Report($"Params can only be declared as a top level type, such as 'Params<int>'", type);
                    return null;
                }
                if (type.Generics.Count == 0)
                {
                    isParams = true;
                    const string arrayAny = "Array<Any>";
                    if (GlobalScope.Types.TryGetValue(arrayAny, out var arrayAnyType))
                    {
                        return arrayAnyType;
                    }

                    return CreateArrayStruct(arrayAny, TypeTable.AnyType);
                }
                if (type.Generics.Count != 1)
                {
                    ErrorReporter.Report($"Type 'Params' should have 1 generic type, but got {type.Generics.Count}", type);
                    return null;
                }
                isParams = true;
                return VerifyArray(type, scope, depth, out isGeneric);
            case "Type":
                if (hasGenerics)
                {
                    ErrorReporter.Report("Type 'Type' cannot have generics", type);
                    return null;
                }
                return TypeTable.TypeType;
            case "Any":
                if (hasGenerics)
                {
                    ErrorReporter.Report("Type 'Any' cannot have generics", type);
                    return null;
                }
                return TypeTable.AnyType;
            default:
                if (hasGenerics)
                {
                    var genericName = PrintTypeDefinition(type);
                    if (GetType(genericName, type.FileIndex, out var structType))
                    {
                        return structType;
                    }

                    if (!GetPolymorphicStruct(type.Name, type.FileIndex, out var structDef))
                    {
                        ErrorReporter.Report($"No polymorphic structs of type '{type.Name}'", type);
                        return null;
                    }

                    var generics = type.Generics;
                    var genericTypes = new IType[generics.Count];
                    var error = false;
                    var privateGenericTypes = false;

                    for (var i = 0; i < generics.Count; i++)
                    {
                        var genericType = genericTypes[i] = VerifyType(generics[i], scope, out var hasGeneric, out _, out _, depth + 1, allowParams);
                        if (genericType == null && !hasGeneric)
                        {
                            error = true;
                        }
                        else if (hasGeneric)
                        {
                            isGeneric = true;
                        }
                        else if (genericType.Private)
                        {
                            privateGenericTypes = true;
                        }
                    }

                    if (structDef.Generics.Count != type.Generics.Count)
                    {
                        ErrorReporter.Report($"Expected type '{type.Name}' to have {structDef.Generics.Count} generic(s), but got {type.Generics.Count}", type);
                        return null;
                    }

                    if (error || isGeneric)
                    {
                        return null;
                    }

                    var fileIndex = structDef.FileIndex;
                    if (privateGenericTypes && !structDef.Private)
                    {
                        fileIndex = type.FileIndex;
                    }

                    // Check that the type wasn't created while verifying the generics
                    if (GetType(genericName, fileIndex, out var polyType))
                    {
                        return polyType;
                    }

                    var polyStruct = Polymorpher.CreatePolymorphedStruct(structDef, fileIndex, genericName, TypeKind.Struct, privateGenericTypes, genericTypes);
                    AddType(genericName, polyStruct, fileIndex);
                    VerifyStruct(polyStruct);
                    return polyStruct;
                }
                if (GetType(type.Name, type.FileIndex, out var typeValue))
                {
                    if (typeValue is StructAst structAst)
                    {
                        if (!structAst.Verifying)
                        {
                            VerifyStruct(structAst);
                        }
                    }
                    else if (typeValue is UnionAst union)
                    {
                        if (!union.Verifying)
                        {
                            VerifyUnion(union);
                        }
                    }
                    else if (typeValue is InterfaceAst interfaceAst)
                    {
                        if (!interfaceAst.Verifying)
                        {
                            VerifyInterface(interfaceAst);
                        }
                    }
                    return typeValue;
                }
                return null;
        }
    }

    private static IType VerifyArray(TypeDefinition typeDef, IScope scope, int depth, out bool isGeneric)
    {
        isGeneric = false;
        var elementTypeDef = typeDef.Generics[0];
        var elementType = VerifyType(elementTypeDef, scope, out isGeneric, out _, out _, depth + 1);
        if (elementType == null || isGeneric)
        {
            return null;
        }

        var name = $"Array<{elementType.Name}>";
        var fileIndex = elementType.Private ? elementType.FileIndex : 0;
        if (GetType(name, fileIndex, out var arrayType))
        {
            return arrayType;
        }

        return CreateArrayStruct(name, elementType, fileIndex);
    }

    private static IType CreateArrayStruct(string name, IType elementType, int fileIndex = 0)
    {
        if (BaseArrayType == null)
        {
            return null;
        }

        var arrayStruct = Polymorpher.CreatePolymorphedStruct(BaseArrayType, fileIndex, name, TypeKind.Array, elementType.Private, elementType);
        AddType(name, arrayStruct, fileIndex);
        VerifyStruct(arrayStruct);
        return arrayStruct;
    }

    private static IType CreatePointerType(string name, IType pointedToType)
    {
        var pointerType = new PointerType {FileIndex = pointedToType.FileIndex, Name = name, Private = pointedToType.Private, PointedType = pointedToType};
        AddType(name, pointerType);
        TypeTable.CreateTypeInfo(pointerType);

        return pointerType;
    }

    private static IType CreateCompoundType(IType[] types, string name, uint size, bool privateType, int fileIndex)
    {
        var compoundType = new CompoundType {FileIndex = fileIndex, Name = name, Size = size, Private = privateType, Types = types};
        AddType(name, compoundType);
        TypeTable.CreateTypeInfo(compoundType);
        return compoundType;
    }

    private static string PrintCompoundType(TypeDefinition type)
    {
        var sb = new StringBuilder();
        PrintGenericTypes(type, sb);
        return sb.ToString();
    }

    public static string PrintTypeDefinition(TypeDefinition type)
    {
        if (type == null) return string.Empty;

        if (type.BakedType != null)
        {
            return type.BakedType.Name;
        }

        if (!type.Generics.Any())
        {
            return type.Name;
        }

        var sb = new StringBuilder();
        if (type.Name == "*")
        {
            PrintTypeDefinition(type.Generics[0], sb);
            sb.Append('*');
        }
        else
        {
            sb.Append(type.Name);
            if (type.Generics.Any())
            {
                sb.Append('<');
                PrintGenericTypes(type, sb);
                sb.Append('>');
            }
        }

        return sb.ToString();
    }

    private static void PrintTypeDefinition(TypeDefinition type, StringBuilder sb)
    {
        if (type == null) return;

        if (type.BakedType != null)
        {
            sb.Append(type.BakedType.Name);
        }
        else if (type.Name == "*")
        {
            PrintTypeDefinition(type.Generics[0], sb);
            sb.Append('*');
        }
        else
        {
            sb.Append(type.Name);
            if (type.Generics.Any())
            {
                sb.Append('<');
                PrintGenericTypes(type, sb);
                sb.Append('>');
            }
        }
    }

    private static void PrintGenericTypes(TypeDefinition type, StringBuilder sb)
    {
        for (var i = 0; i < type.Generics.Count - 1; i++)
        {
            PrintTypeDefinition(type.Generics[i], sb);
            sb.Append(", ");
        }
        PrintTypeDefinition(type.Generics[^1], sb);
    }

    public static string PrintOperator(Operator op)
    {
        return op switch
        {
            Operator.And => "&&",
            Operator.Or => "||",
            Operator.Equality => "==",
            Operator.NotEqual => "!=",
            Operator.GreaterThanEqual => ">=",
            Operator.LessThanEqual => "<=",
            Operator.ShiftLeft => "<<",
            Operator.ShiftRight => ">>",
            Operator.RotateLeft => "<<<",
            Operator.RotateRight => ">>>",
            Operator.Subscript => "[]",
            _ => ((char)op).ToString()
        };
    }
}
