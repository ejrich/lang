using System.Collections.Generic;
using System.Linq;

namespace Lang.Parsing
{
    public class FileParseResult
    {
        public string File { get; set; }
        public bool Success => !Errors.Any();
        public List<IAst> SyntaxTrees { get; } = new();
        public List<ParseError> Errors { get; } = new();
    }

    public class ParseResult
    {
        public bool Success { get; set; } = true;
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
                    parseResult.Errors.AddRange(fileParseResult.Errors);
                    parseResult.Success = false;
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

            var parseResult = new FileParseResult {File = file};

            // 2. Iterate through tokens, tracking different ASTs
            IEnumerator<Token> enumerator = tokens.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current!;
                switch (token!.Type)
                {
                    case TokenType.Token:
                        parseResult.SyntaxTrees.Add(ParseFunction(ref enumerator, out var errors));
                        parseResult.Errors.AddRange(errors);
                        break;
                    default:
                        parseResult.Errors.Add(new ParseError
                        {
                            Error = "Unexpected token", Token = enumerator.Current
                        });
                        break;
                }
            }

            return parseResult;
        }

        private static IAst ParseFunction(ref IEnumerator<Token> enumerator, out List<ParseError> errors)
        {
            // 1. Determine return type and name of the function
            var function = new FunctionAst
            {
                ReturnType = ParseType(ref enumerator, out errors),
                Name = enumerator.Current?.Value
            };

            // 2. Find open paren to start parsing arguments
            enumerator.MoveNext();
            if (enumerator.Current.Type != TokenType.OpenParen)
            {
                // Add an error to the function AST and continue until open paren
                errors.Add(new ParseError
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
                        errors.Add(new ParseError
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
                            errors.Add(new ParseError
                            {
                                Error = "Comma required after declaring an argument",
                                Token = token
                            });
                        }
                        else if (currentArgument == null)
                        {
                            currentArgument = new Variable {Type = ParseType(ref enumerator, out var typeErrors)};
                            errors.AddRange(typeErrors);
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
                            errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in arguments",
                                Token = token
                            });
                        }
                        commaRequiredBeforeNextArgument = false;
                        break;
                    default:
                        errors.Add(new ParseError
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
                errors.Add(new ParseError
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
                            function.Children.Add(ParseReturn(ref enumerator, out var returnErrors));
                            errors.AddRange(returnErrors);
                        }
                        // TODO Add more cases
                        break;
                }
            }
            return function;
        }

        private static TypeDefinition ParseType(ref IEnumerator<Token> enumerator, out List<ParseError> errors)
        {
            var typeDefinition = new TypeDefinition
            {
                Type = enumerator.Current!.Value
            };
            errors = new List<ParseError>();

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
                            errors.Add(new ParseError
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
                                errors.Add(new ParseError
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
                                errors.Add(new ParseError
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

        private static IAst ParseReturn(ref IEnumerator<Token> enumerator, out List<ParseError> errors)
        {
            var returnAst = new ReturnAst();
            errors = new List<ParseError>();

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
                        returnAst.Children.Add(new ConstantAst {Value = token.Value});
                        // TODO Add support for expressions and calls
                        break;
                    default:
                        errors.Add(new ParseError
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
