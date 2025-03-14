using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ol;

public static class Parser
{
    private static string _libraryDirectory;

    public static readonly ConcurrentQueue<IAst> Asts = new();
    public static readonly ConcurrentQueue<CompilerDirectiveAst> Directives = new();
    public static readonly ConcurrentQueue<CompilerDirectiveAst> RunDirectives = new();

    private class TokenEnumerator
    {
        private readonly List<Token> _tokens;
        private int _index;

        public TokenEnumerator(int fileIndex, List<Token> tokens)
        {
            FileIndex = fileIndex;
            _tokens = tokens;
        }

        public readonly int FileIndex;
        public Token Current;
        public bool Remaining = true;
        public bool Private;

        public bool MoveNext()
        {
            if (_tokens.Count > _index)
            {
                Current = _tokens[_index++];
                return true;
            }
            return Remaining = false;
        }

        public bool Move(int steps)
        {
            _index += steps;
            if (_tokens.Count > _index)
            {
                Current = _tokens[_index];
                return true;
            }
            return false;
        }

        public bool Peek(out Token token, int steps = 0)
        {
            if (_tokens.Count > _index + steps)
            {
                token = _tokens[_index + steps];
                return true;
            }
            token = Last;
            return false;
        }

        public void Insert(Token token)
        {
            _tokens.Insert(_index, token);
        }

        public Token Last => _tokens[^1];
    }

    public static void Parse(string entrypoint)
    {
        #if DEBUG
        _libraryDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../", "Modules"));
        #else
        _libraryDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
        #endif
        AddModule("runtime");

        #if _LINUX
        AddModule("linux");
        #elif _WINDOWS
        AddModule("windows");
        #endif

        QueueFileIfNotExists(entrypoint);

        ThreadPool.CompleteWork(ThreadPool.ParseQueue);
    }

    private static void AddModule(string module)
    {
        var file = Path.Combine(_libraryDirectory, $"{module}.ol");
        if (File.Exists(file))
        {
            QueueFileIfNotExists(file);
        }
    }

    private static void AddModule(string module, int fileIndex, Token token)
    {
        var file = Path.Combine(_libraryDirectory, $"{module}.ol");
        if (File.Exists(file))
        {
            QueueFileIfNotExists(file);
        }
        else
        {
            ErrorReporter.Report($"Undefined module '{module}'", fileIndex, token);
        }
    }

    public static void AddModule(CompilerDirectiveAst module)
    {
        if (File.Exists(module.Import.Path))
        {
            QueueFileIfNotExists(module.Import.Path);
        }
        else
        {
            ErrorReporter.Report($"Undefined module '{module.Import.Name}'", module);
        }
    }

    private static bool AddFile(string file, string directory, int fileIndex, uint line, uint column)
    {
        var filePath = Path.Combine(directory, file);
        if (File.Exists(filePath))
        {
            QueueFileIfNotExists(filePath);
            return true;
        }

        ErrorReporter.Report($"File '{file}' does not exist", fileIndex, line, column);
        return false;
    }

    private static void AddFile(string file, string directory, int fileIndex, Token token)
    {
        AddFile(file, directory, fileIndex, token.Line, token.Column);
    }

    public static bool AddFile(string file, int fileIndex, uint line, uint column)
    {
        return AddFile(file, BuildSettings.Path, fileIndex, line, column);
    }

    public static void AddFile(CompilerDirectiveAst import)
    {
        if (File.Exists(import.Import.Path))
        {
            QueueFileIfNotExists(import.Import.Path);
        }
        else
        {
            ErrorReporter.Report($"File '{import.Import.Name}' does not exist", import);
        }
    }

    private static void QueueFileIfNotExists(string file)
    {
        for (var i = 0; i < BuildSettings.Files.Count; i++)
        {
            if (BuildSettings.Files[i] == file)
            {
                return;
            }
        }

        var fileIndex = BuildSettings.AddFile(file);
        ThreadPool.QueueWork(ThreadPool.ParseQueue, ParseFile, new ParseData {File = file, FileIndex = fileIndex});
    }

    private class ParseData
    {
        public string File;
        public int FileIndex;
    }

    private static void ParseFile(object data)
    {
        var parseData = (ParseData)data;

        // 1. Load file tokens
        var fileIndex = parseData.FileIndex;
        var tokens = Lexer.LoadFileTokens(parseData.File, fileIndex);
        var directory = Path.GetDirectoryName(parseData.File);

        // 2. Iterate through tokens, tracking different ASTs
        ParseGlobalAsts(fileIndex, tokens, directory);
    }

    public static int ParseInsertedCode(string code, ScopeAst scope, int fileIndex, uint line, int index)
    {
        var tokens = new List<Token>(code.Length / 5);
        Lexer.ParseTokens(code, fileIndex, tokens, line);

        var inserted = 0;
        var enumerator = new TokenEnumerator(fileIndex, tokens);
        while (enumerator.MoveNext())
        {
            scope.Children.Insert(index++, ParseAst(enumerator, scope, null));
            inserted++;
        }

        return inserted;
    }

    public static void ParseCode(string code, int fileIndex, uint line)
    {
        var tokens = new List<Token>(code.Length / 5);
        Lexer.ParseTokens(code, fileIndex, tokens, line);

        ParseGlobalAsts(fileIndex, tokens, BuildSettings.ObjectDirectory);
    }

    private static void ParseGlobalAsts(int fileIndex, List<Token> tokens, string directory)
    {
        var enumerator = new TokenEnumerator(fileIndex, tokens);
        while (enumerator.MoveNext())
        {
            var attributes = ParseAttributes(enumerator);

            var token = enumerator.Current;
            switch (token.Type)
            {
                case TokenType.Identifier:
                    if (enumerator.Peek(out token))
                    {
                        if (token.Type == TokenType.Colon)
                        {
                            if (attributes != null)
                            {
                                ErrorReporter.Report("Global variables cannot have attributes", fileIndex, token);
                            }
                            var variable = ParseDeclaration(enumerator, global: true);
                            if (TypeChecker.AddGlobalVariable(variable))
                            {
                                Asts.Enqueue(variable);
                            }
                        }
                        else
                        {
                            var function = ParseFunction(enumerator, attributes);
                            if (function?.Name != null)
                            {
                                TypeChecker.AddFunction(function);
                                Asts.Enqueue(function);
                            }
                        }
                        break;
                    }
                    ErrorReporter.Report($"Unexpected token '{token.Value}'", fileIndex, token);
                    break;
                case TokenType.Struct:
                    var structAst = ParseStruct(enumerator, attributes);
                    if (structAst != null)
                    {
                        if (structAst.Generics != null)
                        {
                            if (TypeChecker.AddPolymorphicStruct(structAst) && structAst.Name == "Array")
                            {
                                TypeChecker.BaseArrayType = structAst;
                            }
                        }
                        else
                        {
                            if (TypeChecker.AddStruct(structAst))
                            {
                                if (structAst.Name == "string")
                                {
                                    TypeTable.StringType = structAst;
                                    structAst.TypeKind = TypeKind.String;
                                    structAst.Used = true;
                                }
                                else if (structAst.Name == "Any")
                                {
                                    TypeTable.AnyType = structAst;
                                    structAst.TypeKind = TypeKind.Any;
                                    structAst.Used = true;
                                }
                                else
                                {
                                    structAst.TypeKind = TypeKind.Struct;
                                    Asts.Enqueue(structAst);
                                }
                            }
                        }
                    }
                    break;
                case TokenType.Enum:
                    var enumAst = ParseEnum(enumerator, attributes);
                    if (enumAst != null)
                    {
                        TypeChecker.VerifyEnum(enumAst);
                    }
                    break;
                case TokenType.Union:
                    if (attributes != null)
                    {
                        ErrorReporter.Report("Unions cannot have attributes", fileIndex, token);
                    }
                    var union = ParseUnion(enumerator);
                    TypeChecker.AddUnion(union);
                    Asts.Enqueue(union);
                    break;
                case TokenType.Pound:
                    if (attributes != null)
                    {
                        ErrorReporter.Report("Compiler directives cannot have attributes", fileIndex, token);
                    }
                    var directive = ParseTopLevelDirective(enumerator, directory, true);
                    if (directive != null)
                    {
                        switch (directive.Type)
                        {
                            case DirectiveType.Library:
                                TypeChecker.AddLibrary(directive);
                                break;
                            case DirectiveType.SystemLibrary:
                                TypeChecker.AddSystemLibrary(directive);
                                break;
                            case DirectiveType.Run:
                                RunDirectives.Enqueue(directive);
                                break;
                            case DirectiveType.CopyToOutputDirectory:
                                TypeChecker.CopyFileToOutputDirectory(directive);
                                break;
                            default:
                                Directives.Enqueue(directive);
                                break;
                        }
                    }
                    break;
                case TokenType.Operator:
                    if (attributes != null)
                    {
                        ErrorReporter.Report($"Operator overloads cannot have attributes", fileIndex, token);
                    }
                    var overload = ParseOperatorOverload(enumerator);
                    TypeChecker.AddOverload(overload);
                    Asts.Enqueue(overload);
                    break;
                case TokenType.Interface:
                    if (attributes != null)
                    {
                        ErrorReporter.Report($"Interfaces cannot have attributes", fileIndex, token);
                    }
                    var interfaceAst = ParseInterface(enumerator);
                    if (interfaceAst?.Name != null)
                    {
                        if (interfaceAst.Generics.Any())
                        {
                            TypeChecker.AddPolymorphicInterface(interfaceAst);
                        }
                        else
                        {
                            TypeChecker.AddInterface(interfaceAst);
                            Asts.Enqueue(interfaceAst);
                        }
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}'", fileIndex, token);
                    break;
            }
        }
    }

    private static IAst ParseTopLevelAst(TokenEnumerator enumerator, string directory)
    {
        var attributes = ParseAttributes(enumerator);

        var token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Identifier:
                if (enumerator.Peek(out token))
                {
                    if (token.Type == TokenType.Colon)
                    {
                        if (attributes != null)
                        {
                            ErrorReporter.Report($"Global variables cannot have attributes", enumerator.FileIndex, token);
                        }
                        return ParseDeclaration(enumerator, global: true);
                    }
                    return ParseFunction(enumerator, attributes);
                }
                ErrorReporter.Report($"Unexpected token '{token.Value}'", enumerator.FileIndex, token);
                return null;
            case TokenType.Struct:
                return ParseStruct(enumerator, attributes);
            case TokenType.Enum:
                return ParseEnum(enumerator, attributes);
            case TokenType.Union:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Compiler directives cannot have attributes", enumerator.FileIndex, token);
                }
                return ParseUnion(enumerator);
            case TokenType.Pound:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Compiler directives cannot have attributes", enumerator.FileIndex, token);
                }
                return ParseTopLevelDirective(enumerator, directory);
            case TokenType.Operator:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Operator overloads cannot have attributes", enumerator.FileIndex, token);
                }
                return ParseOperatorOverload(enumerator);
            case TokenType.Interface:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Interfaces cannot have attributes", enumerator.FileIndex, token);
                }
                return ParseInterface(enumerator);
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}'", enumerator.FileIndex, token);
                return null;
        }
    }

    private static List<string> ParseAttributes(TokenEnumerator enumerator)
    {
        if (enumerator.Current.Type != TokenType.OpenBracket)
        {
            return null;
        }

        var attributes = new List<string>();
        var commaRequired = false;
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;
            if (token.Type == TokenType.CloseBracket)
            {
                break;
            }

            switch (token.Type)
            {
                case TokenType.Identifier:
                    if (commaRequired)
                    {
                        ErrorReporter.Report("Expected comma between attributes", enumerator.FileIndex, token);
                    }
                    attributes.Add(token.Value);
                    commaRequired = true;
                    break;
                case TokenType.Comma:
                    if (!commaRequired)
                    {
                        ErrorReporter.Report("Expected attribute after comma or at beginning of attribute list", enumerator.FileIndex, token);
                    }
                    commaRequired = false;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in attribute list", enumerator.FileIndex, token);
                    break;
            }
        }

        if (attributes.Count == 0)
        {
            ErrorReporter.Report("Expected attribute(s) to be in attribute list", enumerator.FileIndex, enumerator.Current);
        }
        else if (!commaRequired)
        {
            ErrorReporter.Report("Expected attribute after comma in attribute list", enumerator.FileIndex, enumerator.Current);
        }
        enumerator.MoveNext();

        return attributes;
    }

    private static FunctionAst ParseFunction(TokenEnumerator enumerator, List<string> attributes)
    {
        // 1. Determine return type and name of the function
        var function = CreateAst<FunctionAst>(enumerator);
        function.Attributes = attributes;
        function.Private = enumerator.Private;

        // 1a. Check if the return type is void
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report($"Expected function definition", enumerator.FileIndex, token);
            return null;
        }

        if (token.Type != TokenType.OpenParen)
        {
            function.ReturnTypeDefinition = ParseType(enumerator);
            enumerator.MoveNext();
        }

        // 1b. Handle multiple return values
        if (enumerator.Current.Type == TokenType.Comma)
        {
            var returnType = CreateAst<TypeDefinition>(function.ReturnTypeDefinition);
            returnType.Compound = true;
            returnType.Generics.Add(function.ReturnTypeDefinition);
            function.ReturnTypeDefinition = returnType;

            while (enumerator.Current.Type == TokenType.Comma)
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }
                returnType.Generics.Add(ParseType(enumerator));
                enumerator.MoveNext();
            }
        }

        // 1b. Set the name of the function or get the name from the type
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected the function name to be declared", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        switch (enumerator.Current.Type)
        {
            case TokenType.Identifier:
                function.Name = enumerator.Current.Value;
                enumerator.MoveNext();
                break;
            case TokenType.OpenParen:
                if (function.ReturnTypeDefinition.Name == "*" || function.ReturnTypeDefinition.Count != null)
                {
                    ErrorReporter.Report("Expected the function name to be declared", function.ReturnTypeDefinition);
                }
                else
                {
                    function.Name = function.ReturnTypeDefinition.Name;
                    foreach (var generic in function.ReturnTypeDefinition.Generics)
                    {
                        if (generic.Generics.Any())
                        {
                            ErrorReporter.Report($"Invalid generic in function '{function.Name}'", generic);
                        }
                        else if (function.Generics.Contains(generic.Name))
                        {
                            ErrorReporter.Report($"Duplicate generic '{generic.Name}' in function '{function.Name}'", generic.FileIndex, generic.Line, generic.Column);
                        }
                        else
                        {
                            function.Generics.Add(generic.Name);
                        }
                    }
                    function.ReturnTypeDefinition = null;
                }
                break;
            default:
                ErrorReporter.Report("Expected the function name to be declared", enumerator.FileIndex, enumerator.Current);
                enumerator.MoveNext();
                break;
        }

        // 2. Parse generics
        if (enumerator.Current.Type == TokenType.LessThan)
        {
            ParseGenerics(enumerator, function.Generics, function.Name, "function");
            enumerator.MoveNext();

            // Search for generics in the function return type
            if (function.ReturnTypeDefinition != null)
            {
                for (var i = 0; i < function.Generics.Count; i++)
                {
                    var generic = function.Generics[i];
                    if (SearchForGeneric(generic, i, function.ReturnTypeDefinition))
                    {
                        function.Flags |= FunctionFlags.ReturnTypeHasGenerics;
                    }
                }
            }
        }

        // 3. Find open paren to start parsing arguments
        if (enumerator.Current.Type != TokenType.OpenParen)
        {
            // Add an error to the function AST and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in function definition", enumerator.FileIndex, token);
            while (enumerator.Remaining && enumerator.Current.Type != TokenType.OpenParen)
                enumerator.MoveNext();
        }

        // 4. Parse arguments until a close paren
        var commaRequiredBeforeNextArgument = false;
        DeclarationAst currentArgument = null;
        while (enumerator.MoveNext())
        {
            token = enumerator.Current;

            if (token.Type == TokenType.CloseParen)
            {
                if (commaRequiredBeforeNextArgument)
                {
                    function.Arguments.Add(currentArgument);
                    currentArgument = null;
                }
                break;
            }

            switch (token.Type)
            {
                case TokenType.Identifier:
                case TokenType.VarArgs:
                    if (commaRequiredBeforeNextArgument)
                    {
                        ErrorReporter.Report("Comma required after declaring an argument", enumerator.FileIndex, token);
                    }
                    else if (currentArgument == null)
                    {
                        currentArgument = CreateAst<DeclarationAst>(token, enumerator.FileIndex);
                        currentArgument.TypeDefinition = ParseType(enumerator, argument: true);
                        for (var i = 0; i < function.Generics.Count; i++)
                        {
                            var generic = function.Generics[i];
                            if (SearchForGeneric(generic, i, currentArgument.TypeDefinition))
                            {
                                currentArgument.HasGenerics = true;
                            }
                        }
                    }
                    else
                    {
                        currentArgument.Name = token.Value;
                        commaRequiredBeforeNextArgument = true;
                    }
                    break;
                case TokenType.Comma:
                    if (commaRequiredBeforeNextArgument)
                    {
                        function.Arguments.Add(currentArgument);
                    }
                    else
                    {
                        ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, token);
                    }
                    currentArgument = null;
                    commaRequiredBeforeNextArgument = false;
                    break;
                case TokenType.Equals:
                    if (commaRequiredBeforeNextArgument)
                    {
                        enumerator.MoveNext();
                        currentArgument.Value = ParseExpression(enumerator, function, null, TokenType.Comma, TokenType.CloseParen);
                        if (!enumerator.Remaining)
                        {
                            ErrorReporter.Report($"Incomplete definition for function '{function.Name}'", enumerator.FileIndex, enumerator.Last);
                            return null;
                        }

                        switch (enumerator.Current.Type)
                        {
                            case TokenType.Comma:
                                commaRequiredBeforeNextArgument = false;
                                function.Arguments.Add(currentArgument);
                                currentArgument = null;
                                break;
                            case TokenType.CloseParen:
                                function.Arguments.Add(currentArgument);
                                currentArgument = null;
                                break;
                            default:
                                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in arguments of function '{function.Name}'", enumerator.FileIndex, enumerator.Current);
                                break;
                        }
                    }
                    else
                    {
                        ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, token);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in arguments", enumerator.FileIndex, token);
                    break;
            }

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                break;
            }
        }

        if (currentArgument != null)
        {
            ErrorReporter.Report($"Incomplete function argument in function '{function.Name}'", enumerator.FileIndex, enumerator.Current);
        }

        if (!commaRequiredBeforeNextArgument && function.Arguments.Any())
        {
            ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, enumerator.Current);
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Unexpected function body or compiler directive", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        // 5. Handle compiler directives
        while (enumerator.Current.Type == TokenType.Pound)
        {
            if (!enumerator.MoveNext())
            {
                ErrorReporter.Report("Expected compiler directive value", enumerator.FileIndex, enumerator.Last);
                return null;
            }

            switch (enumerator.Current.Value)
            {
                case "extern":
                    function.Flags |= FunctionFlags.Extern;
                    const string externError = "Extern function definition should be followed by the library in use";
                    if (!enumerator.Peek(out token))
                    {
                        ErrorReporter.Report(externError, enumerator.FileIndex, token);
                    }
                    else if (token.Type == TokenType.Literal)
                    {
                        enumerator.MoveNext();
                        function.ExternLib = token.Value;
                    }
                    else if (token.Type == TokenType.Identifier)
                    {
                        enumerator.MoveNext();
                        function.LibraryName = token.Value;
                    }
                    else
                    {
                        ErrorReporter.Report(externError, enumerator.FileIndex, token);
                    }
                    return function;
                case "compiler":
                    function.Flags |= FunctionFlags.Compiler;
                    return function;
                case "syscall":
                    function.Flags |= FunctionFlags.Syscall;
                    const string syscallError = "Syscall function definition should be followed by the number for the system call";
                    if (!enumerator.Peek(out token))
                    {
                        ErrorReporter.Report(syscallError, enumerator.FileIndex, token);
                    }
                    else if (token.Type == TokenType.Number)
                    {
                        enumerator.MoveNext();
                        if (token.Flags == TokenFlags.None && int.TryParse(token.Value, out var value))
                        {
                            function.Syscall = value;
                        }
                        else
                        {
                            ErrorReporter.Report(syscallError, enumerator.FileIndex, token);
                        }
                    }
                    else
                    {
                        ErrorReporter.Report(syscallError, enumerator.FileIndex, token);
                    }
                    return function;
                case "print_ir":
                    function.Flags |= FunctionFlags.PrintIR;
                    break;
                case "call_location":
                    function.Flags |= FunctionFlags.PassCallLocation;
                    break;
                case "inline":
                    function.Flags |= FunctionFlags.Inline;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    break;
            }
            enumerator.MoveNext();
        }

        // 6. Find open brace to start parsing body
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            // Add an error and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in function definition", enumerator.FileIndex, token);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);

            if (!enumerator.Remaining)
            {
                return function;
            }
        }

        // 7. Parse function body
        function.Body = ParseScope(enumerator, function);

        return function;
    }

    private static StructAst ParseStruct(TokenEnumerator enumerator, List<string> attributes)
    {
        var structAst = CreateAst<StructAst>(enumerator);
        structAst.Attributes = attributes;
        structAst.Private = enumerator.Private;

        // 1. Determine name of struct
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected struct to have name", enumerator.FileIndex, enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Type)
        {
            case TokenType.Identifier:
                structAst.Name = enumerator.Current.Value;
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in struct definition", enumerator.FileIndex, enumerator.Current);
                break;
        }

        // 2. Parse struct generics
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report($"Unexpected struct body", enumerator.FileIndex, token);
            return null;
        }

        if (token.Type == TokenType.LessThan)
        {
            // Clear the '<' before entering loop
            enumerator.MoveNext();
            var generics = structAst.Generics = new();
            ParseGenerics(enumerator, generics, structAst.Name, "struct");
        }

        // 3. Get any inherited structs
        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Colon)
        {
            enumerator.MoveNext();
            structAst.BaseTypeDefinition = ParseType(enumerator);
            enumerator.MoveNext();
        }

        // 4. Parse over the open brace
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected '{' token in struct definition", enumerator.FileIndex, enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 5. Iterate through fields
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Type == TokenType.CloseBrace)
            {
                break;
            }

            var field = ParseStructField(enumerator, out var closed);
            if (closed) break;

            structAst.Fields.Add(field);
        }

        // 6. Mark field types as generic if necessary
        if (structAst.Generics != null)
        {
            for (var i = 0; i < structAst.Generics.Count; i++)
            {
                var generic = structAst.Generics[i];
                foreach (var field in structAst.Fields)
                {
                    if (field.TypeDefinition != null && SearchForGeneric(generic, i, field.TypeDefinition))
                    {
                        field.HasGenerics = true;
                    }
                }
            }
        }

        return structAst;
    }

    private static StructFieldAst ParseStructField(TokenEnumerator enumerator, out bool closed)
    {
        closed = false;
        var attributes = ParseAttributes(enumerator);
        var structField = CreateAst<StructFieldAst>(enumerator);
        structField.Attributes = attributes;

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Expected name of struct field, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
        }
        structField.Name = enumerator.Current.Value;

        // 1. Expect to get colon
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.Colon)
        {
            var errorToken = enumerator.Current;
            ErrorReporter.Report($"Unexpected token in struct field '{errorToken.Value}'", enumerator.FileIndex, errorToken);
            // Parse to a ; or }
            do
            {
                var tokenType = enumerator.Current.Type;
                if (tokenType == TokenType.SemiColon)
                {
                    break;
                }
                if (tokenType == TokenType.CloseBrace)
                {
                    closed = true;
                    break;
                }
            } while (enumerator.MoveNext());
            return structField;
        }

        // 2. Check if type is given
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Expected type of struct field", enumerator.FileIndex, token);
            return null;
        }

        if (token.Type == TokenType.Identifier)
        {
            enumerator.MoveNext();
            structField.TypeDefinition = ParseType(enumerator, null);
        }

        // 3. Get the value or return
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected declaration to have value", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Equals:
                ParseValue(structField, enumerator, null);
                break;
            case TokenType.SemiColon:
                if (structField.TypeDefinition == null)
                {
                    ErrorReporter.Report("Expected struct field to have value", enumerator.FileIndex, token);
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in struct field", enumerator.FileIndex, token);
                // Parse until there is an equals sign or semicolon
                do
                {
                    var tokenType = enumerator.Current.Type;
                    if (tokenType == TokenType.SemiColon) break;
                    if (tokenType == TokenType.CloseBrace)
                    {
                        closed = true;
                        break;
                    }
                    if (tokenType == TokenType.Equals)
                    {
                        ParseValue(structField, enumerator, null);
                        break;
                    }
                } while (enumerator.MoveNext());
                break;
        }

        return structField;
    }

    private static EnumAst ParseEnum(TokenEnumerator enumerator, List<string> attributes)
    {
        var enumAst = CreateAst<EnumAst>(enumerator);
        enumAst.Attributes = attributes;
        enumAst.Private = enumerator.Private;

        // 1. Determine name of enum
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected enum to have name", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        if (enumerator.Current.Type == TokenType.Identifier)
        {
            enumAst.Name = enumerator.Current.Value;
        }
        else
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in enum definition", enumerator.FileIndex, enumerator.Current);
        }

        // 2. Parse over the open brace
        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Colon)
        {
            enumerator.MoveNext();
            enumAst.BaseTypeDefinition = ParseType(enumerator);
            enumerator.MoveNext();

            var baseType = TypeChecker.VerifyType(enumAst.BaseTypeDefinition, TypeChecker.GlobalScope);
            if (baseType?.TypeKind != TypeKind.Integer)
            {
                ErrorReporter.Report($"Base type of enum must be an integer, but got '{TypeChecker.PrintTypeDefinition(enumAst.BaseTypeDefinition)}'", enumAst.BaseTypeDefinition);
                enumAst.BaseType = TypeTable.S32Type;
            }
            else
            {
                enumAst.BaseType = (PrimitiveAst)baseType;
                enumAst.Alignment = enumAst.Size = enumAst.BaseType.Size;
            }
        }
        else
        {
            enumAst.BaseType = TypeTable.S32Type;
        }

        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected '{' token in enum definition", enumerator.FileIndex, enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 3. Iterate through fields
        var lowestAllowedValue = enumAst.BaseType.Signed ? -Math.Pow(2, 8 * enumAst.Size - 1) : 0;
        var largestAllowedValue = enumAst.BaseType.Signed ? Math.Pow(2, 8 * enumAst.Size - 1) - 1 : Math.Pow(2, 8 * enumAst.Size) - 1;
        long largestValue = -1;
        var enumIndex = 0;

        EnumValueAst currentValue = null;
        var parsingValueDefault = false;
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;

            if (token.Type == TokenType.CloseBrace)
            {
                break;
            }

            switch (token.Type)
            {
                case TokenType.Identifier:
                    if (currentValue == null)
                    {
                        currentValue = CreateAst<EnumValueAst>(token, enumerator.FileIndex);
                        currentValue.Index = enumIndex++;
                        currentValue.Name = token.Value;
                    }
                    else if (parsingValueDefault)
                    {
                        parsingValueDefault = false;
                        if (enumAst.Values.TryGetValue(token.Value, out var value))
                        {
                            if (!value.Defined)
                            {
                                ErrorReporter.Report($"Expected previously defined value '{token.Value}' to have a defined value", enumerator.FileIndex, token);
                            }
                            currentValue.Value = value.Value;
                            currentValue.Defined = true;
                        }
                        else
                        {
                            ErrorReporter.Report($"Expected value '{token.Value}' to be previously defined in enum '{enumAst.Name}'", enumerator.FileIndex, token);
                        }
                    }
                    else
                    {
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
                    }
                    break;
                case TokenType.SemiColon:
                    if (currentValue != null)
                    {
                        // Catch if the name hasn't been set
                        if (currentValue.Name == null || parsingValueDefault)
                        {
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
                        }
                        // Add the value to the enum and continue
                        else
                        {
                            if (!enumAst.Values.TryAdd(currentValue.Name, currentValue))
                            {
                                ErrorReporter.Report($"Enum '{enumAst.Name}' already contains value '{currentValue.Name}'", currentValue);
                            }

                            if (currentValue.Defined)
                            {
                                if (currentValue.Value > largestValue)
                                {
                                    largestValue = currentValue.Value;
                                }
                            }
                            else
                            {
                                currentValue.Value = ++largestValue;
                            }

                            if (currentValue.Value < lowestAllowedValue || currentValue.Value > largestAllowedValue)
                            {
                                ErrorReporter.Report($"Enum value '{enumAst.Name}.{currentValue.Name}' value '{currentValue.Value}' is out of range", currentValue);
                            }
                        }

                        currentValue = null;
                        parsingValueDefault = false;
                    }
                    break;
                case TokenType.Equals:
                    if (currentValue?.Name != null)
                    {
                        parsingValueDefault = true;
                    }
                    else
                    {
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
                    }
                    break;
                case TokenType.Number:
                    if (currentValue != null && parsingValueDefault)
                    {
                        if (token.Flags == TokenFlags.None)
                        {
                            if (int.TryParse(token.Value, out var value))
                            {
                                currentValue.Value = value;
                                currentValue.Defined = true;
                            }
                            else
                            {
                                ErrorReporter.Report($"Expected enum value to be an integer, but got '{token.Value}'", enumerator.FileIndex, token);
                            }
                        }
                        else if (token.Flags.HasFlag(TokenFlags.Float))
                        {
                            ErrorReporter.Report($"Expected enum value to be an integer, but got '{token.Value}'", enumerator.FileIndex, token);
                        }
                        else if (token.Flags.HasFlag(TokenFlags.HexNumber))
                        {
                            if (token.Value.Length == 2)
                            {
                                ErrorReporter.Report($"Invalid number '{token.Value}'", enumerator.FileIndex, token);
                            }

                            var value = token.Value.Substring(2);
                            if (value.Length <= 8)
                            {
                                if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u32))
                                {
                                    currentValue.Value = (long)u32;
                                    currentValue.Defined = true;
                                }
                            }
                            else if (value.Length <= 16)
                            {
                                if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u64))
                                {
                                    currentValue.Value = (long)u64;
                                    currentValue.Defined = true;
                                }
                            }
                            else
                            {
                                ErrorReporter.Report($"Expected enum value to be an integer, but got '{token.Value}'", enumerator.FileIndex, token);
                            }
                        }
                        parsingValueDefault = false;
                    }
                    else
                    {
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
                    }
                    break;
                case TokenType.Character:
                    if (currentValue != null && parsingValueDefault)
                    {
                        currentValue.Value = token.Value[0];
                        currentValue.Defined = true;
                        parsingValueDefault = false;
                    }
                    else
                    {
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
                    break;
            }
        }

        if (currentValue != null)
        {
            var token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", enumerator.FileIndex, token);
        }

        if (enumAst.Values.Count == 0)
        {
            ErrorReporter.Report("Expected enum to have 1 or more values", enumAst);
        }

        return enumAst;
    }

    private static UnionAst ParseUnion(TokenEnumerator enumerator)
    {
        var union = CreateAst<UnionAst>(enumerator);
        union.Private = enumerator.Private;

        // 1. Determine name of enum
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected union to have name", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        if (enumerator.Current.Type == TokenType.Identifier)
        {
            union.Name = enumerator.Current.Value;
        }
        else
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in union definition", enumerator.FileIndex, enumerator.Current);
        }

        // 2. Parse over the open brace
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected '{' token in union definition", enumerator.FileIndex, enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 3. Iterate through fields
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;

            if (token.Type == TokenType.CloseBrace)
            {
                break;
            }

            var field = ParseUnionField(enumerator, out var closed);
            if (closed) break;

            union.Fields.Add(field);
        }

        return union;
    }

    private static UnionFieldAst ParseUnionField(TokenEnumerator enumerator, out bool closed)
    {
        closed = false;
        var field = CreateAst<UnionFieldAst>(enumerator);

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Expected name of union field, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
        }
        field.Name = enumerator.Current.Value;

        // 1. Expect to get colon
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.Colon)
        {
            var errorToken = enumerator.Current;
            ErrorReporter.Report($"Unexpected token in union field '{errorToken.Value}'", enumerator.FileIndex, errorToken);
            // Parse to a ; or }
            do
            {
                var tokenType = enumerator.Current.Type;
                if (tokenType == TokenType.SemiColon)
                {
                    break;
                }
                if (tokenType == TokenType.CloseBrace)
                {
                    closed = true;
                    break;
                }
            } while (enumerator.MoveNext());
            return field;
        }

        // 2. Check if type is given
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Unexpected type of union field", enumerator.FileIndex, token);
            return null;
        }

        if (token.Type == TokenType.Identifier)
        {
            enumerator.MoveNext();
            field.TypeDefinition = ParseType(enumerator, null);
        }

        // 3. Get the value or return
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected union to be closed by a '}'", enumerator.FileIndex, token);
            return null;
        }

        token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.SemiColon:
                if (field.TypeDefinition == null)
                {
                    ErrorReporter.Report("Expected union field to have a type", enumerator.FileIndex, token);
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in declaration", enumerator.FileIndex, token);
                // Parse until there is a semicolon or closing brace
                do
                {
                    token = enumerator.Current;
                    if (token.Type == TokenType.SemiColon)
                    {
                        break;
                    }
                    if (token.Type == TokenType.CloseBrace)
                    {
                        closed = true;
                        break;
                    }
                }
                while (enumerator.MoveNext());
                break;
        }

        return field;
    }

    private static bool SearchForGeneric(string generic, int index, TypeDefinition type)
    {
        if (type.Name == generic)
        {
            type.IsGeneric = true;
            type.GenericIndex = index;
            return true;
        }

        var hasGeneric = false;
        foreach (var typeGeneric in type.Generics)
        {
            if (SearchForGeneric(generic, index, typeGeneric))
            {
                hasGeneric = true;
            }
        }
        return hasGeneric;
    }

    private static void ParseLine(TokenEnumerator enumerator, ScopeAst scope, IFunction currentFunction, bool inDefer = false)
    {
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("End of file reached without closing scope", enumerator.FileIndex, enumerator.Last);
            return;
        }

        scope.Children.Add(ParseAst(enumerator, scope, currentFunction, inDefer));
    }

    private static IAst ParseAst(TokenEnumerator enumerator, ScopeAst scope, IFunction currentFunction, bool inDefer = false)
    {
        var token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Return:
                if (inDefer)
                {
                    ErrorReporter.Report("Cannot defer a return statement", enumerator.FileIndex, token);
                }
                return ParseReturn(enumerator, currentFunction);
            case TokenType.If:
                return ParseConditional(enumerator, currentFunction);
            case TokenType.While:
                return ParseWhile(enumerator, currentFunction);
            case TokenType.Each:
                return ParseEach(enumerator, currentFunction);
            case TokenType.Identifier:
                if (!enumerator.Peek(out token))
                {
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.FileIndex, enumerator.Last);
                    return null;
                }
                switch (token.Type)
                {
                    case TokenType.OpenParen:
                        return ParseCall(enumerator, currentFunction, true);
                    case TokenType.Colon:
                        return ParseDeclaration(enumerator, currentFunction);
                    case TokenType.Equals:
                        return ParseAssignment(enumerator, currentFunction, out _);
                    default:
                        return ParseExpression(enumerator, currentFunction);
                }
            case TokenType.Increment:
            case TokenType.Decrement:
            case TokenType.Asterisk:
                return ParseExpression(enumerator, currentFunction);
            case TokenType.OpenBrace:
                return ParseScope(enumerator, currentFunction);
            case TokenType.Pound:
                return ParseCompilerDirective(enumerator, currentFunction);
            case TokenType.Asm:
                return ParseInlineAssembly(enumerator);
            case TokenType.Switch:
                return ParseSwitch(enumerator, currentFunction);
            case TokenType.Defer:
                var deferAst = CreateAst<DeferAst>(token, enumerator.FileIndex);
                if (inDefer)
                {
                    ErrorReporter.Report("Unable to nest defer statements", enumerator.FileIndex, token);
                }
                else if (enumerator.MoveNext())
                {
                    deferAst.Statement = ParseScopeBody(enumerator, currentFunction, inDefer: true);
                }
                else
                {
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.FileIndex, enumerator.Last);
                }
                return deferAst;
            case TokenType.Break:
                var breakAst = CreateAst<BreakAst>(token, enumerator.FileIndex);
                if (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type != TokenType.SemiColon)
                    {
                        ErrorReporter.Report("Expected ';'", enumerator.FileIndex, enumerator.Current);
                    }
                }
                else
                {
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.FileIndex, enumerator.Last);
                }
                return breakAst;
            case TokenType.Continue:
                var continueAst = CreateAst<ContinueAst>(token, enumerator.FileIndex);
                if (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type != TokenType.SemiColon)
                    {
                        ErrorReporter.Report("Expected ';'", enumerator.FileIndex, enumerator.Current);
                    }
                }
                else
                {
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.FileIndex, enumerator.Last);
                }
                return continueAst;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}'", enumerator.FileIndex, token);
                return null;
        }
    }

    private static ScopeAst ParseScopeBody(TokenEnumerator enumerator, IFunction currentFunction, bool topLevel = false, string directory = null, bool inDefer = false)
    {
        if (enumerator.Current.Type == TokenType.OpenBrace)
        {
            // Parse until close brace
            return ParseScope(enumerator, currentFunction, topLevel, directory);
        }

        // Parse single AST
        var scope = CreateAst<ScopeAst>(enumerator);
        if (topLevel)
        {
            var ast = ParseTopLevelAst(enumerator, directory);
            if (ast != null)
            {
                scope.Children.Add(ast);
            }
        }
        else
        {
            ParseLine(enumerator, scope, currentFunction);
        }

        return scope;
    }

    private static ScopeAst ParseScope(TokenEnumerator enumerator, IFunction currentFunction, bool topLevel = false, string directory = null)
    {
        var scope = CreateAst<ScopeAst>(enumerator);

        var closed = false;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Type == TokenType.CloseBrace)
            {
                closed = true;
                break;
            }

            if (topLevel)
            {
                var ast = ParseTopLevelAst(enumerator, directory);
                if (ast != null)
                {
                    scope.Children.Add(ast);
                }
            }
            else
            {
                ParseLine(enumerator, scope, currentFunction);
            }
        }

        if (!closed)
        {
            ErrorReporter.Report("Scope not closed by '}'", enumerator.FileIndex, enumerator.Current);
        }

        return scope;
    }

    private static ConditionalAst ParseConditional(TokenEnumerator enumerator, IFunction currentFunction, bool topLevel = false, string directory = null)
    {
        var conditionalAst = CreateAst<ConditionalAst>(enumerator);

        // 1. Parse the conditional expression by first iterating over the initial 'if'
        enumerator.MoveNext();
        conditionalAst.Condition = ParseConditionExpression(enumerator, currentFunction);

        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected if to contain conditional expression and body", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        // 2. Determine how many lines to parse
        conditionalAst.IfBlock = ParseScopeBody(enumerator, currentFunction, topLevel, directory);

        // 3. Parse else block if necessary
        if (!enumerator.Peek(out var token))
        {
            return conditionalAst;
        }

        if (token.Type == TokenType.Else)
        {
            // First clear the else and then determine how to parse else block
            enumerator.MoveNext();
            if (!enumerator.MoveNext())
            {
                ErrorReporter.Report("Expected body of else branch", enumerator.FileIndex, enumerator.Last);
                return null;
            }

            conditionalAst.ElseBlock = ParseScopeBody(enumerator, currentFunction, topLevel, directory);
        }

        return conditionalAst;
    }

    private static WhileAst ParseWhile(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var whileAst = CreateAst<WhileAst>(enumerator);

        // 1. Parse the conditional expression by first iterating over the initial 'while'
        enumerator.MoveNext();
        whileAst.Condition = ParseConditionExpression(enumerator, currentFunction);

        // 2. Determine how many lines to parse
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected while loop to contain body", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        whileAst.Body = ParseScopeBody(enumerator, currentFunction);
        return whileAst;
    }

    private static EachAst ParseEach(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var eachAst = CreateAst<EachAst>(enumerator);

        // 1. Parse the iteration variable by first iterating over the initial 'each'
        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Identifier)
        {
            eachAst.IterationVariable = CreateAst<VariableAst>(enumerator);
            eachAst.IterationVariable.Name = enumerator.Current.Value;
        }
        else
        {
            ErrorReporter.Report("Expected variable in each block definition", enumerator.FileIndex, enumerator.Current);
        }

        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Comma)
        {
            enumerator.MoveNext();
            if (enumerator.Current.Type == TokenType.Identifier)
            {
                eachAst.IndexVariable = CreateAst<VariableAst>(enumerator);
                eachAst.IndexVariable.Name = enumerator.Current.Value;
                enumerator.MoveNext();
            }
            else
            {
                ErrorReporter.Report("Expected index variable after comma in each declaration", enumerator.FileIndex, enumerator.Current);
            }
        }

        // 2. Parse over the in keyword
        if (enumerator.Current.Type != TokenType.In)
        {
            ErrorReporter.Report("Expected 'in' token in each block", enumerator.FileIndex, enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.In);
        }

        // 3. Determine the iterator
        enumerator.MoveNext();
        var expression = ParseConditionExpression(enumerator, currentFunction);

        // 3a. Check if the next token is a range
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected each block to have iteration and body", enumerator.FileIndex, enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Type)
        {
            case TokenType.Range:
                if (eachAst.IndexVariable != null)
                {
                    ErrorReporter.Report("Range enumerators cannot have iteration and index variable", enumerator.FileIndex, enumerator.Current);
                }

                eachAst.RangeBegin = expression;
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report("Expected range to have an end", enumerator.FileIndex, enumerator.Last);
                    return eachAst;
                }

                eachAst.RangeEnd = ParseConditionExpression(enumerator, currentFunction);
                if (!enumerator.Remaining)
                {
                    ErrorReporter.Report("Expected each block to have iteration and body", enumerator.FileIndex, enumerator.Last);
                    return eachAst;
                }
                break;
            default:
                eachAst.Iteration = expression;
                break;
        }

        // 4. Determine how many lines to parse
        eachAst.Body = ParseScopeBody(enumerator, currentFunction);
        return eachAst;
    }

    private static IAst ParseConditionExpression(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var expression = CreateAst<ExpressionAst>(enumerator);
        var operatorRequired = false;

        do
        {
            var token = enumerator.Current;

            if (token.Type == TokenType.OpenBrace)
            {
                break;
            }

            if (operatorRequired)
            {
                if (token.Type == TokenType.Increment || token.Type == TokenType.Decrement)
                {
                    // Create subexpression to hold the operation
                    // This case would be `var b = 4 + a++`, where we have a value before the operator
                    var source = expression.Children[^1];
                    var changeByOneAst = CreateAst<ChangeByOneAst>(source);
                    changeByOneAst.Positive = token.Type == TokenType.Increment;
                    changeByOneAst.Value = source;
                    expression.Children[^1] = changeByOneAst;
                    continue;
                }
                if (token.Type == TokenType.Number && token.Value[0] == '-')
                {
                    token.Value = token.Value[1..];
                    expression.Operators.Add(Operator.Subtract);
                    var constant = ParseConstant(token, enumerator.FileIndex);
                    expression.Children.Add(constant);
                    continue;
                }
                if (token.Type == TokenType.Period)
                {
                    var structFieldRef = ParseStructFieldRef(enumerator, expression.Children[^1], currentFunction);
                    expression.Children[^1] = structFieldRef;
                    continue;
                }
                var op = ConvertOperator(token);
                if (op != Operator.None)
                {
                    expression.Operators.Add(op);
                    operatorRequired = false;
                }
                else
                {
                    break;
                }
            }
            else
            {
                var ast = ParseNextExpressionUnit(enumerator, currentFunction, out operatorRequired);
                if (ast != null)
                    expression.Children.Add(ast);
            }
        } while (enumerator.MoveNext());

        return CheckExpression(enumerator, expression, operatorRequired);
    }

    private static DeclarationAst ParseDeclaration(TokenEnumerator enumerator, IFunction currentFunction = null, bool global = false)
    {
        var declaration = CreateAst<DeclarationAst>(enumerator);
        declaration.Global = global;
        declaration.Private = enumerator.Private;

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Expected variable name to be an identifier, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
        }
        declaration.Name = enumerator.Current.Value;

        // 1. Expect to get colon
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.Colon)
        {
            var errorToken = enumerator.Current;
            ErrorReporter.Report($"Unexpected token in declaration '{errorToken.Value}'", enumerator.FileIndex, errorToken);
            return declaration;
        }

        // 2. Check if type is given
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Expected type or value of declaration", enumerator.FileIndex, token);
        }
        if (token.Type == TokenType.Identifier)
        {
            enumerator.MoveNext();
            declaration.TypeDefinition = ParseType(enumerator, currentFunction);
            if (currentFunction != null)
            {
                for (var i = 0; i < currentFunction.Generics.Count; i++)
                {
                    var generic = currentFunction.Generics[i];
                    if (SearchForGeneric(generic, i, declaration.TypeDefinition))
                    {
                        declaration.HasGenerics = true;
                    }
                }
            }
        }

        // 3. Get the value or return
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected declaration to have value", enumerator.FileIndex, enumerator.Last);
            return declaration;
        }

        token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Equals:
                ParseValue(declaration, enumerator, currentFunction);
                break;
            case TokenType.SemiColon:
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report("Expected declaration to have value", enumerator.FileIndex, token);
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in declaration", enumerator.FileIndex, token);
                // Parse until there is an equals sign or semicolon
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == TokenType.SemiColon) break;
                    if (enumerator.Current.Type == TokenType.Equals)
                    {
                        ParseValue(declaration, enumerator, currentFunction);
                        break;
                    }
                }
                break;
        }

        // 4. Parse compiler directives
        if (enumerator.Peek(out token) && token.Type == TokenType.Pound)
        {
            if (enumerator.Peek(out token, 1) && token.Value == "const")
            {
                declaration.Constant = true;
                enumerator.MoveNext();
                enumerator.MoveNext();
            }
        }

        return declaration;
    }

    public static void ParseAssignmentValue(DeclarationAst variable, string code, int fileIndex, uint line)
    {
        var tokens = new List<Token>(code.Length / 5);
        Lexer.ParseTokens(code, fileIndex, tokens, line);

        var enumerator = new TokenEnumerator(fileIndex, tokens);
        ParseValue(variable, enumerator, null);

        if (enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected inserted global assignment value without any following expressions", fileIndex, enumerator.Current);
        }
    }

    private static bool ParseValue(IValues values, TokenEnumerator enumerator, IFunction currentFunction, bool arrayValue = false)
    {
        // 1. Step over '=' sign
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected declaration to have a value", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        // 2. Parse expression, constant, or object/array initialization as the value
        switch (enumerator.Current.Type)
        {
            case TokenType.OpenBrace:
                values.Assignments = new Dictionary<string, AssignmentAst>();
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;
                    if (token.Type == TokenType.CloseBrace)
                    {
                        break;
                    }

                    var assignment = ParseAssignment(enumerator, currentFunction, out var moveNext);

                    if (!values.Assignments.TryAdd(token.Value, assignment))
                    {
                        ErrorReporter.Report($"Multiple assignments for field '{token.Value}'", enumerator.FileIndex, token);
                    }

                    if (moveNext)
                    {
                        enumerator.Peek(out token);
                        if (token.Type == TokenType.CloseBrace)
                        {
                            enumerator.MoveNext();
                            break;
                        }
                    }
                    else if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        break;
                    }
                }
                return true;
            case TokenType.OpenBracket:
                values.ArrayValues = new List<Values>();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == TokenType.CloseBracket)
                    {
                        break;
                    }

                    var elementValues = ParseValues(enumerator, currentFunction, out var moveNext);
                    if (elementValues != null)
                    {
                        values.ArrayValues.Add(elementValues);
                        if (moveNext) enumerator.MoveNext();
                    }
                    if (enumerator.Current.Type == TokenType.CloseBracket)
                    {
                        break;
                    }
                    if (enumerator.Current.Type != TokenType.Comma)
                    {
                        ErrorReporter.Report("Expected ',' in array value", enumerator.FileIndex, enumerator.Current);
                    }
                }
                break;
            default:
                values.Value = ParseExpression(enumerator, currentFunction);
                break;
        }

        return false;
    }

    private static Values ParseValues(TokenEnumerator enumerator, IFunction currentFunction, out bool moveNext)
    {
        moveNext = false;
        var values = CreateAst<Values>(enumerator);
        switch (enumerator.Current.Type)
        {
            case TokenType.OpenBrace:
                values.Assignments = new Dictionary<string, AssignmentAst>();
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;
                    if (token.Type == TokenType.CloseBrace)
                    {
                        break;
                    }

                    var assignment = ParseAssignment(enumerator, currentFunction, out var getNext);

                    if (!values.Assignments.TryAdd(token.Value, assignment))
                    {
                        ErrorReporter.Report($"Multiple assignments for field '{token.Value}'", enumerator.FileIndex, token);
                    }

                    if (getNext)
                    {
                        enumerator.Peek(out token);
                        if (token.Type == TokenType.CloseBrace)
                        {
                            enumerator.MoveNext();
                            break;
                        }
                    }
                    else if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        break;
                    }
                }
                moveNext = true;
                break;
            case TokenType.OpenBracket:
                values.ArrayValues = new List<Values>();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == TokenType.CloseBracket)
                    {
                        break;
                    }

                    var elementValues = ParseValues(enumerator, currentFunction, out var getNext);
                    if (elementValues != null)
                    {
                        values.ArrayValues.Add(elementValues);
                        if (getNext) enumerator.MoveNext();
                    }
                    if (enumerator.Current.Type == TokenType.CloseBracket)
                    {
                        break;
                    }
                    if (enumerator.Current.Type != TokenType.Comma)
                    {
                        ErrorReporter.Report("Expected ',' in array value", enumerator.FileIndex, enumerator.Current);
                    }
                }
                moveNext = true;
                break;
            default:
                values.Value = ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseBracket);
                break;
        }

        return values;
    }

    private static AssignmentAst ParseAssignment(TokenEnumerator enumerator, IFunction currentFunction, out bool moveNext, IAst reference = null)
    {
        // 1. Set the variable
        var assignment = reference == null ? CreateAst<AssignmentAst>(enumerator) : CreateAst<AssignmentAst>(reference);
        assignment.Reference = reference;
        moveNext = false;

        // 2. When the original reference is null, set the l-value to an identifier
        if (reference == null)
        {
            var variableAst = CreateAst<IdentifierAst>(enumerator);
            if (enumerator.Current.Type != TokenType.Identifier)
            {
                ErrorReporter.Report($"Expected variable name to be an identifier, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
            }
            variableAst.Name = enumerator.Current.Value;
            assignment.Reference = variableAst;

            // 2a. Expect to get equals sign
            if (!enumerator.MoveNext())
            {
                ErrorReporter.Report("Expected '=' in assignment'", enumerator.FileIndex, enumerator.Last);
                return assignment;
            }

            // 2b. Assign the operator is there is one
            var token = enumerator.Current;
            if (token.Type != TokenType.Equals)
            {
                var op = ConvertOperator(token);
                if (op != Operator.None)
                {
                    assignment.Operator = op;
                    if (!enumerator.Peek(out token))
                    {
                        ErrorReporter.Report("Expected '=' in assignment'", enumerator.FileIndex, token);
                        return null;
                    }
                    if (token.Type == TokenType.Equals)
                    {
                        enumerator.MoveNext();
                    }
                    else
                    {
                        ErrorReporter.Report("Expected '=' in assignment'", enumerator.FileIndex, token);
                    }
                }
                else
                {
                    ErrorReporter.Report("Expected operator in assignment", enumerator.FileIndex, token);
                }
            }
        }
        // 3, Get the operator on the reference expression if the expression ends with an operator
        else if (reference is ExpressionAst expression)
        {
            if (expression.Children.Count == 1)
            {
                assignment.Reference = expression.Children[0];
            }
            if (expression.Operators.Any() && expression.Children.Count == expression.Operators.Count)
            {
                assignment.Operator = expression.Operators.Last();
                expression.Operators.RemoveAt(expression.Operators.Count - 1);
            }
        }

        // 4. Parse expression, field assignments, or array values
        switch (enumerator.Current.Type)
        {
            case TokenType.Equals:
                moveNext = ParseValue(assignment, enumerator, currentFunction);
                break;
            case TokenType.SemiColon:
                ErrorReporter.Report("Expected assignment to have value", enumerator.FileIndex, enumerator.Current);
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assignment", enumerator.FileIndex, enumerator.Current);
                // Parse until there is an equals sign or semicolon
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == TokenType.SemiColon) break;
                    if (enumerator.Current.Type == TokenType.Equals)
                    {
                        moveNext = ParseValue(assignment, enumerator, currentFunction);
                        break;
                    }
                }
                break;
        }

        return assignment;
    }

    private static IAst ParseExpression(TokenEnumerator enumerator, IFunction currentFunction, ExpressionAst initial = null, params TokenType[] endToken)
    {
        var operatorRequired = initial != null;

        var expression = initial ?? CreateAst<ExpressionAst>(enumerator);
        endToken = endToken.Append(TokenType.SemiColon).ToArray();

        do
        {
            var token = enumerator.Current;

            if (endToken.Contains(token.Type))
            {
                break;
            }

            if (token.Type == TokenType.Equals)
            {
                return ParseAssignment(enumerator, currentFunction, out _, expression);
            }
            if (token.Type == TokenType.Comma)
            {
                if (expression.Children.Count == 1)
                {
                    return ParseCompoundExpression(enumerator, currentFunction, expression.Children[0]);
                }
                return ParseCompoundExpression(enumerator, currentFunction, expression);
            }

            if (operatorRequired)
            {
                if (token.Type == TokenType.Increment || token.Type == TokenType.Decrement)
                {
                    // Create subexpression to hold the operation
                    // This case would be `var b = 4 + a++`, where we have a value before the operator
                    var source = expression.Children[^1];
                    var changeByOneAst = CreateAst<ChangeByOneAst>(source);
                    changeByOneAst.Positive = token.Type == TokenType.Increment;
                    changeByOneAst.Value = source;
                    expression.Children[^1] = changeByOneAst;
                    continue;
                }
                if (token.Type == TokenType.Number && token.Value[0] == '-')
                {
                    token.Value = token.Value[1..];
                    expression.Operators.Add(Operator.Subtract);
                    var constant = ParseConstant(token, enumerator.FileIndex);
                    expression.Children.Add(constant);
                    continue;
                }
                if (token.Type == TokenType.Period)
                {
                    var structFieldRef = ParseStructFieldRef(enumerator, expression.Children[^1], currentFunction);
                    expression.Children[^1] = structFieldRef;
                    continue;
                }
                var op = ConvertOperator(token);
                if (op != Operator.None)
                {
                    expression.Operators.Add(op);
                    operatorRequired = false;
                }
                else
                {
                    ErrorReporter.Report($"Unexpected token '{token.Value}' when operator was expected", enumerator.FileIndex, token);
                    return null;
                }
            }
            else
            {
                var ast = ParseNextExpressionUnit(enumerator, currentFunction, out operatorRequired);
                if (ast != null)
                    expression.Children.Add(ast);
            }
        } while (enumerator.MoveNext());

        return CheckExpression(enumerator, expression, operatorRequired);
    }

    private static IAst CheckExpression(TokenEnumerator enumerator, ExpressionAst expression, bool operatorRequired)
    {
        if (!expression.Children.Any())
        {
            ErrorReporter.Report("Expression should contain elements", enumerator.FileIndex, enumerator.Current);
            return null;
        }
        if (!operatorRequired && expression.Children.Any())
        {
            ErrorReporter.Report("Value required after operator", enumerator.FileIndex, enumerator.Current);
            return expression;
        }
        if (expression.Children.Count == 1)
        {
            return expression.Children.First();
        }

        SetOperatorPrecedence(expression);
        return expression;
    }

    private static IAst ParseCompoundExpression(TokenEnumerator enumerator, IFunction currentFunction, IAst initial)
    {
        var compoundExpression = CreateAst<CompoundExpressionAst>(initial);
        compoundExpression.Children.Add(initial);
        var firstToken = enumerator.Current;

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected compound expression to contain multiple values", enumerator.FileIndex, firstToken);
            return compoundExpression;
        }

        while (enumerator.Remaining)
        {
            var token = enumerator.Current;
            if (token.Type == TokenType.SemiColon)
            {
                break;
            }

            switch (token.Type)
            {
                case TokenType.Equals:
                    if (compoundExpression.Children.Count == 1)
                    {
                        ErrorReporter.Report("Expected compound expression to contain multiple values", enumerator.FileIndex, firstToken);
                    }
                    return ParseAssignment(enumerator, currentFunction, out _, compoundExpression);
                case TokenType.Colon:
                    var compoundDeclaration = CreateAst<CompoundDeclarationAst>(compoundExpression);
                    compoundDeclaration.Variables = new VariableAst[compoundExpression.Children.Count];

                    // Copy the initial expression to variables
                    for (var i = 0; i < compoundExpression.Children.Count; i++)
                    {
                        var variable = compoundExpression.Children[i];
                        if (variable is not IdentifierAst identifier)
                        {
                            ErrorReporter.Report("Declaration should contain a variable", variable);
                        }
                        else
                        {
                            var variableAst = CreateAst<VariableAst>(identifier);
                            variableAst.Name = identifier.Name;
                            compoundDeclaration.Variables[i] = variableAst;
                        }
                    }

                    if (!enumerator.MoveNext())
                    {
                        ErrorReporter.Report("Expected declaration to contain type and/or value", enumerator.FileIndex, enumerator.Last);
                        return null;
                    }

                    if (enumerator.Current.Type == TokenType.Identifier)
                    {
                        compoundDeclaration.TypeDefinition = ParseType(enumerator);
                        if (currentFunction != null)
                        {
                            for (var i = 0; i < currentFunction.Generics.Count; i++)
                            {
                                var generic = currentFunction.Generics[i];
                                if (SearchForGeneric(generic, i, compoundDeclaration.TypeDefinition))
                                {
                                    compoundDeclaration.HasGenerics = true;
                                }
                            }
                        }
                        enumerator.MoveNext();
                    }

                    if (!enumerator.Remaining)
                    {
                        return compoundDeclaration;
                    }
                    switch (enumerator.Current.Type)
                    {
                        case TokenType.Equals:
                            ParseValue(compoundDeclaration, enumerator, currentFunction);
                            break;
                        case TokenType.SemiColon:
                            if (compoundDeclaration.TypeDefinition == null)
                            {
                                ErrorReporter.Report("Expected token declaration to have type and/or value", enumerator.FileIndex, token);
                            }
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in declaration", enumerator.FileIndex, enumerator.Current);
                            // Parse until there is an equals sign or semicolon
                            while (enumerator.MoveNext())
                            {
                                if (enumerator.Current.Type == TokenType.SemiColon) break;
                                if (enumerator.Current.Type == TokenType.Equals)
                                {
                                    ParseValue(compoundDeclaration, enumerator, currentFunction);
                                    break;
                                }
                            }
                            break;
                    }

                    return compoundDeclaration;
                default:
                    compoundExpression.Children.Add(ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.Colon, TokenType.Equals));
                    if (enumerator.Current.Type == TokenType.Comma)
                    {
                        enumerator.MoveNext();
                    }
                    break;
            }
        }

        if (compoundExpression.Children.Count == 1)
        {
            ErrorReporter.Report("Expected compound expression to contain multiple values", enumerator.FileIndex, firstToken);
        }

        return compoundExpression;
    }

    private static IAst ParseStructFieldRef(TokenEnumerator enumerator, IAst initialAst, IFunction currentFunction)
    {
        // 1. Initialize and move over the dot operator
        var structFieldRef = CreateAst<StructFieldRefAst>(initialAst);
        structFieldRef.Children.Add(initialAst);

        // 2. Parse expression units until the operator is not '.'
        var operatorRequired = false;
        while (enumerator.MoveNext())
        {
            if (operatorRequired)
            {
                if (enumerator.Current.Type != TokenType.Period)
                {
                    enumerator.Move(-1);
                    break;
                }
                operatorRequired = false;
            }
            else
            {
                var ast = ParseNextExpressionUnit(enumerator, currentFunction, out operatorRequired);
                if (ast != null)
                    structFieldRef.Children.Add(ast);
            }
        }

        return structFieldRef;
    }

    private static IAst ParseNextExpressionUnit(TokenEnumerator enumerator, IFunction currentFunction, out bool operatorRequired)
    {
        var token = enumerator.Current;
        operatorRequired = true;
        switch (token.Type)
        {
            case TokenType.Number:
            case TokenType.Boolean:
            case TokenType.Literal:
            case TokenType.Character:
                // Parse constant
                return ParseConstant(token, enumerator.FileIndex);
            case TokenType.Null:
                return CreateAst<NullAst>(token, enumerator.FileIndex);
            case TokenType.Identifier:
                // Parse variable, call, or expression
                if (!enumerator.Peek(out var nextToken))
                {
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", enumerator.FileIndex, token);
                    return null;
                }
                switch (nextToken.Type)
                {
                    case TokenType.OpenParen:
                        return ParseCall(enumerator, currentFunction);
                    case TokenType.OpenBracket:
                        return ParseIndex(enumerator, currentFunction);
                    case TokenType.Asterisk:
                        if (enumerator.Peek(out nextToken, 1))
                        {
                            switch (nextToken.Type)
                            {
                                case TokenType.Comma:
                                case TokenType.CloseParen:
                                    if (TryParseType(enumerator, out var typeDefinition))
                                    {
                                        for (var i = 0; i < currentFunction.Generics.Count; i++)
                                        {
                                            SearchForGeneric(currentFunction.Generics[i], i, typeDefinition);
                                        }
                                        return typeDefinition;
                                    }
                                    break;
                            }
                        }
                        break;
                    case TokenType.LessThan:
                    {
                        if (TryParseType(enumerator, out var typeDefinition))
                        {
                            if (enumerator.Current.Type == TokenType.OpenParen)
                            {
                                var callAst = CreateAst<CallAst>(typeDefinition);
                                callAst.Name = typeDefinition.Name;
                                callAst.Generics = typeDefinition.Generics;

                                if (currentFunction != null)
                                {
                                    foreach (var generic in callAst.Generics)
                                    {
                                        for (var i = 0; i < currentFunction.Generics.Count; i++)
                                        {
                                            SearchForGeneric(currentFunction.Generics[i], i, generic);
                                        }
                                    }
                                }

                                enumerator.MoveNext();
                                ParseArguments(callAst, enumerator, currentFunction);
                                return callAst;
                            }
                            else
                            {
                                if (currentFunction != null)
                                {
                                    for (var i = 0; i < currentFunction.Generics.Count; i++)
                                    {
                                        SearchForGeneric(currentFunction.Generics[i], i, typeDefinition);
                                    }
                                }
                                return typeDefinition;
                            }
                        }
                        break;
                    }
                }
                var identifier = CreateAst<IdentifierAst>(token, enumerator.FileIndex);
                identifier.Name = token.Value;
                return identifier;
            case TokenType.Increment:
            case TokenType.Decrement:
                var positive = token.Type == TokenType.Increment;
                if (enumerator.MoveNext())
                {
                    var changeByOneAst = CreateAst<ChangeByOneAst>(enumerator);
                    changeByOneAst.Prefix = true;
                    changeByOneAst.Positive = positive;
                    changeByOneAst.Value = ParseNextExpressionUnit(enumerator, currentFunction, out operatorRequired);
                    if (enumerator.Peek(out token) && token.Type == TokenType.Period)
                    {
                        enumerator.MoveNext();
                        changeByOneAst.Value = ParseStructFieldRef(enumerator, changeByOneAst.Value, currentFunction);
                    }
                    return changeByOneAst;
                }
                else
                {
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", enumerator.FileIndex, token);
                    return null;
                }
            case TokenType.OpenParen:
                // Parse subexpression
                if (enumerator.MoveNext())
                {
                    return ParseExpression(enumerator, currentFunction, null, TokenType.CloseParen);
                }
                else
                {
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", enumerator.FileIndex, token);
                    return null;
                }
            case TokenType.Not:
            case TokenType.Minus:
            case TokenType.Asterisk:
            case TokenType.Ampersand:
                if (enumerator.MoveNext())
                {
                    var unaryAst = CreateAst<UnaryAst>(token, enumerator.FileIndex);
                    unaryAst.Operator = (UnaryOperator)token.Value[0];
                    unaryAst.Value = ParseNextExpressionUnit(enumerator, currentFunction, out operatorRequired);
                    if (enumerator.Peek(out token) && token.Type == TokenType.Period)
                    {
                        enumerator.MoveNext();
                        unaryAst.Value = ParseStructFieldRef(enumerator, unaryAst.Value, currentFunction);
                    }
                    return unaryAst;
                }
                else
                {
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", enumerator.FileIndex, token);
                    return null;
                }
            case TokenType.Cast:
                return ParseCast(enumerator, currentFunction);
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in expression", enumerator.FileIndex, token);
                operatorRequired = false;
                return null;
       }
    }

    private static void SetOperatorPrecedence(ExpressionAst expression)
    {
        if (expression.Operators.Count == 0) return;

        // 1. Set the initial operator precedence
        var operatorPrecedence = GetOperatorPrecedence(expression.Operators[0]);
        for (var i = 1; i < expression.Operators.Count; i++)
        {
            // 2. Get the next operator
            var precedence = GetOperatorPrecedence(expression.Operators[i]);

            // 3. Create subexpressions to enforce operator precedence if necessary
            if (precedence > operatorPrecedence)
            {
                var subExpression = CreateSubExpression(expression, precedence, i, out var end);
                expression.Children[i] = subExpression;
                expression.Children.RemoveRange(i + 1, end - i);
                expression.Operators.RemoveRange(i, end - i);

                if (i >= expression.Operators.Count) return;
                operatorPrecedence = GetOperatorPrecedence(expression.Operators[--i]);
            }
            else
            {
                operatorPrecedence = precedence;
            }
        }
    }

    private static ExpressionAst CreateSubExpression(ExpressionAst expression, int parentPrecedence, int i, out int end)
    {
        var subExpression = CreateAst<ExpressionAst>(expression.Children[i]);

        subExpression.Children.Add(expression.Children[i]);
        subExpression.Operators.Add(expression.Operators[i]);
        for (++i; i < expression.Operators.Count; i++)
        {
            // 1. Get the next operator
            var precedence = GetOperatorPrecedence(expression.Operators[i]);

            // 2. Create subexpressions to enforce operator precedence if necessary
            if (precedence > parentPrecedence)
            {
                subExpression.Children.Add(CreateSubExpression(expression, precedence, i, out i));
                if (i == expression.Operators.Count)
                {
                    end = i;
                    return subExpression;
                }

                subExpression.Operators.Add(expression.Operators[i]);
            }
            else if (precedence < parentPrecedence)
            {
                subExpression.Children.Add(expression.Children[i]);
                end = i;
                return subExpression;
            }
            else
            {
                subExpression.Children.Add(expression.Children[i]);
                subExpression.Operators.Add(expression.Operators[i]);
            }
        }

        subExpression.Children.Add(expression.Children[^1]);
        end = i;
        return subExpression;
    }

    private static int GetOperatorPrecedence(Operator op)
    {
        switch (op)
        {
            // Boolean comparisons
            case Operator.And:
            case Operator.Or:
            case Operator.BitwiseAnd:
            case Operator.BitwiseOr:
            case Operator.Xor:
                return 0;
            // Value comparisons
            case Operator.Equality:
            case Operator.NotEqual:
            case Operator.GreaterThan:
            case Operator.LessThan:
            case Operator.GreaterThanEqual:
            case Operator.LessThanEqual:
                return 5;
            // First order operators
            case Operator.Add:
            case Operator.Subtract:
                return 10;
            // Second order operators
            case Operator.Multiply:
            case Operator.Divide:
            case Operator.Modulus:
                return 20;
            default:
                return 0;
        }
    }

    private static CallAst ParseCall(TokenEnumerator enumerator, IFunction currentFunction, bool requiresSemicolon = false)
    {
        var callAst = CreateAst<CallAst>(enumerator);
        callAst.Name = enumerator.Current.Value;

        // This enumeration is the open paren
        enumerator.MoveNext();
        // Enumerate over the first argument
        ParseArguments(callAst, enumerator, currentFunction);

        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected to close call", enumerator.FileIndex, enumerator.Last);
        }
        else if (requiresSemicolon)
        {
            if (enumerator.Current.Type == TokenType.SemiColon)
                return callAst;

            if (!enumerator.Peek(out var token) || token.Type != TokenType.SemiColon)
            {
                ErrorReporter.Report("Expected ';'", enumerator.FileIndex, token);
            }
            else
            {
                enumerator.MoveNext();
            }
        }

        return callAst;
    }

    private static void ParseArguments(CallAst callAst, TokenEnumerator enumerator, IFunction currentFunction)
    {
        var nextArgumentRequired = false;
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                if (nextArgumentRequired)
                {
                    ErrorReporter.Report($"Expected argument in call following a comma", enumerator.FileIndex, token);
                }
                break;
            }

            if (token.Type == TokenType.Comma)
            {
                ErrorReporter.Report("Expected comma before next argument", enumerator.FileIndex, token);
            }
            else
            {
                if (token.Type == TokenType.Identifier && enumerator.Peek(out var nextToken) && nextToken.Type == TokenType.Equals)
                {
                    var argumentName = token.Value;

                    enumerator.MoveNext();
                    enumerator.MoveNext();

                    callAst.SpecifiedArguments ??= new Dictionary<string, IAst>();
                    var argument = ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseParen);
                    if (!callAst.SpecifiedArguments.TryAdd(argumentName, argument))
                    {
                        ErrorReporter.Report($"Specified argument '{token.Value}' is already in the call", enumerator.FileIndex, token);
                    }
                }
                else
                {
                    callAst.Arguments.Add(ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseParen));
                }

                var currentType = enumerator.Current.Type;
                if (currentType == TokenType.CloseParen) break;
                if (currentType == TokenType.Comma)
                {
                    nextArgumentRequired = true;
                }
                if (currentType == TokenType.SemiColon)
                {
                    ErrorReporter.Report("Expected to close call with ')'", enumerator.FileIndex, enumerator.Current);
                    break;
                }
            }
        }
    }

    private static ReturnAst ParseReturn(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var returnAst = CreateAst<ReturnAst>(enumerator);

        if (enumerator.MoveNext())
        {
            if (enumerator.Current.Type != TokenType.SemiColon)
            {
                returnAst.Value = ParseExpression(enumerator, currentFunction);
            }
        }
        else
        {
            ErrorReporter.Report("Return does not have value", enumerator.FileIndex, enumerator.Last);
        }

        return returnAst;
    }

    private static IndexAst ParseIndex(TokenEnumerator enumerator, IFunction currentFunction)
    {
        // 1. Initialize the index ast
        var index = CreateAst<IndexAst>(enumerator);
        index.Name = enumerator.Current.Value;
        enumerator.MoveNext();

        // 2. Parse index expression
        enumerator.MoveNext();
        index.Index = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);

        return index;
    }

    private static CompilerDirectiveAst ParseTopLevelDirective(TokenEnumerator enumerator, string directory, bool global = false)
    {
        var directive = CreateAst<CompilerDirectiveAst>(enumerator);

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected compiler directive to have a value", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        var token = enumerator.Current;
        switch (token.Value)
        {
            case "run":
                directive.Type = DirectiveType.Run;
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report("End of file reached without closing #run directive", enumerator.FileIndex, enumerator.Last);
                    return null;
                }
                directive.Value = ParseScopeBody(enumerator, null);
                break;
            case "if":
                directive.Type = DirectiveType.If;
                directive.Value = ParseConditional(enumerator, null, true, directory);
                break;
            case "assert":
                directive.Type = DirectiveType.Assert;
                enumerator.MoveNext();
                directive.Value = ParseExpression(enumerator, null);
                if (enumerator.Peek(out var message) && message.Type == TokenType.Literal)
                {
                    directive.StringValue = message.Value;
                    enumerator.MoveNext();
                }
                break;
            case "import":
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report($"Expected module name or source file", enumerator.FileIndex, enumerator.Last);
                    return null;
                }
                token = enumerator.Current;
                switch (token.Type)
                {
                    case TokenType.Identifier:
                        directive.Type = DirectiveType.ImportModule;
                        var module = token.Value;
                        if (global)
                        {
                            AddModule(module, enumerator.FileIndex, token);
                        }
                        else
                        {
                            directive.Import = new Import {Name = module, Path = Path.Combine(_libraryDirectory, $"{token.Value}.ol")};
                        }
                        break;
                    case TokenType.Literal:
                        directive.Type = DirectiveType.ImportFile;
                        var file = token.Value;
                        if (global)
                        {
                            AddFile(file, directory, enumerator.FileIndex, token);
                        }
                        else
                        {
                            directive.Import = new Import {Name = file, Path = Path.Combine(directory, token.Value)};
                        }
                        break;
                    default:
                        ErrorReporter.Report($"Expected module name or source file, but got '{token.Value}'", enumerator.FileIndex, token);
                        break;
                }
                break;
            case "library":
                directive.Type = DirectiveType.Library;
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Identifier)
                {
                    ErrorReporter.Report($"Expected library name, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    return null;
                }
                var name = enumerator.Current.Value;
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Literal)
                {
                    ErrorReporter.Report($"Expected library path, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    return null;
                }
                var path = enumerator.Current.Value;
                directive.Library = new Library
                {
                    Name = name, Path = path,
                    AbsolutePath = Path.IsPathRooted(path) ? path : Path.Combine(directory, path)
                };
                break;
            case "system_library":
                directive.Type = DirectiveType.SystemLibrary;
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Identifier)
                {
                    ErrorReporter.Report($"Expected library name, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    return null;
                }
                name = enumerator.Current.Value;
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Literal)
                {
                    ErrorReporter.Report($"Expected library file name, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    return null;
                }
                var fileName = enumerator.Current.Value;
                directive.Library = new Library {Name = name, FileName = fileName};
                if (enumerator.Peek(out token) && token.Type == TokenType.Literal)
                {
                    directive.Library.LibPath = Path.IsPathRooted(token.Value) ? token.Value : Path.Combine(directory, token.Value);
                    enumerator.MoveNext();
                }
                break;
            case "copy_to_output_directory":
                directive.Type = DirectiveType.CopyToOutputDirectory;
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Literal)
                {
                    ErrorReporter.Report($"Expected path of file to copy, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    return null;
                }
                directive.StringValue = Path.IsPathRooted(enumerator.Current.Value) ? enumerator.Current.Value : Path.Combine(directory, enumerator.Current.Value);
                break;
            case "private":
                if (enumerator.Private)
                {
                    ErrorReporter.Report("Tried to set #private when already in private scope", enumerator.FileIndex, token);
                }
                else
                {
                    TypeChecker.PrivateScopes[enumerator.FileIndex] = new PrivateScope{Parent = TypeChecker.GlobalScope};
                }
                enumerator.Private = true;
                return null;
            default:
                ErrorReporter.Report($"Unsupported top-level compiler directive '{token.Value}'", enumerator.FileIndex, token);
                return null;
        }

        return directive;
    }

    private static IAst ParseCompilerDirective(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var directive = CreateAst<CompilerDirectiveAst>(enumerator);

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected compiler directive to have a value", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        var token = enumerator.Current;
        switch (token.Value)
        {
            case "if":
                directive.Type = DirectiveType.If;
                directive.Value = ParseConditional(enumerator, currentFunction);
                if (currentFunction != null)
                {
                    currentFunction.Flags |= FunctionFlags.HasDirectives;
                }
                break;
            case "assert":
                directive.Type = DirectiveType.Assert;
                enumerator.MoveNext();
                directive.Value = ParseExpression(enumerator, currentFunction);
                if (enumerator.Peek(out var message) && message.Type == TokenType.Literal)
                {
                    directive.StringValue = message.Value;
                    enumerator.MoveNext();
                }
                if (currentFunction != null)
                {
                    currentFunction.Flags |= FunctionFlags.HasDirectives;
                }
                break;
            case "inline":
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Identifier)
                {
                    ErrorReporter.Report($"Expected function call following #inline directive", enumerator.FileIndex, token);
                    return null;
                }
                var call = ParseCall(enumerator, currentFunction, true);
                if (call != null)
                {
                    call.Inline = true;
                }
                return call;
            case "insert":
                directive.Type = DirectiveType.Insert;
                enumerator.MoveNext();
                directive.Value = ParseScopeBody(enumerator, currentFunction);
                break;
            default:
                ErrorReporter.Report($"Unsupported function level compiler directive '{token.Value}'", enumerator.FileIndex, token);
                return null;
        }

        return directive;
    }

    private static AssemblyAst ParseInlineAssembly(TokenEnumerator enumerator)
    {
        var assembly = CreateAst<AssemblyAst>(enumerator);
        assembly.Instructions = new();
        assembly.InRegisters = new();
        assembly.OutValues = new();

        // First move over the opening '{'
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected an opening '{' at asm block", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected an opening '{' at asm block", enumerator.FileIndex, enumerator.Current);
            return null;
        }

        var closed = false;
        var parsingInRegisters = true;
        var parsingOutRegisters = false;
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;
            if (token.Type == TokenType.CloseBrace)
            {
                closed = true;
                break;
            }

            switch (token.Type)
            {
                case TokenType.In:
                    if (ParseInRegister(assembly, enumerator))
                    {
                        if (parsingOutRegisters || !parsingInRegisters)
                        {
                            ErrorReporter.Report("In instructions should be declared before body instructions", enumerator.FileIndex, token);
                        }
                    }
                    else
                    {
                        // Skip through the next ';' or '}'
                        while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && enumerator.MoveNext());

                        if (enumerator.Current.Type == TokenType.CloseBrace)
                        {
                            return assembly;
                        }
                    }
                    break;
                case TokenType.Out:
                    if (ParseOutValue(assembly, enumerator))
                    {
                        if (parsingInRegisters)
                        {
                            parsingInRegisters = false;
                            parsingOutRegisters = true;
                            ErrorReporter.Report("Expected instructions before out registers", enumerator.FileIndex, token);
                        }
                        else if (!parsingOutRegisters)
                        {
                            parsingOutRegisters = true;
                        }
                    }
                    else
                    {
                        // Skip through the next ';' or '}'
                        while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && enumerator.MoveNext());

                        if (enumerator.Current.Type == TokenType.CloseBrace)
                        {
                            return assembly;
                        }
                    }
                    break;
                case TokenType.Identifier:
                    var instruction = ParseAssemblyInstruction(enumerator);
                    if (instruction == null)
                    {
                        // Skip through the next ';' or '}'
                        while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && enumerator.MoveNext());

                        if (enumerator.Current.Type == TokenType.CloseBrace)
                        {
                            return assembly;
                        }
                    }
                    else
                    {
                        if (parsingInRegisters)
                        {
                            parsingInRegisters = false;
                        }
                        else if (parsingOutRegisters)
                        {
                            ErrorReporter.Report("Expected body instructions before out values", instruction);
                        }
                        assembly.Instructions.Add(instruction);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Expected instruction in assembly block, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);

                    // Skip through the next ';' or '}'
                    while ((enumerator.Current.Type != TokenType.SemiColon || enumerator.Current.Type != TokenType.CloseBrace) && enumerator.MoveNext());

                    if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        return assembly;
                    }
                    break;
            }
        }

        if (!closed)
        {
            ErrorReporter.Report("Assembly block not closed by '}'", enumerator.FileIndex, enumerator.Current);
        }

        return assembly;
    }

    private static bool ParseInRegister(AssemblyAst assembly, TokenEnumerator enumerator)
    {
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value or semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return false;
        }

        var register = enumerator.Current.Value;
        if (assembly.InRegisters.ContainsKey(register))
        {
            ErrorReporter.Report($"Duplicate in register '{register}'", enumerator.FileIndex, enumerator.Current);
            return false;
        }
        var input = CreateAst<AssemblyInputAst>(enumerator);
        input.Register = register;

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected comma or semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        if (enumerator.Current.Type != TokenType.Comma)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return false;
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        input.Ast = ParseExpression(enumerator, null);

        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        if (enumerator.Current.Type != TokenType.SemiColon)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return false;
        }

        assembly.InRegisters[register] = input;
        return true;
    }

    private static bool ParseOutValue(AssemblyAst assembly, TokenEnumerator enumerator)
    {
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value or semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        var output = CreateAst<AssemblyInputAst>(enumerator);

        output.Ast = ParseNextExpressionUnit(enumerator, null, out _);
        if (output.Ast == null)
        {
            return false;
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected comma or semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        if (enumerator.Current.Type != TokenType.Comma)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return false;
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return false;
        }
        output.Register = enumerator.Current.Value;

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return false;
        }

        if (enumerator.Current.Type != TokenType.SemiColon)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return false;
        }

        assembly.OutValues.Add(output);
        return true;
    }

    private static AssemblyInstructionAst ParseAssemblyInstruction(TokenEnumerator enumerator)
    {
        var instruction = CreateAst<AssemblyInstructionAst>(enumerator);
        instruction.Instruction = enumerator.Current.Value;

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value or semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        switch (enumerator.Current.Type)
        {
            case TokenType.Identifier:
                instruction.Value1 = CreateAst<AssemblyValueAst>(enumerator);
                instruction.Value1.Register = enumerator.Current.Value;
                break;
            case TokenType.Number:
                instruction.Value1 = CreateAst<AssemblyValueAst>(enumerator);
                instruction.Value1.Constant = ParseConstant(enumerator.Current, enumerator.FileIndex);
                break;
            case TokenType.OpenBracket:
                instruction.Value1 = CreateAst<AssemblyValueAst>(enumerator);
                if (!ParseAssemblyPointer(instruction.Value1, enumerator))
                {
                    return null;
                }
                break;
            case TokenType.SemiColon:
                return instruction;
            default:
                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
                return null;
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected comma or semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        switch (enumerator.Current.Type)
        {
            case TokenType.Comma:
                break;
            case TokenType.SemiColon:
                return instruction;
            default:
                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
                return null;
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value in instruction", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        instruction.Value2 = CreateAst<AssemblyValueAst>(enumerator);
        switch (enumerator.Current.Type)
        {
            case TokenType.Identifier:
                instruction.Value2.Register = enumerator.Current.Value;
                break;
            case TokenType.Number:
                instruction.Value2.Constant = ParseConstant(enumerator.Current, enumerator.FileIndex);
                break;
            case TokenType.OpenBracket:
                if (!ParseAssemblyPointer(instruction.Value2, enumerator))
                {
                    return null;
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
                return null;
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected semicolon in instruction", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        if (enumerator.Current.Type != TokenType.SemiColon)
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.FileIndex, enumerator.Current);
            return null;
        }

        return instruction;
    }

    private static bool ParseAssemblyPointer(AssemblyValueAst value, TokenEnumerator enumerator)
    {
        if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report("Expected register after pointer in instruction", enumerator.FileIndex, enumerator.Current);
            return false;
        }
        value.Dereference = true;
        value.Register = enumerator.Current.Value;

        if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.CloseBracket)
        {
            ErrorReporter.Report("Expected to close pointer to register with ']'", enumerator.FileIndex, enumerator.Current);
            return false;
        }

        return true;
    }

    private static SwitchAst ParseSwitch(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var switchAst = CreateAst<SwitchAst>(enumerator);
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected value for switch statement", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        switchAst.Value = ParseExpression(enumerator, currentFunction, null, TokenType.OpenBrace);

        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected switch statement value to be followed by '{' and cases", enumerator.FileIndex, enumerator.Current);
            return null;
        }

        var closed = false;
        List<IAst> currentCases = null;
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;
            if (token.Type == TokenType.CloseBrace)
            {
                closed = true;
                if (currentCases != null)
                {
                    ErrorReporter.Report("Switch statement contains case(s) without bodies starting", currentCases[0]);
                    return null;
                }
                break;
            }

            if (token.Type == TokenType.Case)
            {
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report("Expected value after case", enumerator.FileIndex, enumerator.Last);
                    return null;
                }

                var switchCase = ParseExpression(enumerator, currentFunction);
                if (currentCases == null)
                {
                    currentCases = new() {switchCase};
                }
                else
                {
                    currentCases.Add(switchCase);
                }
            }
            else if (token.Type == TokenType.Default)
            {
                if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.SemiColon)
                {
                    ErrorReporter.Report("Expected ';' after default", enumerator.FileIndex, enumerator.Current);
                    return null;
                }
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report("Expected body after default", enumerator.FileIndex, enumerator.Last);
                    return null;
                }

                switchAst.DefaultCase = ParseScopeBody(enumerator, currentFunction);
            }
            else if (currentCases == null)
            {
                ErrorReporter.Report("Switch statement contains case body prior to any cases", enumerator.FileIndex, token);
                // Skip through the next ';' or '}'
                while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && enumerator.MoveNext());
            }
            else
            {
                // Parse through the case body and add to the list
                var caseScope = ParseScopeBody(enumerator, currentFunction);
                switchAst.Cases.Add((currentCases, caseScope));
                currentCases = null;
            }
        }

        if (!closed)
        {
            ErrorReporter.Report("Expected switch statement to be closed by '}'", enumerator.FileIndex, enumerator.Current);
            return null;
        }
        else if (switchAst.Cases.Count == 0)
        {
            ErrorReporter.Report("Expected switch to have 1 or more non-default cases", switchAst);
            return null;
        }

        return switchAst;
    }

    private static OperatorOverloadAst ParseOperatorOverload(TokenEnumerator enumerator)
    {
        var overload = CreateAst<OperatorOverloadAst>(enumerator);
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected an operator be specified to overload", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        // 1. Determine the operator
        if (enumerator.Current.Type == TokenType.OpenBracket && enumerator.Peek(out var token) && token.Type == TokenType.CloseBracket)
        {
            overload.Operator = Operator.Subscript;
            enumerator.MoveNext();
        }
        else
        {
            overload.Operator = ConvertOperator(enumerator.Current);
            if (overload.Operator == Operator.None)
            {
                ErrorReporter.Report($"Expected an operator to be be specified, but got '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
            }
        }
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report($"Expected to get the type to overload the operator", enumerator.FileIndex, enumerator.Last);
            return null;
        }

        // 2. Determine generics if necessary
        if (enumerator.Current.Type == TokenType.LessThan)
        {
            var commaRequiredBeforeNextType = false;
            var generics = new HashSet<string>();
            while (enumerator.MoveNext())
            {
                token = enumerator.Current;

                if (token.Type == TokenType.GreaterThan)
                {
                    if (!commaRequiredBeforeNextType)
                    {
                        ErrorReporter.Report($"Expected comma in generics", enumerator.FileIndex, token);
                    }

                    break;
                }

                if (!commaRequiredBeforeNextType)
                {
                    switch (token.Type)
                    {
                        case TokenType.Identifier:
                            if (!generics.Add(token.Value))
                            {
                                ErrorReporter.Report($"Duplicate generic '{token.Value}'", enumerator.FileIndex, token);
                            }
                            commaRequiredBeforeNextType = true;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in generics", enumerator.FileIndex, token);
                            commaRequiredBeforeNextType = true;
                            break;
                    }
                }
                else
                {
                    switch (token.Type)
                    {
                        case TokenType.Comma:
                            commaRequiredBeforeNextType = false;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' when defining generics", enumerator.FileIndex, token);
                            commaRequiredBeforeNextType = false;
                            break;
                    }
                }
            }

            if (!generics.Any())
            {
                ErrorReporter.Report("Expected operator overload to contain generics", enumerator.FileIndex, enumerator.Current);
            }
            enumerator.MoveNext();
            overload.Generics.AddRange(generics);
        }

        // 3. Find open paren to start parsing arguments
        if (enumerator.Current.Type != TokenType.OpenParen)
        {
            // Add an error to the function AST and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in operator overload definition", enumerator.FileIndex, token);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenParen);
        }

        // 4. Get the arguments for the operator overload
        var commaRequiredBeforeNextArgument = false;
        DeclarationAst currentArgument = null;
        while (enumerator.MoveNext())
        {
            token = enumerator.Current;

            if (token.Type == TokenType.CloseParen)
            {
                if (commaRequiredBeforeNextArgument)
                {
                    overload.Arguments.Add(currentArgument);
                    currentArgument = null;
                }
                break;
            }

            switch (token.Type)
            {
                case TokenType.Identifier:
                    if (commaRequiredBeforeNextArgument)
                    {
                        ErrorReporter.Report("Comma required after declaring an argument", enumerator.FileIndex, token);
                    }
                    else if (currentArgument == null)
                    {
                        currentArgument = CreateAst<DeclarationAst>(token, enumerator.FileIndex);
                        currentArgument.TypeDefinition = ParseType(enumerator, argument: true);
                        for (var i = 0; i < overload.Generics.Count; i++)
                        {
                            var generic = overload.Generics[i];
                            if (SearchForGeneric(generic, i, currentArgument.TypeDefinition))
                            {
                                currentArgument.HasGenerics = true;
                            }
                        }
                        if (overload.Arguments.Count == 0)
                        {
                            overload.Type = currentArgument.TypeDefinition;
                        }
                    }
                    else
                    {
                        currentArgument.Name = token.Value;
                        commaRequiredBeforeNextArgument = true;
                    }
                    break;
                case TokenType.Comma:
                    if (commaRequiredBeforeNextArgument)
                    {
                        overload.Arguments.Add(currentArgument);
                    }
                    else
                    {
                        ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, token);
                    }
                    currentArgument = null;
                    commaRequiredBeforeNextArgument = false;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in arguments", enumerator.FileIndex, token);
                    break;
            }

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                break;
            }
        }

        if (currentArgument != null)
        {
            ErrorReporter.Report($"Incomplete argument in overload for type '{overload.Type.Name}'", enumerator.FileIndex, enumerator.Current);
        }

        if (!commaRequiredBeforeNextArgument && overload.Arguments.Any())
        {
            ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, enumerator.Current);
        }

        // 5. Set the return type based on the operator
        enumerator.MoveNext();
        switch (overload.Operator)
        {
            case Operator.And:
            case Operator.Or:
            case Operator.Equality:
            case Operator.NotEqual:
            case Operator.GreaterThanEqual:
            case Operator.LessThanEqual:
            case Operator.GreaterThan:
            case Operator.LessThan:
            case Operator.Xor:
                overload.ReturnTypeDefinition = new TypeDefinition {Name = "bool"};
                break;
            case Operator.Subscript:
                if (enumerator.Current.Type != TokenType.Colon)
                {
                    ErrorReporter.Report($"Unexpected to define return type for subscript", enumerator.FileIndex, enumerator.Current);
                }
                else
                {
                    if (enumerator.MoveNext())
                    {
                        overload.ReturnTypeDefinition = ParseType(enumerator);
                        for (var i = 0; i < overload.Generics.Count; i++)
                        {
                            if (SearchForGeneric(overload.Generics[i], i, overload.ReturnTypeDefinition))
                            {
                                overload.Flags |= FunctionFlags.ReturnTypeHasGenerics;
                            }
                        }
                        enumerator.MoveNext();
                    }
                }
                break;
            default:
                overload.ReturnTypeDefinition = overload.Type;
                if (overload.Generics.Any())
                {
                    overload.Flags |= FunctionFlags.ReturnTypeHasGenerics;
                }
                for (var i = 0; i < overload.Generics.Count; i++)
                {
                    SearchForGeneric(overload.Generics[i], i, overload.ReturnTypeDefinition);
                }
                break;
        }

        // 6. Handle compiler directives
        if (enumerator.Current.Type == TokenType.Pound)
        {
            if (!enumerator.MoveNext())
            {
                ErrorReporter.Report("Expected compiler directive value", enumerator.FileIndex, enumerator.Last);
                return null;
            }
            switch (enumerator.Current.Value)
            {
                case "print_ir":
                    overload.Flags |= FunctionFlags.PrintIR;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.FileIndex, enumerator.Current);
                    break;
            }
            enumerator.MoveNext();
        }

        // 7. Find open brace to start parsing body
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            // Add an error and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in operator overload definition", enumerator.FileIndex, token);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 8. Parse body
        overload.Body = ParseScope(enumerator, overload);

        return overload;
    }

    private static InterfaceAst ParseInterface(TokenEnumerator enumerator)
    {
        var interfaceAst = CreateAst<InterfaceAst>(enumerator);
        interfaceAst.Private = enumerator.Private;
        enumerator.MoveNext();

        // 1a. Check if the return type is void
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Expected interface definition", enumerator.FileIndex, token);
        }
        if (token.Type != TokenType.OpenParen)
        {
            interfaceAst.ReturnTypeDefinition = ParseType(enumerator);
            enumerator.MoveNext();
        }

        // 1b. Handle multiple return values
        if (enumerator.Current.Type == TokenType.Comma)
        {
            var returnType = CreateAst<TypeDefinition>(interfaceAst.ReturnTypeDefinition);
            returnType.Compound = true;
            returnType.Generics.Add(interfaceAst.ReturnTypeDefinition);
            interfaceAst.ReturnTypeDefinition = returnType;

            while (enumerator.Current.Type == TokenType.Comma)
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }
                returnType.Generics.Add(ParseType(enumerator));
                enumerator.MoveNext();
            }
        }

        // 1b. Set the name of the interface or get the name from the type
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected the interface name to be declared", enumerator.FileIndex, enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Type)
        {
            case TokenType.Identifier:
                interfaceAst.Name = enumerator.Current.Value;
                enumerator.MoveNext();
                break;
            case TokenType.OpenParen:
                if (interfaceAst.ReturnTypeDefinition.Name == "*" || interfaceAst.ReturnTypeDefinition.Count != null)
                {
                    ErrorReporter.Report("Expected the interface name to be declared", interfaceAst.ReturnTypeDefinition);
                }
                else
                {
                    interfaceAst.Name = interfaceAst.ReturnTypeDefinition.Name;
                    foreach (var generic in interfaceAst.ReturnTypeDefinition.Generics)
                    {
                        if (generic.Generics.Any())
                        {
                            ErrorReporter.Report($"Invalid generic in interface '{interfaceAst.Name}'", generic);
                        }
                        else if (interfaceAst.Generics.Contains(generic.Name))
                        {
                            ErrorReporter.Report($"Duplicate generic '{generic.Name}' in interface '{interfaceAst.Name}'", generic.FileIndex, generic.Line, generic.Column);
                        }
                        else
                        {
                            interfaceAst.Generics.Add(generic.Name);
                        }
                    }
                    interfaceAst.ReturnTypeDefinition = null;
                }
                break;
            default:
                ErrorReporter.Report("Expected the interface name to be declared", enumerator.FileIndex, enumerator.Current);
                enumerator.MoveNext();
                break;
        }

        // 2. Parse generics
        if (enumerator.Current.Type == TokenType.LessThan)
        {
            ParseGenerics(enumerator, interfaceAst.Generics, interfaceAst.Name, "interface");
            enumerator.MoveNext();

            // Search for generics in the return type
            if (interfaceAst.ReturnTypeDefinition != null)
            {
                for (var i = 0; i < interfaceAst.Generics.Count; i++)
                {
                    var generic = interfaceAst.Generics[i];
                    if (SearchForGeneric(generic, i, interfaceAst.ReturnTypeDefinition))
                    {
                        interfaceAst.Flags |= FunctionFlags.ReturnTypeHasGenerics;
                    }
                }
            }
        }

        // 3. Parse arguments until a close paren
        var commaRequiredBeforeNextArgument = false;
        DeclarationAst currentArgument = null;
        while (enumerator.MoveNext())
        {
            token = enumerator.Current;

            if (token.Type == TokenType.CloseParen)
            {
                if (commaRequiredBeforeNextArgument)
                {
                    interfaceAst.Arguments.Add(currentArgument);
                    currentArgument = null;
                }
                break;
            }

            switch (token.Type)
            {
                case TokenType.Identifier:
                    if (commaRequiredBeforeNextArgument)
                    {
                        ErrorReporter.Report("Comma required after declaring an argument", enumerator.FileIndex, token);
                    }
                    else if (currentArgument == null)
                    {
                        currentArgument = CreateAst<DeclarationAst>(token, enumerator.FileIndex);
                        currentArgument.TypeDefinition = ParseType(enumerator);
                        for (var i = 0; i < interfaceAst.Generics.Count; i++)
                        {
                            var generic = interfaceAst.Generics[i];
                            if (SearchForGeneric(generic, i, currentArgument.TypeDefinition))
                            {
                                currentArgument.HasGenerics = true;
                            }
                        }
                    }
                    else
                    {
                        currentArgument.Name = token.Value;
                        commaRequiredBeforeNextArgument = true;
                    }
                    break;
                case TokenType.Comma:
                    if (commaRequiredBeforeNextArgument)
                    {
                        interfaceAst.Arguments.Add(currentArgument);
                    }
                    else
                    {
                        ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, token);
                    }
                    currentArgument = null;
                    commaRequiredBeforeNextArgument = false;
                    break;
                case TokenType.Equals:
                    if (commaRequiredBeforeNextArgument)
                    {
                        ErrorReporter.Report($"Interface '{interfaceAst.Name}' cannot have default argument values", enumerator.FileIndex, token);
                        while (enumerator.MoveNext())
                        {
                            if (enumerator.Current.Type == TokenType.Comma)
                            {
                                commaRequiredBeforeNextArgument = false;
                                interfaceAst.Arguments.Add(currentArgument);
                                currentArgument = null;
                                break;
                            }
                            else if (enumerator.Current.Type == TokenType.Comma)
                            {
                                interfaceAst.Arguments.Add(currentArgument);
                                currentArgument = null;
                                break;
                            }
                        }
                    }
                    else
                    {
                        ErrorReporter.Report("Unexpected token '=' in arguments", enumerator.FileIndex, token);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in arguments", enumerator.FileIndex, token);
                    break;
            }

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                break;
            }
        }

        if (currentArgument != null)
        {
            ErrorReporter.Report($"Incomplete argument in interface '{interfaceAst.Name}'", enumerator.FileIndex, enumerator.Current);
        }

        if (!commaRequiredBeforeNextArgument && interfaceAst.Arguments.Any())
        {
            ErrorReporter.Report("Unexpected comma in arguments", enumerator.FileIndex, enumerator.Current);
        }

        return interfaceAst;
    }

    private static void ParseGenerics(TokenEnumerator enumerator, List<string> genericList, string typeName, string typeKind)
    {
        var commaRequiredBeforeNextType = false;
        var generics = new HashSet<string>();
        while (enumerator.MoveNext())
        {
            var token = enumerator.Current;

            if (token.Type == TokenType.GreaterThan)
            {
                if (!commaRequiredBeforeNextType)
                {
                    ErrorReporter.Report($"Expected comma in generics for {typeKind} '{typeName}'", enumerator.FileIndex, token);
                }
                break;
            }

            if (!commaRequiredBeforeNextType)
            {
                switch (token.Type)
                {
                    case TokenType.Identifier:
                        if (!generics.Add(token.Value))
                        {
                            ErrorReporter.Report($"Duplicate generic '{token.Value}' in {typeKind} '{typeName}'", enumerator.FileIndex, token);
                        }
                        commaRequiredBeforeNextType = true;
                        break;
                    default:
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in generics for {typeKind} '{typeName}'", enumerator.FileIndex, token);
                        commaRequiredBeforeNextType = true;
                        break;
                }
            }
            else
            {
                switch (token.Type)
                {
                    case TokenType.Comma:
                        commaRequiredBeforeNextType = false;
                        break;
                    default:
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in definition of {typeKind} '{typeName}'", enumerator.FileIndex, token);
                        commaRequiredBeforeNextType = false;
                        break;
                }
            }
        }

        if (!generics.Any())
        {
            ErrorReporter.Report($"Expected ${typeKind} '{typeName}' to contain generics", enumerator.FileIndex, enumerator.Current);
        }
        genericList.AddRange(generics);
    }

    private static TypeDefinition ParseType(TokenEnumerator enumerator, IFunction currentFunction = null, bool argument = false, int depth = 0)
    {
        var typeDefinition = CreateAst<TypeDefinition>(enumerator);
        typeDefinition.Name = enumerator.Current.Value;

        // Alias int to s32
        if (typeDefinition.Name == "int")
        {
            typeDefinition.Name = "s32";
        }

        if (enumerator.Current.Type == TokenType.VarArgs)
        {
            if (!argument)
            {
                ErrorReporter.Report("Variable args type can only be used as an argument type", enumerator.FileIndex, enumerator.Current);
            }
            return typeDefinition;
        }

        // Determine whether to parse a generic type, otherwise return
        if (enumerator.Peek(out var token) && token.Type == TokenType.LessThan)
        {
            // Clear the '<' before entering loop
            enumerator.MoveNext();
            var commaRequiredBeforeNextType = false;
            while (enumerator.MoveNext())
            {
                token = enumerator.Current;

                if (token.Type == TokenType.GreaterThan)
                {
                    if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                    {
                        ErrorReporter.Report("Unexpected comma in type", enumerator.FileIndex, token);
                    }
                    break;
                }
                else if (token.Type == TokenType.ShiftRight)
                {
                    // Split the token and insert a greater than after the current token
                    token.Value = ">";
                    var newToken = new Token
                    {
                        Type = TokenType.GreaterThan, Value = ">", Line = token.Line, Column = token.Column + 1
                    };
                    enumerator.Insert(newToken);
                    break;
                }
                else if (token.Type == TokenType.RotateRight)
                {
                    // Split the token and insert a shift right after the current token
                    token.Value = ">";
                    var newToken = new Token
                    {
                        Type = TokenType.ShiftRight, Value = ">>", Line = token.Line, Column = token.Column + 1
                    };
                    enumerator.Insert(newToken);
                    break;
                }

                if (!commaRequiredBeforeNextType)
                {
                    switch (token.Type)
                    {
                        case TokenType.Identifier:
                            typeDefinition.Generics.Add(ParseType(enumerator, currentFunction, depth: depth + 1));
                            commaRequiredBeforeNextType = true;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in type definition", enumerator.FileIndex, token);
                            commaRequiredBeforeNextType = true;
                            break;
                    }
                }
                else
                {
                    switch (token.Type)
                    {
                        case TokenType.Comma:
                            commaRequiredBeforeNextType = false;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in type definition", enumerator.FileIndex, token);
                            commaRequiredBeforeNextType = false;
                            break;
                    }
                }
            }

            if (!typeDefinition.Generics.Any())
            {
                ErrorReporter.Report("Expected type to contain generics", enumerator.FileIndex, enumerator.Current);
            }
        }

        while (enumerator.Peek(out token) && token.Type == TokenType.Asterisk)
        {
            enumerator.MoveNext();
            var pointerType = CreateAst<TypeDefinition>(enumerator);
            pointerType.Name = "*";
            pointerType.Generics.Add(typeDefinition);
            typeDefinition = pointerType;
        }

        if (enumerator.Peek(out token) && token.Type == TokenType.OpenBracket)
        {
            // Skip over the open bracket and parse the expression
            enumerator.MoveNext();
            enumerator.MoveNext();
            typeDefinition.Count = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);
        }

        return typeDefinition;
    }

    private static bool TryParseType(TokenEnumerator enumerator, out TypeDefinition typeDef)
    {
        var steps = 0;
        if (TryParseType(enumerator.Current, enumerator, ref steps, out typeDef, out _, out _))
        {
            enumerator.Move(steps);
            return true;
        }
        return false;
    }

    private static bool TryParseType(Token name, TokenEnumerator enumerator, ref int steps, out TypeDefinition typeDefinition, out bool endsWithShift, out bool endsWithRotate, int depth = 0)
    {
        typeDefinition = CreateAst<TypeDefinition>(name, enumerator.FileIndex);
        typeDefinition.Name = name.Value;
        endsWithShift = false;
        endsWithRotate = false;

        // Alias int to s32
        if (typeDefinition.Name == "int")
        {
            typeDefinition.Name = "s32";
        }

        // Determine whether to parse a generic type, otherwise return
        if (enumerator.Peek(out var token, steps) && token.Type == TokenType.LessThan)
        {
            // Clear the '<' before entering loop
            steps++;
            var commaRequiredBeforeNextType = false;
            while (enumerator.Peek(out token, steps))
            {
                steps++;
                if (token.Type == TokenType.GreaterThan)
                {
                    if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                    {
                        return false;
                    }
                    break;
                }
                else if (token.Type == TokenType.ShiftRight)
                {
                    if ((depth % 3 != 1 && !endsWithShift) || (!commaRequiredBeforeNextType && typeDefinition.Generics.Any()))
                    {
                        return false;
                    }
                    endsWithShift = true;
                    break;
                }
                else if (token.Type == TokenType.RotateRight)
                {
                    if ((depth % 3 != 2 && !endsWithRotate) || (!commaRequiredBeforeNextType && typeDefinition.Generics.Any()))
                    {
                        return false;
                    }
                    endsWithRotate = true;
                    break;
                }

                if (!commaRequiredBeforeNextType)
                {
                    switch (token.Type)
                    {
                        case TokenType.Identifier:
                            if (!TryParseType(token, enumerator, ref steps, out var genericType, out endsWithShift, out endsWithRotate, depth + 1))
                            {
                                return false;
                            }
                            if (endsWithShift || endsWithRotate)
                            {
                                steps--;
                            }
                            typeDefinition.Generics.Add(genericType);
                            commaRequiredBeforeNextType = true;
                            break;
                        default:
                            return false;
                    }
                }
                else
                {
                    switch (token.Type)
                    {
                        case TokenType.Comma:
                            commaRequiredBeforeNextType = false;
                            break;
                        default:
                            return false;
                    }
                }
            }

            if (!typeDefinition.Generics.Any())
            {
                return false;
            }
        }

        while (enumerator.Peek(out token, steps) && token.Type == TokenType.Asterisk)
        {
            var pointerType = CreateAst<TypeDefinition>(token, enumerator.FileIndex);
            pointerType.Name = "*";
            pointerType.Generics.Add(typeDefinition);
            typeDefinition = pointerType;
            steps++;
            endsWithShift = false;
            endsWithRotate = false;
        }

        return true;
    }

    public static ConstantAst ParseConstant(Token token, int fileIndex)
    {
        var constant = CreateAst<ConstantAst>(token, fileIndex);
        switch (token.Type)
        {
            case TokenType.Literal:
                constant.Type = TypeTable.StringType;
                constant.String = token.Value;
                return constant;
            case TokenType.Character:
                constant.Type = TypeTable.U8Type;
                constant.Value = new Constant {UnsignedInteger = (byte)token.Value[0]};
                return constant;
            case TokenType.Number:
                if (token.Flags == TokenFlags.None)
                {
                    if (int.TryParse(token.Value, out var s32))
                    {
                        constant.Type = TypeTable.S32Type;
                        constant.Value = new Constant {Integer = s32};
                        return constant;
                    }

                    if (long.TryParse(token.Value, out var s64))
                    {
                        constant.Type = TypeTable.S64Type;
                        constant.Value = new Constant {Integer = s64};
                        return constant;
                    }

                    if (ulong.TryParse(token.Value, out var u64))
                    {
                        constant.Type = TypeTable.U64Type;
                        constant.Value = new Constant {UnsignedInteger = u64};
                        return constant;
                    }

                    ErrorReporter.Report($"Invalid integer '{token.Value}', must be 64 bits or less", fileIndex, token);
                    return null;
                }

                if (token.Flags.HasFlag(TokenFlags.Float))
                {
                    if (float.TryParse(token.Value, out var f32))
                    {
                        constant.Type = TypeTable.FloatType;
                        constant.Value = new Constant {Double = (double)f32};
                        return constant;
                    }

                    if (double.TryParse(token.Value, out var f64))
                    {
                        constant.Type = TypeTable.Float64Type;
                        constant.Value = new Constant {Double = f64};
                        return constant;
                    }

                    ErrorReporter.Report($"Invalid floating point number '{token.Value}', must be single or double precision", fileIndex, token);
                    return null;
                }

                if (token.Flags.HasFlag(TokenFlags.HexNumber))
                {
                    if (token.Value.Length == 2)
                    {
                        ErrorReporter.Report($"Invalid number '{token.Value}'", fileIndex, token);
                        return null;
                    }

                    var value = token.Value.Substring(2);
                    if (value.Length <= 8)
                    {
                        if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u32))
                        {
                            constant.Type = TypeTable.U32Type;
                            constant.Value = new Constant {UnsignedInteger = u32};
                            return constant;
                        }
                    }
                    else if (value.Length <= 16)
                    {
                        if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u64))
                        {
                            constant.Type = TypeTable.U64Type;
                            constant.Value = new Constant {UnsignedInteger = u64};
                            return constant;
                        }
                    }
                    ErrorReporter.Report($"Invalid integer '{token.Value}'", fileIndex, token);
                    return null;
                }
                ErrorReporter.Report($"Unable to determine type of token '{token.Value}'", fileIndex, token);
                return null;
            case TokenType.Boolean:
                constant.Type = TypeTable.BoolType;
                constant.Value = new Constant {Boolean = token.Value == "true"};
                return constant;
            default:
                ErrorReporter.Report($"Unable to determine type of token '{token.Value}'", fileIndex, token);
                return null;
        }
    }

    private static CastAst ParseCast(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var castAst = CreateAst<CastAst>(enumerator);

        // 1. Try to get the open paren to begin the cast
        if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.OpenParen)
        {
            ErrorReporter.Report("Expected '(' after 'cast'", enumerator.FileIndex, enumerator.Current);
            return null;
        }

        // 2. Get the target type
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected to get the target type for the cast", enumerator.FileIndex, enumerator.Last);
            return null;
        }
        castAst.TargetTypeDefinition = ParseType(enumerator, currentFunction);
        if (currentFunction != null)
        {
            for (var i = 0; i < currentFunction.Generics.Count; i++)
            {
                var generic = currentFunction.Generics[i];
                if (SearchForGeneric(generic, i, castAst.TargetTypeDefinition))
                {
                    castAst.HasGenerics = true;
                }
            }
        }

        // 3. Expect to get a comma
        if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.Comma)
        {
            ErrorReporter.Report("Expected ',' after type in cast", enumerator.FileIndex, enumerator.Current);
            return null;
        }

        // 4. Get the value expression
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected to get the value for the cast", enumerator.FileIndex, enumerator.Last);
            return null;
        }
        castAst.Value = ParseExpression(enumerator, currentFunction, null, TokenType.CloseParen);

        return castAst;
    }

    private static T CreateAst<T>(IAst source) where T : IAst, new()
    {
        if (source == null) return new();

        return new()
        {
            FileIndex = source.FileIndex,
            Line = source.Line,
            Column = source.Column
        };
    }

    private static T CreateAst<T>(TokenEnumerator enumerator) where T : IAst, new()
    {
        var token = enumerator.Current;
        return new()
        {
            FileIndex = enumerator.FileIndex,
            Line = token.Line,
            Column = token.Column
        };
    }

    private static T CreateAst<T>(Token token, int fileIndex) where T : IAst, new()
    {
        return new()
        {
            FileIndex = fileIndex,
            Line = token.Line,
            Column = token.Column
        };
    }

    private static Operator ConvertOperator(Token token)
    {
        switch (token.Type)
        {
            // Multi-character operators
            case TokenType.And:
                return Operator.And;
            case TokenType.Or:
                return Operator.Or;
            case TokenType.Equality:
                return Operator.Equality;
            case TokenType.NotEqual:
                return Operator.NotEqual;
            case TokenType.GreaterThanEqual:
                return Operator.GreaterThanEqual;
            case TokenType.LessThanEqual:
                return Operator.LessThanEqual;
            case TokenType.ShiftLeft:
                return Operator.ShiftLeft;
            case TokenType.ShiftRight:
                return Operator.ShiftRight;
            case TokenType.RotateLeft:
                return Operator.RotateLeft;
            case TokenType.RotateRight:
                return Operator.RotateRight;
            // Handle single character operators
            default:
                if (token.Value.Length != 1) return Operator.None;
                var op = (Operator)token.Value[0];
                return Enum.IsDefined(typeof(Operator), op) ? op : Operator.None;
        }
    }
}
