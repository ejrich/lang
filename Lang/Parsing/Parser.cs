using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lang.Parsing
{
    public class ParseResult
    {
        public List<IAst> SyntaxTrees { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }

    public interface IParser
    {
        ParseResult Parse(List<string> projectFiles);
    }

    public class Parser : IParser
    {
        private readonly ILexer _lexer;
        private static FunctionAst _currentFunction;

        private class TokenEnumerator
        {
            private readonly List<Token> _tokens;
            private int _index;

            public TokenEnumerator(List<Token> tokens)
            {
                _tokens = tokens;
            }

            public Token Current { get; private set; }

            public bool MoveNext()
            {
                Current = _tokens.Count > _index ? _tokens[_index] : null;
                _index++;
                return Current != null;
            }

            public bool Move(int steps)
            {
                _index += steps;
                Current = _tokens.Count > _index ? _tokens[_index] : null;
                return Current != null;
            }

            public Token Peek(int steps = 0)
            {
                return _tokens.Count > _index + steps ? _tokens[_index + steps] : null;
            }

            public Token Last => _tokens[^1];
        }

        public Parser(ILexer lexer) => _lexer = lexer;

        public ParseResult Parse(List<string> projectFiles)
        {
            var parseResult = new ParseResult();

            // 1. Parse project files
            var success = true;
            for (var fileIndex = 0; fileIndex < projectFiles.Count; fileIndex++)
            {
                var file = projectFiles[fileIndex];
                var syntaxTrees = ParseFile(file, fileIndex, out var errors);
                if (errors.Any())
                {
                    success = false;
                    foreach (var error in errors)
                    {
                        error.FileIndex = fileIndex;
                    }
                    parseResult.Errors.AddRange(errors);
                }
                else if (success)
                {
                    parseResult.SyntaxTrees.AddRange(syntaxTrees);
                }
            }

            return parseResult;
        }

        private List<IAst> ParseFile(string file, int fileIndex, out List<ParseError> errors)
        {
            // 1. Load file tokens
            var tokens = _lexer.LoadFileTokens(file, fileIndex, out errors);

            // 2. Iterate through tokens, tracking different ASTs
            var syntaxTrees = new List<IAst>();
            var enumerator = new TokenEnumerator(tokens);
            while (enumerator.MoveNext())
            {
                syntaxTrees.Add(ParseTopLevelAst(enumerator, errors));
            }

            return syntaxTrees;
        }

        private static IAst ParseTopLevelAst(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var token = enumerator.Current!;
            switch (token!.Type)
            {
                case TokenType.Token:
                    if (enumerator.Peek()?.Type == TokenType.Colon)
                    {
                        return ParseDeclaration(enumerator, errors);
                    }
                    return ParseFunction(enumerator, errors);
                case TokenType.Struct:
                    return ParseStruct(enumerator, errors);
                case TokenType.Enum:
                    return ParseEnum(enumerator, errors);
                case TokenType.Pound:
                    return ParseTopLevelDirective(enumerator, errors);
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{token.Value}'", Token = enumerator.Current
                    });
                    return null;
            }
        }

        private static FunctionAst ParseFunction(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Determine return type and name of the function
            var function = CreateAst<FunctionAst>(enumerator.Current);
            _currentFunction = function;

            // 1a. Check if the return type is void
            if (enumerator.Peek()?.Type == TokenType.OpenParen)
            {
                function.ReturnType = CreateAst<TypeDefinition>(enumerator.Current);
                function.ReturnType.Name = "void";
            }
            else
            {
                function.ReturnType = ParseType(enumerator, errors);
                enumerator.MoveNext();
            }

            // 1b. Set the name of the function or get the name from the type
            switch (enumerator.Current?.Type)
            {
                case TokenType.Token:
                    function.Name = enumerator.Current.Value;
                    enumerator.MoveNext();
                    break;
                case TokenType.OpenParen:
                    // TODO Switch the return type to be the function name and generics
                    break;
                case null:
                    errors.Add(new ParseError {Error = "Expected the function name to be declared", Token = enumerator.Last});
                    return null;
                default:
                    errors.Add(new ParseError {Error = "Expected the function name to be declared", Token = enumerator.Current});
                    enumerator.MoveNext();
                    break;
            }

            // 2. Parse generics
            if (enumerator.Current?.Type == TokenType.LessThan)
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
                            errors.Add(new ParseError
                            {
                                Error = $"Expected comma in generics of function '{function.Name}'",
                                Token = token
                            });
                        }

                        break;
                    }

                    if (!commaRequiredBeforeNextType)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                if (!generics.Add(token.Value))
                                {
                                    errors.Add(new ParseError
                                    {
                                        Error = $"Duplicate generic '{token.Value}' in function '{function.Name}'", Token = token
                                    });
                                }
                                commaRequiredBeforeNextType = true;
                                break;
                            default:
                                errors.Add(new ParseError
                                {
                                    Error = $"Unexpected token '{token.Value}' in generics of function '{function.Name}'", Token = token
                                });
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
                                errors.Add(new ParseError
                                {
                                    Error = $"Unexpected token '{token.Value}' in function '{function.Name}'", Token = token
                                });
                                commaRequiredBeforeNextType = false;
                                break;
                        }
                    }
                }

                if (!generics.Any())
                {
                    errors.Add(new ParseError
                    {
                        Error = "Expected function to contain generics", Token = enumerator.Current ?? enumerator.Last
                    });
                }
                enumerator.MoveNext();
                // TODO function.Generics.AddRange(generics);
            }

            // 3. Find open paren to start parsing arguments
            if (enumerator.Current?.Type != TokenType.OpenParen)
            {
                // Add an error to the function AST and continue until open paren
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token '{token.Value}' in function definition",
                    Token = token
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenParen)
                    enumerator.MoveNext();
            }

            // 4. Parse arguments until a close paren
            var commaRequiredBeforeNextArgument = false;
            DeclarationAst currentArgument = null;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseParen)
                {
                    if (commaRequiredBeforeNextArgument)
                    {
                        function.Arguments.Add(currentArgument);
                    }
                    break;
                }

                switch (token.Type)
                {
                    case TokenType.Token:
                    case TokenType.VarArgs:
                        if (commaRequiredBeforeNextArgument)
                        {
                            errors.Add(new ParseError
                            {
                                Error = "Comma required after declaring an argument", Token = token
                            });
                        }
                        else if (currentArgument == null)
                        {
                            currentArgument = CreateAst<DeclarationAst>(token);
                            currentArgument.Type = ParseType(enumerator, errors, true);
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
                            currentArgument = null;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = "Unexpected comma in arguments", Token = token});
                        }
                        commaRequiredBeforeNextArgument = false;
                        break;
                    case TokenType.Equals:
                        if (commaRequiredBeforeNextArgument)
                        {
                            enumerator.MoveNext();
                            currentArgument.Value = ParseNextExpressionUnit(enumerator, errors, out _);
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = "Unexpected comma in arguments", Token = token});
                        }
                        break;
                    default:
                        errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in arguments", Token = token});
                        break;
                }
            }

            if (!commaRequiredBeforeNextArgument && function.Arguments.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Unexpected comma in arguments", Token = enumerator.Current ?? enumerator.Last
                });
            }

            enumerator.MoveNext();
            // 5. Handle compiler directives
            if (enumerator.Current?.Type == TokenType.Pound)
            {
                enumerator.MoveNext();
                switch (enumerator.Current?.Value)
                {
                    case "extern":
                        function.Extern = true;
                        if (enumerator.Peek()?.Type != TokenType.Literal)
                        {
                            errors.Add(new ParseError
                            {
                                Error = "Extern function definition should be followed by the library in use",
                                Token = enumerator.Current
                            });
                        }
                        else
                        {
                            enumerator.MoveNext();
                            function.ExternLib = enumerator.Current.Value;
                        }
                        return function;
                    case "compiler":
                        function.Compiler = true;
                        return function;
                    case null:
                        errors.Add(new ParseError
                        {
                            Error = "Expected compiler directive value",
                            Token = enumerator.Last
                        });
                        return function;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected compiler directive '{enumerator.Current.Value}'",
                            Token = enumerator.Current
                        });
                        break;
                }
                enumerator.MoveNext();
            }

            // 6. Find open brace to start parsing body
            if (enumerator.Current?.Type != TokenType.OpenBrace)
            {
                // Add an error to the function AST and continue until open paren
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token '{token.Value}' in function definition",
                    Token = token
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenBrace)
                    enumerator.MoveNext();
            }

            // 7. Parse function body
            var closed = false;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseBrace)
                {
                    closed = true;
                    break;
                }

                var ast = ParseLine(enumerator, errors);
                if (ast != null)
                    function.Children.Add(ast);
            }

            if (!closed)
            {
                errors.Add(new ParseError
                {
                    Error = "Function not closed by '}'", Token = enumerator.Current ?? enumerator.Last
                });
            }

            return function;
        }

        private static StructAst ParseStruct(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var structAst = CreateAst<StructAst>(enumerator.Current);

            // 1. Determine name of struct
            enumerator.MoveNext();
            switch (enumerator.Current?.Type)
            {
                case TokenType.Token:
                    structAst.Name = enumerator.Current.Value;
                    break;
                case null:
                    errors.Add(new ParseError {Error = "Expected struct to have name", Token = enumerator.Last});
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}' in struct definition", Token = enumerator.Current
                    });
                    break;
            }

            // 2. Parse struct generics
            if (enumerator.Peek()?.Type == TokenType.LessThan)
            {
                // Clear the '<' before entering loop
                enumerator.MoveNext();
                var commaRequiredBeforeNextType = false;
                var generics = new HashSet<string>();
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;

                    if (token.Type == TokenType.GreaterThan)
                    {
                        if (!commaRequiredBeforeNextType)
                        {
                            errors.Add(new ParseError
                            {
                                Error = $"Expected comma in generics for struct '{structAst.Name}'",
                                Token = token
                            });
                        }

                        break;
                    }

                    if (!commaRequiredBeforeNextType)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                if (!generics.Add(token.Value))
                                {
                                    errors.Add(new ParseError
                                    {
                                        Error = $"Duplicate generic '{token.Value}' in struct '{structAst.Name}'", Token = token
                                    });
                                }
                                commaRequiredBeforeNextType = true;
                                break;
                            default:
                                errors.Add(new ParseError
                                {
                                    Error = $"Unexpected token '{token.Value}' in generics for struct '{structAst.Name}'", Token = token
                                });
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
                                errors.Add(new ParseError
                                {
                                    Error = $"Unexpected token '{token.Value}' in definition of struct '{structAst.Name}'", Token = token
                                });
                                commaRequiredBeforeNextType = false;
                                break;
                        }
                    }
                }

                if (!generics.Any())
                {
                    errors.Add(new ParseError
                    {
                        Error = $"Expected struct '{structAst.Name}' to contain generics", Token = enumerator.Current ?? enumerator.Last
                    });
                }
                structAst.Generics.AddRange(generics);
            }

            // 3. Parse over the open brace
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.OpenBrace)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected '{' token in struct definition", Token = enumerator.Current ?? enumerator.Last
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenBrace)
                    enumerator.MoveNext();
            }

            // 4. Iterate through fields
            StructFieldAst currentField = null;
            var parsingFieldDefault = false;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseBrace)
                {
                    break;
                }

                switch (token.Type)
                {
                    case TokenType.Token:
                        // First get the type of the field
                        if (currentField == null)
                        {
                            currentField = CreateAst<StructFieldAst>(token);
                            currentField.Type = ParseType(enumerator, errors);
                        }
                        else if (currentField.Name == null)
                        {
                            currentField.Name = enumerator.Current.Value;
                        }
                        else if (parsingFieldDefault)
                        {
                            var structField = ParseExpression(enumerator, errors);
                            currentField.DefaultValue = structField;
                            structAst.Fields.Add(currentField);
                            currentField = null;
                            parsingFieldDefault = false;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        }
                        break;
                    case TokenType.SemiColon:
                        if (currentField != null)
                        {
                            // Catch if the name hasn't been set
                            if (currentField.Name == null || parsingFieldDefault)
                            {
                                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                            }
                            // Add the field to the struct and continue
                            structAst.Fields.Add(currentField);
                            currentField = null;
                            parsingFieldDefault = false;
                        }
                        break;
                    case TokenType.Equals:
                        if (currentField?.Type != null && currentField.Name != null)
                        {
                            parsingFieldDefault = true;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        }
                        break;
                    case TokenType.Number:
                    case TokenType.Boolean:
                    case TokenType.Literal:
                        if (currentField != null && parsingFieldDefault)
                        {
                            var constant = CreateAst<ConstantAst>(token);
                            constant.Type = InferType(token, errors);
                            constant.Value = token.Value;
                            currentField.DefaultValue = constant;
                            parsingFieldDefault = false;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        }
                        break;
                    default:
                        errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
                        break;
                }
            }

            if (currentField != null)
            {
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in struct", Token = token});
            }

            // 5. Mark field types as generic if necessary
            for (var i = 0; i < structAst.Generics.Count; i++)
            {
                var generic = structAst.Generics[i];
                foreach (var field in structAst.Fields)
                {
                    if (SearchForGeneric(generic, i, field.Type))
                    {
                        field.HasGeneric = true;
                    }
                }
            }

            return structAst;
        }

        private static EnumAst ParseEnum(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var enumAst = CreateAst<EnumAst>(enumerator.Current);

            // 1. Determine name of enum
            enumerator.MoveNext();
            switch (enumerator.Current?.Type)
            {
                case TokenType.Token:
                    enumAst.Name = enumerator.Current.Value;
                    break;
                case null:
                    errors.Add(new ParseError {Error = "Expected enum to have name", Token = enumerator.Last});
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}' in enum definition", Token = enumerator.Current
                    });
                    break;
            }

            // 2. Parse over the open brace
            enumerator.MoveNext();
            if (enumerator.Current?.Type == TokenType.Colon)
            {
                enumerator.MoveNext();
                enumAst.BaseType = ParseType(enumerator, errors);
                enumerator.MoveNext();
            }

            if (enumerator.Current?.Type != TokenType.OpenBrace)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected '{' token in enum definition", Token = enumerator.Current ?? enumerator.Last
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenBrace)
                    enumerator.MoveNext();
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
                    case TokenType.Token:
                        if (currentValue == null)
                        {
                            currentValue = CreateAst<EnumValueAst>(token);
                            currentValue.Name = token.Value;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in enum", Token = token});
                        }
                        break;
                    case TokenType.SemiColon:
                        if (currentValue != null)
                        {
                            // Catch if the name hasn't been set
                            if (currentValue.Name == null || parsingValueDefault)
                            {
                                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in enum", Token = token});
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
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in enum", Token = token});
                        }
                        break;
                    case TokenType.Number:
                        if (currentValue != null && parsingValueDefault)
                        {
                            if (int.TryParse(token.Value, out var value))
                            {
                                currentValue.Value = value;
                                currentValue.Defined = true;
                            }
                            else
                            {
                                errors.Add(new ParseError {Error = $"Expected enum value to be an integer, but got '{token.Value}'", Token = token});
                            }
                            parsingValueDefault = false;
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in enum", Token = token});
                        }
                        break;
                    default:
                        errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in enum", Token = token});
                        break;
                }
            }

            if (currentValue != null)
            {
                var token = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in enum", Token = token});
            }

            return enumAst;
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

        private static IAst ParseLine(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var token = enumerator.Current;

            switch (token?.Type)
            {
                case TokenType.Return:
                    return ParseReturn(enumerator, errors);
                case TokenType.If:
                    return ParseConditional(enumerator, errors);
                case TokenType.While:
                    return ParseWhile(enumerator, errors);
                case TokenType.Each:
                    return ParseEach(enumerator, errors);
                case TokenType.Token:
                    var nextToken = enumerator.Peek();
                    switch (nextToken?.Type)
                    {
                        case TokenType.OpenParen:
                            return ParseCall(enumerator, errors, true);
                        case TokenType.Colon:
                            return ParseDeclaration(enumerator, errors);
                        case TokenType.Equals:
                            return ParseAssignment(enumerator, errors);
                        default:
                            return ParseExpression(enumerator, errors);
                    }
                case TokenType.Increment:
                case TokenType.Decrement:
                    return ParseExpression(enumerator, errors);
                case TokenType.OpenBrace:
                    return ParseScope(enumerator, errors);
                case TokenType.Pound:
                    return ParseCompilerDirective(enumerator, errors);
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "End of file reached without closing scope", Token = enumerator.Last
                    });
                    return null;
                default:
                    errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}'", Token = token});
                    return null;
            }
        }

        private static ScopeAst ParseScope(TokenEnumerator enumerator, List<ParseError> errors)
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

                var ast = ParseLine(enumerator, errors);
                if (ast != null)
                    scopeAst.Children.Add(ast);
            }

            if (!closed)
            {
                errors.Add(new ParseError
                {
                    Error = "Scope not closed by '}'", Token = enumerator.Current ?? enumerator.Last
                });
            }

            return scopeAst;
        }

        private static ConditionalAst ParseConditional(TokenEnumerator enumerator, List<ParseError> errors, bool topLevel = false)
        {
            var conditionalAst = CreateAst<ConditionalAst>(enumerator.Current);

            // 1. Parse the conditional expression by first iterating over the initial 'if'
            enumerator.MoveNext();
            conditionalAst.Condition = ParseExpression(enumerator, errors, null, TokenType.OpenBrace, TokenType.Then);

            // 2. Determine how many lines to parse
            switch (enumerator.Current?.Type)
            {
                case TokenType.Then:
                {
                    // Parse single AST
                    enumerator.MoveNext();
                    var ast = topLevel ? ParseTopLevelAst(enumerator, errors) : ParseLine(enumerator, errors);
                    if (ast != null)
                        conditionalAst.Children.Add(ast);
                    break;
                }
                case TokenType.OpenBrace:
                {
                    // Parse until close brace
                    while (enumerator.MoveNext())
                    {
                        if (enumerator.Current.Type == TokenType.CloseBrace)
                        {
                            break;
                        }

                        var ast = topLevel ? ParseTopLevelAst(enumerator, errors) : ParseLine(enumerator, errors);
                        if (ast != null)
                            conditionalAst.Children.Add(ast);
                    }
                    break;
                }
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected if to contain conditional expression", Token = enumerator.Last
                    });
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
                    });
                    break;
            }

            // 3. Parse else block if necessary
            if (enumerator.Peek()?.Type == TokenType.Else)
            {
                // First clear the else and then determine how to parse else block
                enumerator.MoveNext();
                enumerator.MoveNext();
                switch (enumerator.Current?.Type)
                {
                    case TokenType.Then:
                    {
                        // Parse single AST
                        enumerator.MoveNext();
                        var ast = topLevel ? ParseTopLevelAst(enumerator, errors) : ParseLine(enumerator, errors);
                        if (ast != null)
                            conditionalAst.Else.Add(ast);
                        break;
                    }
                    case TokenType.OpenBrace:
                    {
                        // Parse until close brace
                        while (enumerator.MoveNext())
                        {
                            if (enumerator.Current.Type == TokenType.CloseBrace)
                            {
                                break;
                            }

                            var ast = topLevel ? ParseTopLevelAst(enumerator, errors) : ParseLine(enumerator, errors);
                            if (ast != null)
                                conditionalAst.Else.Add(ast);
                        }
                        break;
                    }
                    case TokenType.If:
                        // Nest another conditional in else children
                        var conditional = ParseConditional(enumerator, errors);
                        conditionalAst.Else.Add(conditional);
                        break;
                    case null:
                        errors.Add(new ParseError {Error = "Expected body of else branch", Token = enumerator.Last});
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
                        });
                        break;
                }
            }

            return conditionalAst;
        }

        private static WhileAst ParseWhile(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var whileAst = CreateAst<WhileAst>(enumerator.Current);

            // 1. Parse the conditional expression by first iterating over the initial 'while'
            enumerator.MoveNext();
            whileAst.Condition = ParseExpression(enumerator, errors, null, TokenType.OpenBrace, TokenType.Then);

            // 2. Determine how many lines to parse
            switch (enumerator.Current?.Type)
            {
                case TokenType.Then:
                {
                    // Parse single AST
                    enumerator.MoveNext();
                    var ast = ParseLine(enumerator, errors);
                    if (ast != null)
                        whileAst.Children.Add(ast);
                    break;
                }
                case TokenType.OpenBrace:
                {
                    // Parse until close brace
                    whileAst.Children.Add(ParseScope(enumerator, errors));
                    break;
                }
                case null:
                    errors.Add(new ParseError {Error = "Expected while loop to contain body", Token = enumerator.Last});
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
                    });
                    break;
            }

            return whileAst;
        }

        private static EachAst ParseEach(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var eachAst = CreateAst<EachAst>(enumerator.Current);

            // 1. Parse the iteration variable by first iterating over the initial 'each'
            enumerator.MoveNext();
            if (enumerator.Current?.Type == TokenType.Token)
            {
                eachAst.IterationVariable = enumerator.Current.Value;
            }
            else
            {
                errors.Add(new ParseError
                {
                    Error = "Expected variable in each block definition",
                    Token = enumerator.Current ?? enumerator.Last
                });
            }

            // 2. Parse over the in keyword
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.In)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected 'in' token in each block", Token = enumerator.Current ?? enumerator.Last
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.In)
                    enumerator.MoveNext();
            }

            // 3. Determine the iterator
            enumerator.MoveNext();
            var expression = ParseExpression(enumerator, errors, null, TokenType.OpenBrace, TokenType.Then, TokenType.Range);

            // 3a. Check if the next token is a range
            switch (enumerator.Current?.Type)
            {
                case TokenType.Range:
                    eachAst.RangeBegin = expression;
                    enumerator.MoveNext();
                    if (enumerator.Current == null)
                    {
                        errors.Add(new ParseError {Error = "Expected range to have an end", Token = enumerator.Last});
                        return eachAst;
                    }

                    eachAst.RangeEnd = ParseExpression(enumerator, errors, null, TokenType.OpenBrace, TokenType.Then);
                    if (enumerator.Current == null)
                    {
                        errors.Add(new ParseError
                        {
                            Error = "Expected each block to have iteration and body", Token = enumerator.Last
                        });
                        return eachAst;
                    }

                    break;
                case TokenType.OpenBrace:
                case TokenType.Then:
                    eachAst.Iteration = expression;
                    break;
                case null:
                    errors.Add(new ParseError
                    {
                        Error = "Expected each block to have iteration and body", Token = enumerator.Last
                    });
                    return eachAst;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}' in each block",
                        Token = enumerator.Current
                    });
                    return eachAst;
            }

            // 4. Determine how many lines to parse
            switch (enumerator.Current?.Type)
            {
                case TokenType.Then:
                {
                    // Parse single AST
                    enumerator.MoveNext();
                    var ast = ParseLine(enumerator, errors);
                    if (ast != null)
                        eachAst.Children.Add(ast);
                    break;
                }
                case TokenType.OpenBrace:
                {
                    // Parse until close brace
                    eachAst.Children.Add(ParseScope(enumerator, errors));
                    break;
                }
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{enumerator.Current.Value}'", Token = enumerator.Current
                    });
                    break;
            }

            return eachAst;
        }

        private static DeclarationAst ParseDeclaration(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var declaration = CreateAst<DeclarationAst>(enumerator.Current);
            declaration.Name = enumerator.Current.Value;

            // 1. Expect to get colon
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.Colon)
            {
                var errorToken = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token in declaration '{errorToken.Value}'",
                    Token = errorToken
                });
                return declaration;
            }

            // 2. Check if type is given
            if (enumerator.Peek()?.Type == TokenType.Token)
            {
                enumerator.MoveNext();
                declaration.Type = ParseType(enumerator, errors);
            }

            // 3. Get the value or return
            enumerator.MoveNext();
            var token = enumerator.Current;
            switch (token?.Type)
            {
                case TokenType.Equals:
                    ParseDeclarationValue(declaration, enumerator, errors);
                    break;
                case TokenType.SemiColon:
                    if (declaration.Type == null)
                    {
                        errors.Add(new ParseError {Error = "Expected token declaration to have value", Token = token});
                    }
                    break;
                case null:
                    errors.Add(new ParseError {Error = "Expected declaration to have value", Token = enumerator.Last});
                    return declaration;
                default:
                    errors.Add(new ParseError {Error = $"Unexpected token '{token.Value}' in declaration", Token = token});
                    // Parse until there is an equals sign
                    while (enumerator.Current != null && enumerator.Current.Type != TokenType.Equals)
                        enumerator.MoveNext();

                    ParseDeclarationValue(declaration, enumerator, errors);
                    break;
            }

            // 4. Parse compiler directives
            if (enumerator.Peek()?.Type == TokenType.Pound)
            {
                switch (enumerator.Peek(1)?.Value)
                {
                    case "const":
                        declaration.Constant = true;
                        enumerator.MoveNext();
                        enumerator.MoveNext();
                        break;
                }
            }

            return declaration;
        }

        private static void ParseDeclarationValue(DeclarationAst declaration, TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Step over '=' sign
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected declaration to have a value", Token = enumerator.Last});
                return;
            }

            // 2. Parse expression, constant, or object initialization as the value
            if (enumerator.Current.Type == TokenType.OpenBrace)
            {
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        break;
                    }

                    var assignment = ParseAssignment(enumerator, errors);
                    declaration.Assignments.Add(assignment);
                }
            }
            else
            {
                declaration.Value = ParseExpression(enumerator, errors);
            }
        }

        private static AssignmentAst ParseAssignment(TokenEnumerator enumerator, List<ParseError> errors, ExpressionAst reference = null)
        {
            // 1. Set the variable
            var assignment = CreateAst<AssignmentAst>(enumerator.Current);
            assignment.Reference = reference;

            // 2. When the original reference is null, set the l-value to an identifier
            if (reference == null)
            {
                var variableAst = CreateAst<IdentifierAst>(enumerator.Current);
                variableAst.Name = enumerator.Current.Value;
                assignment.Reference = variableAst;

                // 2a. Expect to get equals sign
                if (!enumerator.MoveNext())
                {
                    errors.Add(new ParseError {Error = "Expected '=' in assignment'", Token = enumerator.Last});
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
                        if (enumerator.Peek()?.Type == TokenType.Equals)
                        {
                            enumerator.MoveNext();
                        }
                        else
                        {
                            errors.Add(new ParseError {Error = "Expected '=' in assignment'", Token = token});
                        }
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = "Expected operator in assignment", Token = token});
                    }
                }
            }
            // 3, Get the operator on the reference expression if the expression ends with an operator
            else
            {
                if (reference.Children.Count == 1)
                {
                    assignment.Reference = reference.Children[0];
                }
                if (reference.Children.Count == reference.Operators.Count)
                {
                    assignment.Operator = reference.Operators.Last();
                    reference.Operators.RemoveAt(reference.Operators.Count - 1);
                }
            }

            // 4. Step over '=' sign
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected to have a value", Token = enumerator.Last});
                return null;
            }

            // 5. Parse expression, constant, or another token as the value
            assignment.Value = ParseExpression(enumerator, errors);

            return assignment;
        }

        private static IAst ParseExpression(TokenEnumerator enumerator, List<ParseError> errors, ExpressionAst initial = null, params TokenType[] endToken)
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
                    return ParseAssignment(enumerator, errors, expression);
                }

                if (operatorRequired)
                {
                    if (token.Type == TokenType.Increment || token.Type == TokenType.Decrement)
                    {
                        // Create subexpression to hold the operation
                        // This case would be `var b = 4 + a++`, where we have a value before the operator
                        var changeByOneAst = CreateAst<ChangeByOneAst>(token);
                        changeByOneAst.Positive = token.Type == TokenType.Increment;
                        changeByOneAst.Value = expression.Children[^1];
                        expression.Children[^1] = changeByOneAst;
                        continue;
                    }
                    if (token.Type == TokenType.Number && token.Value[0] == '-')
                    {
                        token.Value = token.Value[1..];
                        expression.Operators.Add(Operator.Subtract);
                        var constant = CreateAst<ConstantAst>(token);
                        constant.Type = InferType(token, errors);
                        constant.Value = token.Value;
                        expression.Children.Add(constant);
                        continue;
                    }
                    var op = ConvertOperator(token);
                    if (op == Operator.Dot)
                    {
                        var structFieldRef = ParseStructFieldRef(enumerator, errors, expression.Children[^1]);
                        expression.Children[^1] = structFieldRef;
                    }
                    else if (op != Operator.None)
                    {
                        expression.Operators.Add(op);
                        operatorRequired = false;
                    }
                    else
                    {
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}' when operator was expected", Token = token
                        });
                        return null;
                    }
                }
                else
                {
                    var ast = ParseNextExpressionUnit(enumerator, errors, out operatorRequired);
                    if (ast != null)
                        expression.Children.Add(ast);
                }
            } while (enumerator.MoveNext());

            if (!expression.Children.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Expression should contain elements", Token = enumerator.Current ?? enumerator.Last
                });
            }
            else if (!operatorRequired && expression.Children.Any())
            {
                errors.Add(new ParseError
                {
                    Error = "Value required after operator", Token = enumerator.Current ?? enumerator.Last
                });
                return expression;
            }

            if (expression.Children.Count == 1)
            {
                return expression.Children.First();
            }

            if (!errors.Any())
            {
                SetOperatorPrecedence(expression);
            }
            return expression;
        }

        private static IAst ParseStructFieldRef(TokenEnumerator enumerator, List<ParseError> errors, IAst initialAst)
        {
            // 1. Initialize and move over the dot operator
            var structFieldRef = new StructFieldRefAst {FileIndex = initialAst.FileIndex, Line = initialAst.Line, Column = initialAst.Column};
            structFieldRef.Children.Add(initialAst);

            // 2. Parse expression units until the operator is not '.'
            var operatorRequired = false;
            while (enumerator.MoveNext())
            {
                if (operatorRequired)
                {
                    if (ConvertOperator(enumerator.Current) != Operator.Dot)
                    {
                        enumerator.Move(-1);
                        break;
                    }
                    operatorRequired = false;
                }
                else
                {
                    var ast = ParseNextExpressionUnit(enumerator, errors, out operatorRequired);
                    if (ast != null)
                        structFieldRef.Children.Add(ast);
                }
            }

            return structFieldRef;
        }

        private static IAst ParseNextExpressionUnit(TokenEnumerator enumerator, List<ParseError> errors, out bool operatorRequired)
        {
            var token = enumerator.Current;
            var nextToken = enumerator.Peek();
            operatorRequired = true;
            switch (token.Type)
            {
                case TokenType.Number:
                case TokenType.Boolean:
                case TokenType.Literal:
                    // Parse constant
                    var constant = CreateAst<ConstantAst>(token);
                    constant.Type = InferType(token, errors);
                    constant.Value = token.Value;
                    return constant;
                case TokenType.Null:
                    return CreateAst<NullAst>(token);
                case TokenType.Token:
                    // Parse variable, call, or expression
                    switch (nextToken?.Type)
                    {
                        case TokenType.OpenParen:
                            return ParseCall(enumerator, errors);
                        case TokenType.OpenBracket:
                            return ParseIndex(enumerator, errors);
                        case null:
                            errors.Add(new ParseError
                            {
                                Error = $"Expected token to follow '{token.Value}'", Token = token
                            });
                            return null;
                        case TokenType.Asterisk:
                            switch (enumerator.Peek(1)?.Type)
                            {
                                case TokenType.Comma:
                                case TokenType.CloseParen:
                                    if (TryParseType(enumerator, errors, out var typeDefinition))
                                    {
                                        return typeDefinition;
                                    }
                                    break;
                            }
                            break;
                        case TokenType.LessThan:
                        {
                            if (TryParseType(enumerator, errors, out var typeDefinition))
                            {
                                return typeDefinition;
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
                        changeByOneAst.Value = ParseNextExpressionUnit(enumerator, errors, out operatorRequired);
                        if (enumerator.Peek()?.Type == TokenType.Period)
                        {
                            enumerator.MoveNext();
                            changeByOneAst.Value = ParseStructFieldRef(enumerator, errors, changeByOneAst.Value);
                        }
                        return changeByOneAst;
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = $"Expected token to follow '{token.Value}'", Token = token});
                        return null;
                    }
                case TokenType.OpenParen:
                    // Parse subexpression
                    if (enumerator.MoveNext())
                    {
                        return ParseExpression(enumerator, errors, null, TokenType.CloseParen);
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = $"Expected token to follow '{token.Value}'", Token = token});
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
                        unaryAst.Value = ParseNextExpressionUnit(enumerator, errors, out operatorRequired);
                        if (enumerator.Peek()?.Type == TokenType.Period)
                        {
                            enumerator.MoveNext();
                            unaryAst.Value = ParseStructFieldRef(enumerator, errors, unaryAst.Value);
                        }
                        return unaryAst;
                    }
                    else
                    {
                        errors.Add(new ParseError {Error = $"Expected token to follow '{token.Value}'", Token = token});
                        return null;
                    }
                case TokenType.Cast:
                    return ParseCast(enumerator, errors);
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token '{token.Value}' in expression", Token = token
                    });
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
                    operatorPrecedence = GetOperatorPrecedence(expression.Operators[i]);
                }
                else
                {
                    operatorPrecedence = precedence;
                }
            }
        }

        private static ExpressionAst CreateSubExpression(ExpressionAst expression, int parentPrecedence, int i, out int end)
        {
            // @Fix this case should make these subexpressions
            // d := a + 1 == b + 2 && 1 + b == 2 || b > 3 + 4 * c - 1;
            // d := a + 1 == (b + 2) && (1 + b == 2) || (b > (3 + (4 * c) - 1));
            // This can be fixed in code by adding parens around (1 + b)
            var subExpression = new ExpressionAst
            {
                FileIndex = expression.Children[i].FileIndex,
                Line = expression.Children[i].Line,
                Column = expression.Children[i].Column
            };

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

        private static CallAst ParseCall(TokenEnumerator enumerator, List<ParseError> errors, bool requiresSemicolon = false)
        {
            var callAst = CreateAst<CallAst>(enumerator.Current);
            callAst.Function = enumerator.Current.Value;

            // This enumeration is the open paren
            enumerator.MoveNext();
            // Enumerate over the first argument
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (enumerator.Current.Type == TokenType.CloseParen) break;

                if (token.Type == TokenType.Comma)
                {
                    errors.Add(new ParseError {Error = "Expected comma before next argument", Token = token});
                }
                else
                {
                    if (token.Type == TokenType.Token && enumerator.Peek()?.Type == TokenType.Equals)
                    {
                        var argumentName = token.Value;

                        enumerator.MoveNext();
                        enumerator.MoveNext();

                        callAst.SpecifiedArguments ??= new Dictionary<string, IAst>();
                        var argument = ParseExpression(enumerator, errors, null, TokenType.Comma, TokenType.CloseParen);
                        if (!callAst.SpecifiedArguments.TryAdd(argumentName, argument))
                        {
                            errors.Add(new ParseError {Error = $"Specified argument '{token.Value}' is already in the call", Token = token});
                        }
                    }
                    else
                    {
                        callAst.Arguments.Add(ParseExpression(enumerator, errors, null, TokenType.Comma, TokenType.CloseParen));
                    }

                    var currentType = enumerator.Current?.Type;
                    if (currentType == TokenType.CloseParen) break;
                    if (currentType == TokenType.SemiColon)
                    {
                        errors.Add(new ParseError
                        {
                            Error = "Expected to close call with ')'", Token = enumerator.Current
                        });
                        break;
                    }
                }
            }

            var current = enumerator.Current;
            if (current == null)
            {
                errors.Add(new ParseError
                {
                    Error = "Expected to close call", Token = enumerator.Last
                });
            }
            else if (requiresSemicolon)
            {
                if (current.Type == TokenType.SemiColon)
                    return callAst;

                if (enumerator.Peek()?.Type != TokenType.SemiColon)
                {
                    errors.Add(new ParseError
                    {
                        Error = "Expected ';'", Token = enumerator.Current ?? enumerator.Last
                    });
                }
                else
                {
                    enumerator.MoveNext();
                }
            }

            return callAst;
        }

        private static ReturnAst ParseReturn(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var returnAst = CreateAst<ReturnAst>(enumerator.Current);

            if (enumerator.MoveNext())
            {
                if (enumerator.Current.Type != TokenType.SemiColon)
                {
                    returnAst.Value = ParseExpression(enumerator, errors);
                }
            }
            else
            {
                errors.Add(new ParseError {Error = "Return does not have value", Token = enumerator.Last});
            }

            return returnAst;
        }

        private static IndexAst ParseIndex(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Initialize the index ast
            var index = CreateAst<IndexAst>(enumerator.Current);
            index.Name = enumerator.Current.Value;
            enumerator.MoveNext();

            // 2. Expect to get open bracket
            if (enumerator.Current?.Type != TokenType.OpenBracket)
            {
                var errorToken = enumerator.Current ?? enumerator.Last;
                errors.Add(new ParseError
                {
                    Error = $"Unexpected token in List index '{errorToken.Value}'",
                    Token = errorToken
                });
                return index;
            }

            enumerator.MoveNext();
            index.Index = ParseExpression(enumerator, errors, null, TokenType.CloseBracket);

            return index;
        }

        private static IAst ParseTopLevelDirective(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var directive = CreateAst<CompilerDirectiveAst>(enumerator.Current);

            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected compiler directive to have a value", Token = enumerator.Last});
                return null;
            }

            var token = enumerator.Current;
            switch (token.Value)
            {
                case "run":
                    directive.Type = DirectiveType.Run;
                    enumerator.MoveNext();
                    var ast = ParseLine(enumerator, errors);
                    if (ast != null)
                        directive.Value = ast;
                    break;
                case "if":
                    directive.Type = DirectiveType.If;
                    directive.Value = ParseConditional(enumerator, errors, true);
                    break;
                case "assert":
                    directive.Type = DirectiveType.Assert;
                    enumerator.MoveNext();
                    directive.Value = ParseExpression(enumerator, errors);
                    break;
                default:
                    errors.Add(new ParseError {Error = $"Unsupported top-level compiler directive '{token.Value}'", Token = token});
                    return null;
            }

            return directive;
        }

        private static IAst ParseCompilerDirective(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var directive = CreateAst<CompilerDirectiveAst>(enumerator.Current);

            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected compiler directive to have a value", Token = enumerator.Last});
                return null;
            }

            var token = enumerator.Current;
            switch (token.Value)
            {
                case "if":
                    directive.Type = DirectiveType.If;
                    directive.Value = ParseConditional(enumerator, errors);
                    _currentFunction.HasDirectives = true;
                    break;
                case "assert":
                    directive.Type = DirectiveType.Assert;
                    enumerator.MoveNext();
                    directive.Value = ParseExpression(enumerator, errors);
                    break;
                default:
                    errors.Add(new ParseError {Error = $"Unsupported compiler directive '{token.Value}'", Token = token});
                    return null;
            }

            return directive;
        }

        private static readonly HashSet<string> IntegerTypes = new()
        {
            "int", "u64", "s64", "u32", "s32", "u16", "s16", "u8", "s8"
        };

        private static readonly HashSet<string> FloatTypes = new() {"float", "float64"};

        private static TypeDefinition ParseType(TokenEnumerator enumerator, List<ParseError> errors, bool argument = false)
        {
            var typeDefinition = CreateAst<TypeDefinition>(enumerator.Current);
            typeDefinition.Name = enumerator.Current.Value;

            // Set the primitive type if necessary
            if (IntegerTypes.Contains(typeDefinition.Name))
            {
                if (typeDefinition.Name == "int")
                {
                    typeDefinition.Name = "s32";
                    typeDefinition.PrimitiveType = new IntegerType {Bytes = 4, Signed = true};
                }
                else
                {
                    var bytes = byte.Parse(typeDefinition.Name[1..]) / 8;
                    typeDefinition.PrimitiveType = new IntegerType {Bytes = (byte)bytes, Signed = typeDefinition.Name[0] == 's'};
                }
            }
            else if (FloatTypes.Contains(typeDefinition.Name))
            {
                typeDefinition.PrimitiveType = new FloatType {Bytes = typeDefinition.Name == "float" ? (byte)4 : (byte)8};
            }

            if (enumerator.Current.Type == TokenType.VarArgs)
            {
                if (!argument)
                {
                    errors.Add(new ParseError
                    {
                        Error = "Variable args type can only be used as an argument type", Token = enumerator.Current
                    });
                }
                return typeDefinition;
            }

            // Determine whether to parse a generic type, otherwise return
            if (enumerator.Peek()?.Type == TokenType.LessThan)
            {
                // Clear the '<' before entering loop
                enumerator.MoveNext();
                var commaRequiredBeforeNextType = false;
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;

                    if (token.Type == TokenType.GreaterThan)
                    {
                        if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                        {
                            errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in type",
                                Token = new Token {Type = TokenType.Comma, Line = token.Line}
                            });
                        }

                        break;
                    }

                    if (!commaRequiredBeforeNextType)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                typeDefinition.Generics.Add(ParseType(enumerator, errors));
                                commaRequiredBeforeNextType = true;
                                break;
                            default:
                                errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition", Token = token
                                });
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
                                errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition", Token = token
                                });
                                commaRequiredBeforeNextType = false;
                                break;
                        }
                    }
                }

                if (!typeDefinition.Generics.Any())
                {
                    errors.Add(new ParseError
                    {
                        Error = "Expected type to contain generics", Token = enumerator.Current ?? enumerator.Last
                    });
                }
            }

            while (enumerator.Peek()?.Type == TokenType.Asterisk)
            {
                enumerator.MoveNext();
                var pointerType = CreateAst<TypeDefinition>(enumerator.Current);
                pointerType.Name = "*";
                pointerType.Generics.Add(typeDefinition);
                typeDefinition = pointerType;
            }

            if (enumerator.Peek()?.Type == TokenType.OpenBracket)
            {
                // Skip over the open bracket and parse the expression
                enumerator.MoveNext();
                enumerator.MoveNext();
                typeDefinition.Count = ParseExpression(enumerator, errors, null, TokenType.CloseBracket);
            }

            if (enumerator.Peek()?.Type == TokenType.Pound)
            {
                enumerator.MoveNext();
                switch (enumerator.Peek()?.Value)
                {
                    case "c_array":
                        typeDefinition.CArray = true;
                        break;
                    case null:
                        errors.Add(new ParseError
                        {
                            Error = "Expected compiler directive value",
                            Token = enumerator.Last
                        });
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected compiler directive '{enumerator.Current.Value}'",
                            Token = enumerator.Current
                        });
                        break;
                }
                enumerator.MoveNext();
            }

            return typeDefinition;
        }

        private static bool TryParseType(TokenEnumerator enumerator, List<ParseError> errors, out TypeDefinition typeDef)
        {
            var steps = 0;
            if (TryParseType(enumerator.Current, enumerator, errors, ref steps, out typeDef))
            {
                enumerator.Move(steps);
                return true;
            }
            return false;
        }

        private static bool TryParseType(Token name, TokenEnumerator enumerator, List<ParseError> errors, ref int steps, out TypeDefinition typeDefinition)
        {
            typeDefinition = CreateAst<TypeDefinition>(name);
            typeDefinition.Name = name.Value;

            // Set the primitive type if necessary
            if (IntegerTypes.Contains(typeDefinition.Name))
            {
                if (typeDefinition.Name == "int")
                {
                    typeDefinition.Name = "s32";
                    typeDefinition.PrimitiveType = new IntegerType {Bytes = 4, Signed = true};
                }
                else
                {
                    var bytes = byte.Parse(typeDefinition.Name[1..]) / 8;
                    typeDefinition.PrimitiveType = new IntegerType {Bytes = (byte)bytes, Signed = typeDefinition.Name[0] == 's'};
                }
            }
            else if (FloatTypes.Contains(typeDefinition.Name))
            {
                typeDefinition.PrimitiveType = new FloatType {Bytes = typeDefinition.Name == "float" ? (byte)4 : (byte)8};
            }

            // Determine whether to parse a generic type, otherwise return
            if (enumerator.Peek(steps)?.Type == TokenType.LessThan)
            {
                // Clear the '<' before entering loop
                steps++;
                var commaRequiredBeforeNextType = false;
                while (enumerator.Peek(steps) != null)
                {
                    var token = enumerator.Peek(steps++);

                    if (token.Type == TokenType.GreaterThan)
                    {
                        if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                        {
                            return false;
                        }
                        break;
                    }

                    if (!commaRequiredBeforeNextType)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                if (!TryParseType(token, enumerator, errors, ref steps, out var genericType))
                                {
                                    return false;
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

            while (enumerator.Peek(steps)?.Type == TokenType.Asterisk)
            {
                var pointerType = CreateAst<TypeDefinition>(enumerator.Peek(steps));
                pointerType.Name = "*";
                pointerType.Generics.Add(typeDefinition);
                typeDefinition = pointerType;
                steps++;
            }

            return true;
        }

        private static TypeDefinition InferType(Token token, List<ParseError> errors)
        {
            var typeDefinition = CreateAst<TypeDefinition>(token);
            switch (token.Type)
            {
                case TokenType.Literal:
                    typeDefinition.Name = "string";
                    return typeDefinition;
                case TokenType.Number:
                    if (token.Flags == TokenFlags.None)
                    {
                        if (int.TryParse(token.Value, out _))
                        {
                            typeDefinition.Name = "s32";
                            typeDefinition.PrimitiveType = new IntegerType {Bytes = 4, Signed = true};
                            return typeDefinition;
                        }

                        if (long.TryParse(token.Value, out _))
                        {
                            typeDefinition.Name = "s64";
                            typeDefinition.PrimitiveType = new IntegerType {Bytes = 8, Signed = true};
                            return typeDefinition;
                        }

                        if (ulong.TryParse(token.Value, out _))
                        {
                            typeDefinition.Name = "u64";
                            typeDefinition.PrimitiveType = new IntegerType {Bytes = 8, Signed = false};
                            return typeDefinition;
                        }

                        errors.Add(new ParseError
                        {
                            Error = $"Invalid integer '{token.Value}', must be 64 bits or less", Token = token
                        });
                        return null;
                    }

                    if (token.Flags.HasFlag(TokenFlags.Float))
                    {
                        if (float.TryParse(token.Value, out _))
                        {
                            typeDefinition.Name = "float";
                            typeDefinition.PrimitiveType = new FloatType {Bytes = 4};
                            return typeDefinition;
                        }

                        if (double.TryParse(token.Value, out _))
                        {
                            typeDefinition.Name = "float64";
                            typeDefinition.PrimitiveType = new FloatType {Bytes = 8};
                            return typeDefinition;
                        }

                        errors.Add(new ParseError
                        {
                            Error = $"Invalid floating point number '{token.Value}', must be single or double precision",
                            Token = token
                        });
                        return null;
                    }

                    if (token.Flags.HasFlag(TokenFlags.HexNumber))
                    {
                        if (token.Value.Length == 2)
                        {
                            errors.Add(new ParseError {Error = $"Invalid number '{token.Value}'", Token = token});
                            return null;
                        }

                        var value = token.Value.Substring(2);
                        if (value.Length <= 8)
                        {
                            if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
                            {
                                token.Value = result.ToString();
                                typeDefinition.Name = "u32";
                                typeDefinition.PrimitiveType = new IntegerType {Bytes = 4};
                                return typeDefinition;
                            }
                        }
                        else if (value.Length <= 16)
                        {
                            if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
                            {
                                token.Value = result.ToString();
                                typeDefinition.Name = "u64";
                                typeDefinition.PrimitiveType = new IntegerType {Bytes = 8};
                                return typeDefinition;
                            }
                        }
                        errors.Add(new ParseError {Error = $"Invalid integer '{token.Value}'", Token = token});
                        return null;
                    }
                    errors.Add(new ParseError {Error = $"Unable to determine type of token '{token.Value}'", Token = token});
                    return null;
                case TokenType.Boolean:
                    typeDefinition.Name = "bool";
                    return typeDefinition;
                default:
                    errors.Add(new ParseError {Error = $"Unable to determine type of token '{token.Value}'", Token = token});
                    return null;
            }
        }

        private static CastAst ParseCast(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var castAst = CreateAst<CastAst>(enumerator.Current);

            // 1. Try to get the open paren to begin the cast
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.OpenParen)
            {
                errors.Add(new ParseError {Error = "Expected '(' after cast", Token = enumerator.Current ?? enumerator.Last});
                return null;
            }

            // 2. Get the target type
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected to get the target type for the cast", Token = enumerator.Last});
                return null;
            }
            castAst.TargetType = ParseType(enumerator, errors);

            // 3. Expect to get a comma
            enumerator.MoveNext();
            if (enumerator.Current?.Type != TokenType.Comma)
            {
                errors.Add(new ParseError {Error = "Expected ',' after type in cast", Token = enumerator.Current ?? enumerator.Last});
                return null;
            }


            // 4. Get the value expression
            if (!enumerator.MoveNext())
            {
                errors.Add(new ParseError {Error = "Expected to get the value for the cast", Token = enumerator.Last});
                return null;
            }
            castAst.Value = ParseExpression(enumerator, errors, null, TokenType.CloseParen);

            return castAst;
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
                // Handle single character operators
                default:
                    var op = (Operator)token.Value[0];
                    return Enum.IsDefined(typeof(Operator), op) ? op : Operator.None;
            }
        }
    }
}
