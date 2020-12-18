﻿using System.Collections.Generic;
using System.Linq;

namespace Lang.Parsing
{
    public class FileParseResult
    {
        public string File { get; init; }
        public bool Success => !Errors.Any();
        public List<IAst> SyntaxTrees { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }

    public class ParseResult
    {
        public bool Success => !Errors.Any();
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

        public Parser(ILexer lexer) => _lexer = lexer;

        public ParseResult Parse(List<string> projectFiles)
        {
            var parseResult = new ParseResult();

            foreach (var file in projectFiles)
            {
                var fileParseResult = ParseFile(file);
                if (!fileParseResult.Success)
                {
                    foreach (var error in fileParseResult.Errors)
                    {
                        error.File = file;
                    }
                    parseResult.Errors.AddRange(fileParseResult.Errors);
                }
                else if (parseResult.Success)
                {
                    parseResult.SyntaxTrees.AddRange(fileParseResult.SyntaxTrees);
                }
            }

            return parseResult;
        }

        private FileParseResult ParseFile(string file)
        {
            // 1. Load file tokens
            var tokens = _lexer.LoadFileTokens(file);

            // 2. Determine if any errors happened while lexing
            var parseResult = new FileParseResult {File = file};
            foreach (var token in tokens.Where(token => token.Error))
            {
                parseResult.Errors.Add(new ParseError
                {
                    Error = $"Unexpected token '{token.Value}'",
                    Token = token
                });
            }

            // 3. Iterate through tokens, tracking different ASTs
            IEnumerator<Token> enumerator = tokens.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current!;
                switch (token!.Type)
                {
                    case TokenType.Token:
                        parseResult.SyntaxTrees.Add(ParseFunction(ref enumerator, parseResult));
                        break;
                    default:
                        parseResult.Errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}'",
                            Token = enumerator.Current
                        });
                        break;
                }
            }

            return parseResult;
        }

        private static IAst ParseFunction(ref IEnumerator<Token> enumerator, FileParseResult parseResult)
        {
            // 1. Determine return type and name of the function
            var function = new FunctionAst
            {
                ReturnType = ParseType(ref enumerator, parseResult),
                Name = enumerator.Current?.Value
            };

            // 2. Find open paren to start parsing arguments
            enumerator.MoveNext();
            if (enumerator.Current.Type != TokenType.OpenParen)
            {
                // Add an error to the function AST and continue until open paren
                parseResult.Errors.Add(new ParseError
                {
                    Error = "Unexpected token in function definition",
                    Token = enumerator.Current
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenParen)
                    enumerator.MoveNext();
            }

            // 3. Parse arguments until a close paren
            var commaRequiredBeforeNextArgument = false;
            Variable currentArgument = null;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseParen)
                {
                    if (!commaRequiredBeforeNextArgument && function.Arguments.Any())
                    {
                        parseResult.Errors.Add(new ParseError
                        {
                            Error = "Unexpected comma in arguments",
                            Token = new Token { Type = TokenType.Comma, Line = token.Line }
                        });
                    }
                    break;
                }

                switch (token.Type)
                {
                    case TokenType.Token:
                        if (commaRequiredBeforeNextArgument)
                        {
                            parseResult.Errors.Add(new ParseError
                            {
                                Error = "Comma required after declaring an argument",
                                Token = token
                            });
                        }
                        else if (currentArgument == null)
                        {
                            currentArgument = new Variable {Type = ParseType(ref enumerator, parseResult)};
                        }
                        else
                        {
                            currentArgument.Name = token.Value;
                            function.Arguments.Add(currentArgument);
                            currentArgument = null;
                            commaRequiredBeforeNextArgument = true;
                        }
                        break;
                    case TokenType.Comma:
                        if (!commaRequiredBeforeNextArgument)
                        {
                            parseResult.Errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in arguments",
                                Token = token
                            });
                        }
                        commaRequiredBeforeNextArgument = false;
                        break;
                    default:
                        parseResult.Errors.Add(new ParseError
                        {
                            Error = "Unexpected token in arguments",
                            Token = token
                        });
                        break;
                }
            }

            // 4. Find open brace to start parsing body
            enumerator.MoveNext();
            if (enumerator.Current.Type != TokenType.OpenBrace)
            {
                // Add an error to the function AST and continue until open paren
                parseResult.Errors.Add(new ParseError
                {
                    Error = "Unexpected token in function definition",
                    Token = enumerator.Current
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenBrace)
                    enumerator.MoveNext();
            }

            // 5. Parse function body
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
                        if (token.Value == "return")
                        {
                            function.Children.Add(ParseReturn(ref enumerator, parseResult));
                        }
                        // TODO Add more cases
                        break;
                }
            }
            return function;
        }

        private static TypeDefinition ParseType(ref IEnumerator<Token> enumerator, FileParseResult parseResult)
        {
            var typeDefinition = new TypeDefinition
            {
                Type = enumerator.Current!.Value
            };

            // Determine whether to parse a generic type, otherwise return
            enumerator.MoveNext();
            if (enumerator.Current!.Type == TokenType.LessThan)
            {
                var commaRequiredBeforeNextType = false;
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;

                    if (token.Type == TokenType.GreaterThan)
                    {
                        if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                        {
                            parseResult.Errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in type",
                                Token = new Token { Type = TokenType.Comma, Line = token.Line }
                            });
                        }
                        return typeDefinition;
                    }
                    
                    if (!commaRequiredBeforeNextType)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                typeDefinition.Generics.Add(token.Value);
                                commaRequiredBeforeNextType = true;
                                break;
                            default:
                                parseResult.Errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition",
                                    Token = token
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
                                parseResult.Errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition",
                                    Token = token
                                });
                                commaRequiredBeforeNextType = false;
                                break;
                        }
                    }
                }
            }

            return typeDefinition;
        }

        private static IAst ParseReturn(ref IEnumerator<Token> enumerator, FileParseResult parseResult)
        {
            var returnAst = new ReturnAst();

            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.SemiColon)
                {
                    break;
                }

                switch (token.Type)
                {
                    case TokenType.Token:
                    case TokenType.Number:
                    case TokenType.Literal:
                        returnAst.Value = new ConstantAst
                        {
                            Type = token.InferType(out var error),
                            Value = token.Value
                        };
                        if (error != null)
                            parseResult.Errors.Add(error);
                        // TODO Add support for expressions and calls
                        break;
                    default:
                        parseResult.Errors.Add(new ParseError
                        {
                            Error = "Unexpected token in return statement",
                            Token = token
                        });
                        break;
                }
            }

            return returnAst;
        }
    }
}
