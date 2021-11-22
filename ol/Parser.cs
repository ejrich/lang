using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ol;

public static class Parser
{
    private static string _libraryDirectory;

    public static SafeLinkedList<IAst> Asts = new();

    private class TokenEnumerator
    {
        private readonly List<Token> _tokens;
        private int _index;

        public TokenEnumerator(List<Token> tokens)
        {
            _tokens = tokens;
        }

        public Token Current;
        public bool Remaining = true;

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
        _libraryDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
        AddModule("runtime");
        AddFile(entrypoint);

        ThreadPool.CompleteWork();
    }

    private static void AddModule(string module)
    {
        var file = Path.Combine(_libraryDirectory, $"{module}.ol");
        if (File.Exists(file))
        {
            QueueFileIfNotExists(file);
        }
    }

    private static void AddModule(string module, Token token)
    {
        var file = Path.Combine(_libraryDirectory, $"{module}.ol");
        if (File.Exists(file))
        {
            QueueFileIfNotExists(file);
        }
        else
        {
            ErrorReporter.Report($"Undefined module '{module}'", token);
        }
    }

    public static void AddModule(CompilerDirectiveAst module)
    {
        if (File.Exists(module.ImportPath))
        {
            QueueFileIfNotExists(module.ImportPath);
        }
        else
        {
            ErrorReporter.Report($"Undefined module '{module.Import}'", module);
        }
    }

    private static void AddFile(string file, string directory, Token token)
    {
        var filePath = Path.Combine(directory, file);
        AddFile(filePath, token);
    }

    private static void AddFile(string file, Token token = default)
    {
        if (File.Exists(file))
        {
            QueueFileIfNotExists(file);
        }
        else
        {
            ErrorReporter.Report($"File '{file}' does not exist", token);
        }
    }

    public static void AddFile(CompilerDirectiveAst import)
    {
        if (File.Exists(import.ImportPath))
        {
            QueueFileIfNotExists(import.ImportPath);
        }
        else
        {
            ErrorReporter.Report($"File '{import.ImportPath}' does not exist", import);
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

        var fileIndex = BuildSettings.Files.Count;
        BuildSettings.Files.Add(file);
        ThreadPool.QueueWork(ParseFile, new ParseData {File = file, FileIndex = fileIndex});
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
        var tokens = Lexer.LoadFileTokens(parseData.File, parseData.FileIndex);
        var directory = Path.GetDirectoryName(parseData.File);

        // 2. Iterate through tokens, tracking different ASTs
        var enumerator = new TokenEnumerator(tokens);
        while (enumerator.MoveNext())
        {
            var ast = ParseTopLevelAst(enumerator, directory);
            Asts.Add(ast);
        }
    }

    private static IAst ParseTopLevelAst(TokenEnumerator enumerator, string directory, bool global = true)
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
                            ErrorReporter.Report($"Global variables cannot have attributes", token);
                        }
                        return ParseDeclaration(enumerator);
                    }
                    return ParseFunction(enumerator, attributes);
                }
                ErrorReporter.Report($"Unexpected token '{token.Value}'", token);
                return null;
            case TokenType.Struct:
                return ParseStruct(enumerator, attributes);
            case TokenType.Enum:
                return ParseEnum(enumerator, attributes);
            case TokenType.Union:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Compiler directives cannot have attributes", token);
                }
                return ParseUnion(enumerator);
            case TokenType.Pound:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Compiler directives cannot have attributes", token);
                }
                return ParseTopLevelDirective(enumerator, directory, global);
            case TokenType.Operator:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Operator overloads cannot have attributes", token);
                }
                return ParseOperatorOverload(enumerator);
            case TokenType.Interface:
                if (attributes != null)
                {
                    ErrorReporter.Report($"Interfaces cannot have attributes", token);
                }
                return ParseInterface(enumerator);
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}'", token);
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
                        ErrorReporter.Report("Expected comma between attributes", token);
                    }
                    attributes.Add(token.Value);
                    commaRequired = true;
                    break;
                case TokenType.Comma:
                    if (!commaRequired)
                    {
                        ErrorReporter.Report("Expected attribute after comma or at beginning of attribute list", token);
                    }
                    commaRequired = false;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in attribute list", token);
                    break;
            }
        }

        if (attributes.Count == 0)
        {
            ErrorReporter.Report("Expected attribute(s) to be in attribute list", enumerator.Current);
        }
        else if (!commaRequired)
        {
            ErrorReporter.Report("Expected attribute after comma in attribute list", enumerator.Current);
        }
        enumerator.MoveNext();

        return attributes;
    }

    private static FunctionAst ParseFunction(TokenEnumerator enumerator, List<string> attributes)
    {
        // 1. Determine return type and name of the function
        var function = CreateAst<FunctionAst>(enumerator.Current);
        function.Attributes = attributes;

        // 1a. Check if the return type is void
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report($"Expected function definition", token);
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
            ErrorReporter.Report("Expected the function name to be declared", enumerator.Last);
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
                ErrorReporter.Report("Expected the function name to be declared", enumerator.Current);
                enumerator.MoveNext();
                break;
        }

        // 2. Parse generics
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
                        ErrorReporter.Report($"Expected comma in generics of function '{function.Name}'", token);
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
                                ErrorReporter.Report($"Duplicate generic '{token.Value}' in function '{function.Name}'", token);
                            }
                            commaRequiredBeforeNextType = true;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in generics of function '{function.Name}'", token);
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
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in function '{function.Name}'", token);
                            commaRequiredBeforeNextType = false;
                            break;
                    }
                }
            }

            if (!generics.Any())
            {
                ErrorReporter.Report("Expected function to contain generics", enumerator.Current);
            }
            enumerator.MoveNext();
            function.Generics.AddRange(generics);
        }

        // 3. Search for generics in the function return type
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

        // 4. Find open paren to start parsing arguments
        if (enumerator.Current.Type != TokenType.OpenParen)
        {
            // Add an error to the function AST and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in function definition", token);
            while (enumerator.Remaining && enumerator.Current.Type != TokenType.OpenParen)
                enumerator.MoveNext();
        }

        // 5. Parse arguments until a close paren
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
                        ErrorReporter.Report("Comma required after declaring an argument", token);
                    }
                    else if (currentArgument == null)
                    {
                        currentArgument = CreateAst<DeclarationAst>(token);
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
                        ErrorReporter.Report("Unexpected comma in arguments", token);
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
                            ErrorReporter.Report($"Incomplete definition for function '{function.Name}'", enumerator.Last);
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
                                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in arguments of function '{function.Name}'", enumerator.Current);
                                break;
                        }
                    }
                    else
                    {
                        ErrorReporter.Report("Unexpected comma in arguments", token);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in arguments", token);
                    break;
            }

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                break;
            }
        }

        if (currentArgument != null)
        {
            ErrorReporter.Report($"Incomplete function argument in function '{function.Name}'", enumerator.Current);
        }

        if (!commaRequiredBeforeNextArgument && function.Arguments.Any())
        {
            ErrorReporter.Report("Unexpected comma in arguments", enumerator.Current);
        }

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Unexpected function body or compiler directive", enumerator.Last);
            return null;
        }
        // 6. Handle compiler directives
        if (enumerator.Current.Type == TokenType.Pound)
        {
            if (!enumerator.MoveNext())
            {
                ErrorReporter.Report("Expected compiler directive value", enumerator.Last);
                return null;
            }

            switch (enumerator.Current.Value)
            {
                case "extern":
                    function.Flags |= FunctionFlags.Extern;
                    if (!enumerator.Peek(out token) || token.Type != TokenType.Literal)
                    {
                        ErrorReporter.Report("Extern function definition should be followed by the library in use", token);
                    }
                    else
                    {
                        enumerator.MoveNext();
                        function.ExternLib = enumerator.Current.Value;
                    }
                    return function;
                case "compiler":
                    function.Flags |= FunctionFlags.Compiler;
                    return function;
                case "print_ir":
                    function.Flags |= FunctionFlags.PrintIR;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.Current);
                    break;
            }
            enumerator.MoveNext();
        }

        // 7. Find open brace to start parsing body
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            // Add an error and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in function definition", token);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);

            if (!enumerator.Remaining)
            {
                return function;
            }
        }

        // 8. Parse function body
        function.Body = ParseScope(enumerator, function);

        return function;
    }

    private static StructAst ParseStruct(TokenEnumerator enumerator, List<string> attributes)
    {
        var structAst = CreateAst<StructAst>(enumerator.Current);
        structAst.Attributes = attributes;

        // 1. Determine name of struct
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected struct to have name", enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Type)
        {
            case TokenType.Identifier:
                structAst.Name = enumerator.Current.Value;
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in struct definition", enumerator.Current);
                break;
        }

        // 2. Parse struct generics
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report($"Unexpected struct body", token);
            return null;
        }

        if (token.Type == TokenType.LessThan)
        {
            // Clear the '<' before entering loop
            enumerator.MoveNext();
            var commaRequiredBeforeNextType = false;
            var generics = new HashSet<string>();
            while (enumerator.MoveNext())
            {
                token = enumerator.Current;

                if (token.Type == TokenType.GreaterThan)
                {
                    if (!commaRequiredBeforeNextType)
                    {
                        ErrorReporter.Report($"Expected comma in generics for struct '{structAst.Name}'", token);
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
                                ErrorReporter.Report($"Duplicate generic '{token.Value}' in struct '{structAst.Name}'", token);
                            }
                            commaRequiredBeforeNextType = true;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in generics for struct '{structAst.Name}'", token);
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
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in definition of struct '{structAst.Name}'", token);
                            commaRequiredBeforeNextType = false;
                            break;
                    }
                }
            }

            if (!generics.Any())
            {
                ErrorReporter.Report($"Expected struct '{structAst.Name}' to contain generics", enumerator.Current);
            }
            structAst.Generics = generics.ToList();
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
            ErrorReporter.Report("Expected '{' token in struct definition", enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 5. Iterate through fields
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Type == TokenType.CloseBrace)
            {
                break;
            }

            structAst.Fields.Add(ParseStructField(enumerator));
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

    private static StructFieldAst ParseStructField(TokenEnumerator enumerator)
    {
        var attributes = ParseAttributes(enumerator);
        var structField = CreateAst<StructFieldAst>(enumerator.Current);
        structField.Attributes = attributes;

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Expected name of struct field, but got '{enumerator.Current.Value}'", enumerator.Current);
        }
        structField.Name = enumerator.Current.Value;

        // 1. Expect to get colon
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.Colon)
        {
            var errorToken = enumerator.Current;
            ErrorReporter.Report($"Unexpected token in struct field '{errorToken.Value}'", errorToken);
            // Parse to a ; or }
            while (enumerator.MoveNext())
            {
                var tokenType = enumerator.Current.Type;
                if (tokenType == TokenType.SemiColon || tokenType == TokenType.CloseBrace)
                {
                    break;
                }
            }
            return structField;
        }

        // 2. Check if type is given
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Expected type of struct field", token);
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
            ErrorReporter.Report("Expected declaration to have value", enumerator.Last);
            return null;
        }

        token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Equals:
                ParseDeclarationValue(structField, enumerator, null);
                break;
            case TokenType.SemiColon:
                if (structField.TypeDefinition == null)
                {
                    ErrorReporter.Report("Expected declaration to have value", token);
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in declaration", token);
                // Parse until there is an equals sign
                while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.Equals);

                ParseDeclarationValue(structField, enumerator, null);
                break;
        }

        return structField;
    }

    private static EnumAst ParseEnum(TokenEnumerator enumerator, List<string> attributes)
    {
        var enumAst = CreateAst<EnumAst>(enumerator.Current);
        enumAst.Attributes = attributes;

        // 1. Determine name of enum
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected enum to have name", enumerator.Last);
            return null;
        }

        if (enumerator.Current.Type == TokenType.Identifier)
        {
            enumAst.Name = enumAst.BackendName = enumerator.Current.Value;
        }
        else
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in enum definition", enumerator.Current);
        }

        // 2. Parse over the open brace
        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Colon)
        {
            enumerator.MoveNext();
            enumAst.BaseTypeDefinition = ParseType(enumerator);
            enumerator.MoveNext();
        }

        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected '{' token in enum definition", enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 3. Iterate through fields
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
                        currentValue = CreateAst<EnumValueAst>(token);
                        currentValue.Name = token.Value;
                    }
                    else if (parsingValueDefault)
                    {
                        parsingValueDefault = false;
                        var found = false;
                        foreach (var value in enumAst.Values)
                        {
                            if (token.Value == value.Name)
                            {
                                if (!value.Defined)
                                {
                                    ErrorReporter.Report($"Expected previously defined value '{token.Value}' to have a defined value", token);
                                }
                                currentValue.Value = value.Value;
                                currentValue.Defined = true;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            ErrorReporter.Report($"Expected value '{token.Value}' to be previously defined in enum '{enumAst.Name}'", token);
                        }
                    }
                    else
                    {
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
                    }
                    break;
                case TokenType.SemiColon:
                    if (currentValue != null)
                    {
                        // Catch if the name hasn't been set
                        if (currentValue.Name == null || parsingValueDefault)
                        {
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
                        }
                        // Add the value to the enum and continue
                        enumAst.Values.Add(currentValue);
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
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
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
                                ErrorReporter.Report($"Expected enum value to be an integer, but got '{token.Value}'", token);
                            }
                        }
                        else if (token.Flags.HasFlag(TokenFlags.Float))
                        {
                            ErrorReporter.Report($"Expected enum value to be an integer, but got '{token.Value}'", token);
                        }
                        else if (token.Flags.HasFlag(TokenFlags.HexNumber))
                        {
                            if (token.Value.Length == 2)
                            {
                                ErrorReporter.Report($"Invalid number '{token.Value}'", token);
                            }

                            var value = token.Value.Substring(2);
                            if (value.Length <= 8)
                            {
                                if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u32))
                                {
                                    currentValue.Value = (int)u32;
                                    currentValue.Defined = true;
                                }
                            }
                            else if (value.Length <= 16)
                            {
                                if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u64))
                                {
                                    currentValue.Value = (int)u64;
                                    currentValue.Defined = true;
                                }
                            }
                            else
                            {
                                ErrorReporter.Report($"Expected enum value to be an integer, but got '{token.Value}'", token);
                            }
                        }
                        parsingValueDefault = false;
                    }
                    else
                    {
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
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
                        ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
                    break;
            }
        }

        if (currentValue != null)
        {
            var token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in enum", token);
        }

        return enumAst;
    }

    private static UnionAst ParseUnion(TokenEnumerator enumerator)
    {
        var union = CreateAst<UnionAst>(enumerator.Current);

        // 1. Determine name of enum
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected union to have name", enumerator.Last);
            return null;
        }

        if (enumerator.Current.Type == TokenType.Identifier)
        {
            union.Name = union.BackendName = enumerator.Current.Value;
        }
        else
        {
            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in union definition", enumerator.Current);
        }

        // 2. Parse over the open brace
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            ErrorReporter.Report("Expected '{' token in union definition", enumerator.Current);
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

            union.Fields.Add(ParseUnionField(enumerator));
        }

        return union;
    }

    private static UnionFieldAst ParseUnionField(TokenEnumerator enumerator)
    {
        var field = CreateAst<UnionFieldAst>(enumerator.Current);

        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Expected name of union field, but got '{enumerator.Current.Value}'", enumerator.Current);
        }
        field.Name = enumerator.Current.Value;

        // 1. Expect to get colon
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.Colon)
        {
            var errorToken = enumerator.Current;
            ErrorReporter.Report($"Unexpected token in union field '{errorToken.Value}'", errorToken);
            // Parse to a ; or }
            while (enumerator.MoveNext())
            {
                var tokenType = enumerator.Current.Type;
                if (tokenType == TokenType.SemiColon || tokenType == TokenType.CloseBrace)
                {
                    break;
                }
            }
            return field;
        }

        // 2. Check if type is given
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Unexpected type of union field", token);
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
            ErrorReporter.Report("Expected union to be closed by a '}'", token);
            return null;
        }

        token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.SemiColon:
                if (field.TypeDefinition == null)
                {
                    ErrorReporter.Report("Expected union field to have a type", token);
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in declaration", token);
                // Parse until there is a semicolon or closing brace
                while (enumerator.MoveNext())
                {
                    token = enumerator.Current;
                    if (token.Type == TokenType.SemiColon || token.Type == TokenType.CloseBrace)
                    {
                        break;
                    }
                }
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

    private static IAst ParseLine(TokenEnumerator enumerator, IFunction currentFunction)
    {
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("End of file reached without closing scope", enumerator.Last);
            return null;
        }

        var token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Return:
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
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.Last);
                    return null;
                }
                switch (token.Type)
                {
                    case TokenType.OpenParen:
                        return ParseCall(enumerator, currentFunction, true);
                    case TokenType.Colon:
                        return ParseDeclaration(enumerator, currentFunction);
                    case TokenType.Equals:
                        return ParseAssignment(enumerator, currentFunction);
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
            case TokenType.Break:
                var breakAst = CreateAst<BreakAst>(token);
                if (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type != TokenType.SemiColon)
                    {
                        ErrorReporter.Report("Expected ';'", enumerator.Current);
                    }
                }
                else
                {
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.Last);
                }
                return breakAst;
            case TokenType.Continue:
                var continueAst = CreateAst<ContinueAst>(token);
                if (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type != TokenType.SemiColon)
                    {
                        ErrorReporter.Report("Expected ';'", enumerator.Current);
                    }
                }
                else
                {
                    ErrorReporter.Report("End of file reached without closing scope", enumerator.Last);
                }
                return continueAst;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}'", token);
                return null;
        }
    }

    private static ScopeAst ParseScope(TokenEnumerator enumerator, IFunction currentFunction, bool topLevel = false, string directory = null)
    {
        var scopeAst = CreateAst<ScopeAst>(enumerator.Current);

        var closed = false;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Type == TokenType.CloseBrace)
            {
                closed = true;
                break;
            }

            scopeAst.Children.Add(topLevel ? ParseTopLevelAst(enumerator, directory, false) : ParseLine(enumerator, currentFunction));
        }

        if (!closed)
        {
            ErrorReporter.Report("Scope not closed by '}'", enumerator.Current);
        }

        return scopeAst;
    }

    private static ConditionalAst ParseConditional(TokenEnumerator enumerator, IFunction currentFunction, bool topLevel = false, string directory = null)
    {
        var conditionalAst = CreateAst<ConditionalAst>(enumerator.Current);

        // 1. Parse the conditional expression by first iterating over the initial 'if'
        enumerator.MoveNext();
        conditionalAst.Condition = ParseConditionExpression(enumerator, currentFunction);

        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected if to contain conditional expression and body", enumerator.Last);
            return null;
        }
        // 2. Determine how many lines to parse
        switch (enumerator.Current.Type)
        {
            case TokenType.Then:
                // Parse single AST
                conditionalAst.IfBlock = CreateAst<ScopeAst>(enumerator.Current);
                enumerator.MoveNext();
                conditionalAst.IfBlock.Children.Add(topLevel ? ParseTopLevelAst(enumerator, directory, false) : ParseLine(enumerator, currentFunction));
                break;
            case TokenType.OpenBrace:
                // Parse until close brace
                conditionalAst.IfBlock = ParseScope(enumerator, currentFunction, topLevel, directory);
                break;
            default:
                // Parse single AST
                conditionalAst.IfBlock = CreateAst<ScopeAst>(enumerator.Current);
                conditionalAst.IfBlock.Children.Add(topLevel ? ParseTopLevelAst(enumerator, directory, false) : ParseLine(enumerator, currentFunction));
                break;
        }

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
                ErrorReporter.Report("Expected body of else branch", enumerator.Last);
                return null;
            }
            switch (enumerator.Current.Type)
            {
                case TokenType.Then:
                    // Parse single AST
                    conditionalAst.ElseBlock = CreateAst<ScopeAst>(enumerator.Current);
                    enumerator.MoveNext();
                    conditionalAst.ElseBlock.Children.Add(topLevel ? ParseTopLevelAst(enumerator, directory, false) : ParseLine(enumerator, currentFunction));
                    break;
                case TokenType.OpenBrace:
                    // Parse until close brace
                    conditionalAst.ElseBlock = ParseScope(enumerator, currentFunction, topLevel, directory);
                    break;
                default:
                    // Parse single AST
                    conditionalAst.ElseBlock = CreateAst<ScopeAst>(enumerator.Current);
                    conditionalAst.ElseBlock.Children.Add(topLevel ? ParseTopLevelAst(enumerator, directory, false) : ParseLine(enumerator, currentFunction));
                    break;
            }
        }

        return conditionalAst;
    }

    private static WhileAst ParseWhile(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var whileAst = CreateAst<WhileAst>(enumerator.Current);

        // 1. Parse the conditional expression by first iterating over the initial 'while'
        enumerator.MoveNext();
        whileAst.Condition = ParseConditionExpression(enumerator, currentFunction);

        // 2. Determine how many lines to parse
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected while loop to contain body", enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Type)
        {
            case TokenType.Then:
                // Parse single AST
                whileAst.Body = CreateAst<ScopeAst>(enumerator.Current);
                enumerator.MoveNext();
                whileAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
                break;
            case TokenType.OpenBrace:
                // Parse until close brace
                whileAst.Body = ParseScope(enumerator, currentFunction);
                break;
            default:
                // Parse single AST
                whileAst.Body = CreateAst<ScopeAst>(enumerator.Current);
                whileAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
                break;
        }

        return whileAst;
    }

    private static EachAst ParseEach(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var eachAst = CreateAst<EachAst>(enumerator.Current);

        // 1. Parse the iteration variable by first iterating over the initial 'each'
        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Identifier)
        {
            eachAst.IterationVariable = CreateAst<VariableAst>(enumerator.Current);
            eachAst.IterationVariable.Name = enumerator.Current.Value;
        }
        else
        {
            ErrorReporter.Report("Expected variable in each block definition", enumerator.Current);
        }

        enumerator.MoveNext();
        if (enumerator.Current.Type == TokenType.Comma)
        {
            enumerator.MoveNext();
            if (enumerator.Current.Type == TokenType.Identifier)
            {
                eachAst.IndexVariable = CreateAst<VariableAst>(enumerator.Current);
                eachAst.IndexVariable.Name = enumerator.Current.Value;
                enumerator.MoveNext();
            }
            else
            {
                ErrorReporter.Report("Expected index variable after comma in each declaration", enumerator.Current);
            }
        }

        // 2. Parse over the in keyword
        if (enumerator.Current.Type != TokenType.In)
        {
            ErrorReporter.Report("Expected 'in' token in each block", enumerator.Current);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.In);
        }

        // 3. Determine the iterator
        enumerator.MoveNext();
        var expression = ParseConditionExpression(enumerator, currentFunction);

        // 3a. Check if the next token is a range
        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected each block to have iteration and body", enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Type)
        {
            case TokenType.Range:
                if (eachAst.IndexVariable != null)
                {
                    ErrorReporter.Report("Range enumerators cannot have iteration and index variable", enumerator.Current);
                }

                eachAst.RangeBegin = expression;
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report("Expected range to have an end", enumerator.Last);
                    return eachAst;
                }

                eachAst.RangeEnd = ParseConditionExpression(enumerator, currentFunction);
                if (!enumerator.Remaining)
                {
                    ErrorReporter.Report("Expected each block to have iteration and body", enumerator.Last);
                    return eachAst;
                }
                break;
            default:
                eachAst.Iteration = expression;
                break;
        }

        // 4. Determine how many lines to parse
        switch (enumerator.Current.Type)
        {
            case TokenType.Then:
                // Parse single AST
                eachAst.Body = CreateAst<ScopeAst>(enumerator.Current);
                enumerator.MoveNext();
                eachAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
                break;
            case TokenType.OpenBrace:
                // Parse until close brace
                eachAst.Body = ParseScope(enumerator, currentFunction);
                break;
            default:
                eachAst.Body = CreateAst<ScopeAst>(enumerator.Current);
                eachAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
                break;
        }

        return eachAst;
    }

    private static IAst ParseConditionExpression(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var expression = CreateAst<ExpressionAst>(enumerator.Current);
        var operatorRequired = false;

        do
        {
            var token = enumerator.Current;

            if (token.Type == TokenType.Then || token.Type == TokenType.OpenBrace)
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
                    var constant = ParseConstant(token);
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

    private static DeclarationAst ParseDeclaration(TokenEnumerator enumerator, IFunction currentFunction = null)
    {
        var declaration = CreateAst<DeclarationAst>(enumerator.Current);
        if (enumerator.Current.Type != TokenType.Identifier)
        {
            ErrorReporter.Report($"Expected variable name to be an identifier, but got '{enumerator.Current.Value}'", enumerator.Current);
        }
        declaration.Name = enumerator.Current.Value;

        // 1. Expect to get colon
        enumerator.MoveNext();
        if (enumerator.Current.Type != TokenType.Colon)
        {
            var errorToken = enumerator.Current;
            ErrorReporter.Report($"Unexpected token in declaration '{errorToken.Value}'", errorToken);
            return declaration;
        }

        // 2. Check if type is given
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Expected type or value of declaration", token);
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
            ErrorReporter.Report("Expected declaration to have value", enumerator.Last);
            return declaration;
        }

        token = enumerator.Current;
        switch (token.Type)
        {
            case TokenType.Equals:
                ParseDeclarationValue(declaration, enumerator, currentFunction);
                break;
            case TokenType.SemiColon:
                if (declaration.TypeDefinition == null)
                {
                    ErrorReporter.Report("Expected token declaration to have value", token);
                }
                break;
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in declaration", token);
                // Parse until there is an equals sign
                while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.Equals);

                ParseDeclarationValue(declaration, enumerator, currentFunction);
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

    private static void ParseDeclarationValue(IDeclaration declaration, TokenEnumerator enumerator, IFunction currentFunction)
    {
        // 1. Step over '=' sign
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected declaration to have a value", enumerator.Last);
            return;
        }

        // 2. Parse expression, constant, or object/array initialization as the value
        switch (enumerator.Current.Type)
        {
            case TokenType.OpenBrace:
                declaration.Assignments = new Dictionary<string, AssignmentAst>();
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;
                    if (token.Type == TokenType.CloseBrace)
                    {
                        break;
                    }

                    var assignment = ParseAssignment(enumerator, currentFunction);
                    if (!declaration.Assignments.TryAdd(token.Value, assignment))
                    {
                        ErrorReporter.Report($"Multiple assignments for field '{token.Value}'", token);
                    }
                    if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        break;
                    }
                }
                break;
            case TokenType.OpenBracket:
                declaration.ArrayValues = new List<IAst>();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == TokenType.CloseBracket)
                    {
                        break;
                    }

                    var value = ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseBracket);
                    declaration.ArrayValues.Add(value);
                    if (enumerator.Current.Type == TokenType.CloseBracket)
                    {
                        break;
                    }
                }
                break;
            default:
                declaration.Value = ParseExpression(enumerator, currentFunction);
                break;
        }
    }

    private static AssignmentAst ParseAssignment(TokenEnumerator enumerator, IFunction currentFunction, IAst reference = null)
    {
        // 1. Set the variable
        var assignment = reference == null ? CreateAst<AssignmentAst>(enumerator.Current) : CreateAst<AssignmentAst>(reference);
        assignment.Reference = reference;

        // 2. When the original reference is null, set the l-value to an identifier
        if (reference == null)
        {
            var variableAst = CreateAst<IdentifierAst>(enumerator.Current);
            if (enumerator.Current.Type != TokenType.Identifier)
            {
                ErrorReporter.Report($"Expected variable name to be an identifier, but got '{enumerator.Current.Value}'", enumerator.Current);
            }
            variableAst.Name = enumerator.Current.Value;
            assignment.Reference = variableAst;

            // 2a. Expect to get equals sign
            if (!enumerator.MoveNext())
            {
                ErrorReporter.Report("Expected '=' in assignment'", enumerator.Last);
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
                        ErrorReporter.Report("Expected '=' in assignment'", token);
                        return null;
                    }
                    if (token.Type == TokenType.Equals)
                    {
                        enumerator.MoveNext();
                    }
                    else
                    {
                        ErrorReporter.Report("Expected '=' in assignment'", token);
                    }
                }
                else
                {
                    ErrorReporter.Report("Expected operator in assignment", token);
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

        // 4. Step over '=' sign
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected to have a value", enumerator.Last);
            return null;
        }

        // 5. Parse expression, constant, or another token as the value
        assignment.Value = ParseExpression(enumerator, currentFunction);

        return assignment;
    }

    private static IAst ParseExpression(TokenEnumerator enumerator, IFunction currentFunction, ExpressionAst initial = null, params TokenType[] endToken)
    {
        var operatorRequired = initial != null;

        var expression = initial ?? CreateAst<ExpressionAst>(enumerator.Current);
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
                return ParseAssignment(enumerator, currentFunction, expression);
            }
            else if (token.Type == TokenType.Comma)
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
                    var constant = ParseConstant(token);
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
                    ErrorReporter.Report($"Unexpected token '{token.Value}' when operator was expected", token);
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
            ErrorReporter.Report("Expression should contain elements", enumerator.Current);
        }
        else if (!operatorRequired && expression.Children.Any())
        {
            ErrorReporter.Report("Value required after operator", enumerator.Current);
            return expression;
        }

        if (expression.Children.Count == 1)
        {
            return expression.Children.First();
        }

        if (!ErrorReporter.Errors.Any())
        {
            SetOperatorPrecedence(expression);
        }
        return expression;
    }

    private static IAst ParseCompoundExpression(TokenEnumerator enumerator, IFunction currentFunction, IAst initial)
    {
        var compoundExpression = CreateAst<CompoundExpressionAst>(initial);
        compoundExpression.Children.Add(initial);
        var firstToken = enumerator.Current;

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected compound expression to contain multiple values", firstToken);
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
                        ErrorReporter.Report("Expected compound expression to contain multiple values", firstToken);
                    }
                    return ParseAssignment(enumerator, currentFunction, compoundExpression);
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
                        ErrorReporter.Report("Expected declaration to contain type and/or value", enumerator.Last);
                        return null;
                    }

                    if (enumerator.Current.Type == TokenType.Identifier)
                    {
                        compoundDeclaration.TypeDefinition = ParseType(enumerator);
                        enumerator.MoveNext();
                    }

                    if (!enumerator.Remaining)
                    {
                        return compoundDeclaration;
                    }
                    switch (enumerator.Current.Type)
                    {
                        case TokenType.Equals:
                            ParseDeclarationValue(compoundDeclaration, enumerator, currentFunction);
                            break;
                        case TokenType.SemiColon:
                            if (compoundDeclaration.TypeDefinition == null)
                            {
                                ErrorReporter.Report("Expected token declaration to have type and/or value", token);
                            }
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{enumerator.Current.Value}' in declaration", enumerator.Current);
                            // Parse until there is an equals sign
                            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.Equals);

                            ParseDeclarationValue(compoundDeclaration, enumerator, currentFunction);
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
            ErrorReporter.Report("Expected compound expression to contain multiple values", firstToken);
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
                return ParseConstant(token);
            case TokenType.Null:
                return CreateAst<NullAst>(token);
            case TokenType.Identifier:
                // Parse variable, call, or expression
                if (!enumerator.Peek(out var nextToken))
                {
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", token);
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

                                foreach (var generic in callAst.Generics)
                                {
                                    for (var i = 0; i < currentFunction.Generics.Count; i++)
                                    {
                                        SearchForGeneric(currentFunction.Generics[i], i, generic);
                                    }
                                }

                                enumerator.MoveNext();
                                ParseArguments(callAst, enumerator, currentFunction);
                                return callAst;
                            }
                            else
                            {
                                for (var i = 0; i < currentFunction.Generics.Count; i++)
                                {
                                    SearchForGeneric(currentFunction.Generics[i], i, typeDefinition);
                                }
                                return typeDefinition;
                            }
                        }
                        break;
                    }
                }
                var identifier = CreateAst<IdentifierAst>(token);
                identifier.Name = token.Value;
                return identifier;
            case TokenType.Increment:
            case TokenType.Decrement:
                var positive = token.Type == TokenType.Increment;
                if (enumerator.MoveNext())
                {
                    var changeByOneAst = CreateAst<ChangeByOneAst>(enumerator.Current);
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
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", token);
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
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", token);
                    return null;
                }
            case TokenType.Not:
            case TokenType.Minus:
            case TokenType.Asterisk:
            case TokenType.Ampersand:
                if (enumerator.MoveNext())
                {
                    var unaryAst = CreateAst<UnaryAst>(token);
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
                    ErrorReporter.Report($"Expected token to follow '{token.Value}'", token);
                    return null;
                }
            case TokenType.Cast:
                return ParseCast(enumerator, currentFunction);
            default:
                ErrorReporter.Report($"Unexpected token '{token.Value}' in expression", token);
                operatorRequired = false;
                return null;
       }
    }

    private static void SetOperatorPrecedence(ExpressionAst expression)
    {
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
        var callAst = CreateAst<CallAst>(enumerator.Current);
        callAst.Name = enumerator.Current.Value;

        // This enumeration is the open paren
        enumerator.MoveNext();
        // Enumerate over the first argument
        ParseArguments(callAst, enumerator, currentFunction);

        if (!enumerator.Remaining)
        {
            ErrorReporter.Report("Expected to close call", enumerator.Last);
        }
        else if (requiresSemicolon)
        {
            if (enumerator.Current.Type == TokenType.SemiColon)
                return callAst;

            if (!enumerator.Peek(out var token) || token.Type != TokenType.SemiColon)
            {
                ErrorReporter.Report("Expected ';'", token);
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
                    ErrorReporter.Report($"Expected argument in call following a comma", token);
                }
                break;
            }

            if (token.Type == TokenType.Comma)
            {
                ErrorReporter.Report("Expected comma before next argument", token);
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
                        ErrorReporter.Report($"Specified argument '{token.Value}' is already in the call", token);
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
                    ErrorReporter.Report("Expected to close call with ')'", enumerator.Current);
                    break;
                }
            }
        }
    }

    private static ReturnAst ParseReturn(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var returnAst = CreateAst<ReturnAst>(enumerator.Current);

        if (enumerator.MoveNext())
        {
            if (enumerator.Current.Type != TokenType.SemiColon)
            {
                returnAst.Value = ParseExpression(enumerator, currentFunction);
            }
        }
        else
        {
            ErrorReporter.Report("Return does not have value", enumerator.Last);
        }

        return returnAst;
    }

    private static IndexAst ParseIndex(TokenEnumerator enumerator, IFunction currentFunction)
    {
        // 1. Initialize the index ast
        var index = CreateAst<IndexAst>(enumerator.Current);
        index.Name = enumerator.Current.Value;
        enumerator.MoveNext();

        // 2. Parse index expression
        enumerator.MoveNext();
        index.Index = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);

        return index;
    }

    private static IAst ParseTopLevelDirective(TokenEnumerator enumerator, string directory, bool global)
    {
        var directive = CreateAst<CompilerDirectiveAst>(enumerator.Current);

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected compiler directive to have a value", enumerator.Last);
            return null;
        }

        var token = enumerator.Current;
        switch (token.Value)
        {
            case "run":
                directive.Type = DirectiveType.Run;
                enumerator.MoveNext();
                var ast = ParseLine(enumerator, null);
                if (ast != null)
                    directive.Value = ast;
                break;
            case "if":
                directive.Type = DirectiveType.If;
                directive.Value = ParseConditional(enumerator, null, true, directory);
                break;
            case "assert":
                directive.Type = DirectiveType.Assert;
                enumerator.MoveNext();
                directive.Value = ParseExpression(enumerator, null);
                break;
            case "import":
                if (!enumerator.MoveNext())
                {
                    ErrorReporter.Report($"Expected module name or source file", enumerator.Last);
                    return null;
                }
                token = enumerator.Current;
                switch (token.Type)
                {
                    case TokenType.Identifier:
                        directive.Type = DirectiveType.ImportModule;
                        directive.Import = token.Value;
                        if (global)
                        {
                            AddModule(token.Value, token);
                        }
                        else
                        {
                            directive.ImportPath = Path.Combine(_libraryDirectory, $"{token.Value}.ol");
                        }
                        break;
                    case TokenType.Literal:
                        directive.Type = DirectiveType.ImportFile;
                        directive.Import = token.Value;
                        if (global)
                        {
                            AddFile(token.Value, directory, token);
                        }
                        else
                        {
                            directive.ImportPath = Path.Combine(directory, token.Value);
                        }
                        break;
                    default:
                        ErrorReporter.Report($"Expected module name or source file, but got '{token.Value}'", token);
                        break;
                }
                break;
            default:
                ErrorReporter.Report($"Unsupported top-level compiler directive '{token.Value}'", token);
                return null;
        }

        return directive;
    }

    private static IAst ParseCompilerDirective(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var directive = CreateAst<CompilerDirectiveAst>(enumerator.Current);

        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected compiler directive to have a value", enumerator.Last);
            return null;
        }

        var token = enumerator.Current;
        switch (token.Value)
        {
            case "if":
                directive.Type = DirectiveType.If;
                directive.Value = ParseConditional(enumerator, currentFunction);
                currentFunction.Flags |= FunctionFlags.HasDirectives;
                break;
            case "assert":
                directive.Type = DirectiveType.Assert;
                enumerator.MoveNext();
                directive.Value = ParseExpression(enumerator, currentFunction);
                currentFunction.Flags |= FunctionFlags.HasDirectives;
                break;
            default:
                ErrorReporter.Report($"Unsupported compiler directive '{token.Value}'", token);
                return null;
        }

        return directive;
    }

    private static OperatorOverloadAst ParseOperatorOverload(TokenEnumerator enumerator)
    {
        var overload = CreateAst<OperatorOverloadAst>(enumerator.Current);
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected an operator be specified to overload", enumerator.Last);
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
                ErrorReporter.Report($"Expected an operator to be be specified, but got '{enumerator.Current.Value}'", enumerator.Current);
            }
        }
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report($"Expected to get the type to overload the operator", enumerator.Last);
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
                        ErrorReporter.Report($"Expected comma in generics", token);
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
                                ErrorReporter.Report($"Duplicate generic '{token.Value}'", token);
                            }
                            commaRequiredBeforeNextType = true;
                            break;
                        default:
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in generics", token);
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
                            ErrorReporter.Report($"Unexpected token '{token.Value}' when defining generics", token);
                            commaRequiredBeforeNextType = false;
                            break;
                    }
                }
            }

            if (!generics.Any())
            {
                ErrorReporter.Report("Expected operator overload to contain generics", enumerator.Current);
            }
            enumerator.MoveNext();
            overload.Generics.AddRange(generics);
        }

        // 3. Find open paren to start parsing arguments
        if (enumerator.Current.Type != TokenType.OpenParen)
        {
            // Add an error to the function AST and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in operator overload definition", token);
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
                        ErrorReporter.Report("Comma required after declaring an argument", token);
                    }
                    else if (currentArgument == null)
                    {
                        currentArgument = CreateAst<DeclarationAst>(token);
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
                        ErrorReporter.Report("Unexpected comma in arguments", token);
                    }
                    currentArgument = null;
                    commaRequiredBeforeNextArgument = false;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in arguments", token);
                    break;
            }

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                break;
            }
        }

        if (currentArgument != null)
        {
            ErrorReporter.Report($"Incomplete argument in overload for type '{overload.Type.Name}'", enumerator.Current);
        }

        if (!commaRequiredBeforeNextArgument && overload.Arguments.Any())
        {
            ErrorReporter.Report("Unexpected comma in arguments", enumerator.Current);
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
                    ErrorReporter.Report($"Unexpected to define return type for subscript", enumerator.Current);
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
                ErrorReporter.Report("Expected compiler directive value", enumerator.Last);
                return null;
            }
            switch (enumerator.Current.Value)
            {
                case "print_ir":
                    overload.Flags |= FunctionFlags.PrintIR;
                    break;
                default:
                    ErrorReporter.Report($"Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.Current);
                    break;
            }
            enumerator.MoveNext();
        }

        // 7. Find open brace to start parsing body
        if (enumerator.Current.Type != TokenType.OpenBrace)
        {
            // Add an error and continue until open paren
            token = enumerator.Current;
            ErrorReporter.Report($"Unexpected token '{token.Value}' in operator overload definition", token);
            while (enumerator.MoveNext() && enumerator.Current.Type != TokenType.OpenBrace);
        }

        // 8. Parse body
        overload.Body = ParseScope(enumerator, overload);

        return overload;
    }

    private static InterfaceAst ParseInterface(TokenEnumerator enumerator)
    {
        var interfaceAst = CreateAst<InterfaceAst>(enumerator.Current);
        enumerator.MoveNext();

        // 1a. Check if the return type is void
        if (!enumerator.Peek(out var token))
        {
            ErrorReporter.Report("Expected interface definition", token);
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
            ErrorReporter.Report("Expected the interface name to be declared", enumerator.Last);
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
                    if (interfaceAst.ReturnTypeDefinition.Generics.Any())
                    {
                        ErrorReporter.Report($"Interface '{interfaceAst.Name}' cannot have generics", interfaceAst.ReturnTypeDefinition);
                    }
                    interfaceAst.ReturnTypeDefinition = null;
                }
                break;
            default:
                ErrorReporter.Report("Expected the interface name to be declared", enumerator.Current);
                enumerator.MoveNext();
                break;
        }

        // 2. Parse arguments until a close paren
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
                        ErrorReporter.Report("Comma required after declaring an argument", token);
                    }
                    else if (currentArgument == null)
                    {
                        currentArgument = CreateAst<DeclarationAst>(token);
                        currentArgument.TypeDefinition = ParseType(enumerator);
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
                        ErrorReporter.Report("Unexpected comma in arguments", token);
                    }
                    currentArgument = null;
                    commaRequiredBeforeNextArgument = false;
                    break;
                case TokenType.Equals:
                    if (commaRequiredBeforeNextArgument)
                    {
                        ErrorReporter.Report($"Interface '{interfaceAst.Name}' cannot have default argument values", token);
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
                        ErrorReporter.Report("Unexpected token '=' in arguments", token);
                    }
                    break;
                default:
                    ErrorReporter.Report($"Unexpected token '{token.Value}' in arguments", token);
                    break;
            }

            if (enumerator.Current.Type == TokenType.CloseParen)
            {
                break;
            }
        }

        if (currentArgument != null)
        {
            ErrorReporter.Report($"Incomplete argument in interface '{interfaceAst.Name}'", enumerator.Current);
        }

        if (!commaRequiredBeforeNextArgument && interfaceAst.Arguments.Any())
        {
            ErrorReporter.Report("Unexpected comma in arguments", enumerator.Current);
        }

        return interfaceAst;
    }

    private static TypeDefinition ParseType(TokenEnumerator enumerator, IFunction currentFunction = null, bool argument = false, int depth = 0)
    {
        var typeDefinition = CreateAst<TypeDefinition>(enumerator.Current);
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
                ErrorReporter.Report("Variable args type can only be used as an argument type", enumerator.Current);
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
                        ErrorReporter.Report("Unexpected comma in type", token);
                    }
                    break;
                }
                else if (token.Type == TokenType.ShiftRight)
                {
                    // Split the token and insert a greater than after the current token
                    token.Value = ">";
                    var newToken = new Token
                    {
                        Type = TokenType.GreaterThan, Value = ">",
                        FileIndex = token.FileIndex, Line = token.Line, Column = token.Column + 1
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
                        Type = TokenType.ShiftRight, Value = ">>",
                        FileIndex = token.FileIndex, Line = token.Line, Column = token.Column + 1
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
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in type definition", token);
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
                            ErrorReporter.Report($"Unexpected token '{token.Value}' in type definition", token);
                            commaRequiredBeforeNextType = false;
                            break;
                    }
                }
            }

            if (!typeDefinition.Generics.Any())
            {
                ErrorReporter.Report("Expected type to contain generics", enumerator.Current);
            }
        }

        while (enumerator.Peek(out token) && token.Type == TokenType.Asterisk)
        {
            enumerator.MoveNext();
            var pointerType = CreateAst<TypeDefinition>(enumerator.Current);
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
        typeDefinition = CreateAst<TypeDefinition>(name);
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
            var pointerType = CreateAst<TypeDefinition>(token);
            pointerType.Name = "*";
            pointerType.Generics.Add(typeDefinition);
            typeDefinition = pointerType;
            steps++;
            endsWithShift = false;
            endsWithRotate = false;
        }

        return true;
    }

    private static ConstantAst ParseConstant(Token token)
    {
        var constant = CreateAst<ConstantAst>(token);
        switch (token.Type)
        {
            case TokenType.Literal:
                constant.TypeName = "string";
                constant.String = token.Value;
                return constant;
            case TokenType.Character:
                constant.TypeName = "u8";
                constant.Value = new Constant {UnsignedInteger = (byte)token.Value[0]};
                return constant;
            case TokenType.Number:
                if (token.Flags == TokenFlags.None)
                {
                    if (int.TryParse(token.Value, out var s32))
                    {
                        constant.TypeName = "s32";
                        constant.Value = new Constant {Integer = s32};
                        return constant;
                    }

                    if (long.TryParse(token.Value, out var s64))
                    {
                        constant.TypeName = "s64";
                        constant.Value = new Constant {Integer = s64};
                        return constant;
                    }

                    if (ulong.TryParse(token.Value, out var u64))
                    {
                        constant.TypeName = "u64";
                        constant.Value = new Constant {UnsignedInteger = u64};
                        return constant;
                    }

                    ErrorReporter.Report($"Invalid integer '{token.Value}', must be 64 bits or less", token);
                    return null;
                }

                if (token.Flags.HasFlag(TokenFlags.Float))
                {
                    if (float.TryParse(token.Value, out var f32))
                    {
                        constant.TypeName = "float";
                        constant.Value = new Constant {Double = (double)f32};
                        return constant;
                    }

                    if (double.TryParse(token.Value, out var f64))
                    {
                        constant.TypeName = "float64";
                        constant.Value = new Constant {Double = f64};
                        return constant;
                    }

                    ErrorReporter.Report($"Invalid floating point number '{token.Value}', must be single or double precision", token);
                    return null;
                }

                if (token.Flags.HasFlag(TokenFlags.HexNumber))
                {
                    if (token.Value.Length == 2)
                    {
                        ErrorReporter.Report($"Invalid number '{token.Value}'", token);
                        return null;
                    }

                    var value = token.Value.Substring(2);
                    if (value.Length <= 8)
                    {
                        if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u32))
                        {
                            constant.TypeName = "u32";
                            constant.Value = new Constant {UnsignedInteger = u32};
                            return constant;
                        }
                    }
                    else if (value.Length <= 16)
                    {
                        if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u64))
                        {
                            constant.TypeName = "u64";
                            constant.Value = new Constant {UnsignedInteger = u64};
                            return constant;
                        }
                    }
                    ErrorReporter.Report($"Invalid integer '{token.Value}'", token);
                    return null;
                }
                ErrorReporter.Report($"Unable to determine type of token '{token.Value}'", token);
                return null;
            case TokenType.Boolean:
                constant.TypeName = "bool";
                constant.Value = new Constant {Boolean = token.Value == "true"};
                return constant;
            default:
                ErrorReporter.Report($"Unable to determine type of token '{token.Value}'", token);
                return null;
        }
    }

    private static CastAst ParseCast(TokenEnumerator enumerator, IFunction currentFunction)
    {
        var castAst = CreateAst<CastAst>(enumerator.Current);

        // 1. Try to get the open paren to begin the cast
        if (!enumerator.MoveNext() || enumerator.Current.Type != TokenType.OpenParen)
        {
            ErrorReporter.Report("Expected '(' after 'cast'", enumerator.Current);
            return null;
        }

        // 2. Get the target type
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected to get the target type for the cast", enumerator.Last);
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
            ErrorReporter.Report("Expected ',' after type in cast", enumerator.Current);
            return null;
        }

        // 4. Get the value expression
        if (!enumerator.MoveNext())
        {
            ErrorReporter.Report("Expected to get the value for the cast", enumerator.Last);
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

    private static T CreateAst<T>(Token token) where T : IAst, new()
    {
        return new()
        {
            FileIndex = token.FileIndex,
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
                var op = (Operator)token.Value[0];
                return Enum.IsDefined(typeof(Operator), op) ? op : Operator.None;
        }
    }
}
