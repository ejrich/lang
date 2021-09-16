using System.Collections.Generic;
using System.Linq;

namespace Lang.Parsing
{
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

            public Token Peek(int steps = 0)
            {
                return _tokens.Count > _index + steps ? _tokens[_index + steps] : null;
            }
        }

        public Parser(ILexer lexer) => _lexer = lexer;

        public ParseResult Parse(List<string> projectFiles)
        {
            var parseResult = new ParseResult();

            foreach (var file in projectFiles)
            {
                var syntaxTrees = ParseFile(file, out var errors);
                if (errors.Any())
                {
                    foreach (var error in errors)
                    {
                        error.File = file;
                    }
                    parseResult.Errors.AddRange(errors);
                }
                else if (parseResult.Success)
                {
                    parseResult.SyntaxTrees.AddRange(syntaxTrees);
                }
            }

            return parseResult;
        }

        private List<IAst> ParseFile(string file, out List<ParseError> errors)
        {
            // 1. Load file tokens
            var tokens = _lexer.LoadFileTokens(file, out errors);

            // 3. Iterate through tokens, tracking different ASTs
            var syntaxTrees = new List<IAst>();
            var enumerator = new TokenEnumerator(tokens);
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current!;
                switch (token!.Type)
                {
                    case TokenType.Token:
                        syntaxTrees.Add(ParseFunction(enumerator, errors));
                        break;
                    default:
                        errors.Add(new ParseError
                        {
                            Error = $"Unexpected token '{token.Value}'",
                            Token = enumerator.Current
                        });
                        break;
                }
            }

            return syntaxTrees;
        }

        private static IAst ParseFunction(TokenEnumerator enumerator, List<ParseError> errors)
        {
            // 1. Determine return type and name of the function
            var function = new FunctionAst
            {
                ReturnType = ParseType(enumerator, errors),
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
                            currentArgument = new Variable {Type = ParseType(enumerator, errors)};
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
                        switch (token.Value)
                        {
                            case "return":
                                function.Children.Add(ParseReturn(enumerator, errors));
                                break;
                            case "var":
                                function.Children.Add(ParseDeclaration(enumerator, errors));
                                break;
                        }
                        // TODO Add more cases
                        break;
                }
            }
            return function;
        }

        private static IAst ParseDeclaration(TokenEnumerator enumerator, List<ParseError> errors)
        {
            var declaration = new DeclarationAst();

            // 1. Expect to get variable name
            enumerator.MoveNext();
            switch (enumerator.Current.Type)
            {
                case TokenType.Token:
                    declaration.Name = enumerator.Current.Value;
                    break;
                case TokenType.SemiColon:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token in declaration '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    return declaration;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token in declaration '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    break;
            }

            // 2. Expect to get equals sign
            enumerator.MoveNext();
            if (enumerator.Current.Type != TokenType.Equals)
            {
                errors.Add(new ParseError
                {
                    Error = $"Expected '=' in declaration'",
                    Token = enumerator.Current
                });
            }

            // 3. Parse expression, constant, or another token as the value
            enumerator.MoveNext();
            switch (enumerator.Current.Type)
            {
                case TokenType.Number:
                case TokenType.Boolean:
                case TokenType.Literal:
                    if (enumerator.Peek()?.Type == TokenType.SemiColon)
                    {
                        declaration.Value = new ConstantAst
                        {
                            Type = enumerator.Current.InferType(out var error),
                            Value = enumerator.Current.Value
                        };
                        enumerator.MoveNext();
                    }
                    // TODO Handle expressions
                    break;
                case TokenType.Token:
                    // TODO Handle calls, expressions, or variables
                    break;
                default:
                    errors.Add(new ParseError
                    {
                        Error = $"Unexpected token in declaration '{enumerator.Current.Value}'",
                        Token = enumerator.Current
                    });
                    break;
            }
            return declaration;
        }

        private static TypeDefinition ParseType(TokenEnumerator enumerator, List<ParseError> errors)
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

        private static IAst ParseReturn(TokenEnumerator enumerator, List<ParseError> errors)
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
                    case TokenType.Boolean:
                    case TokenType.Literal:
                        returnAst.Value = new ConstantAst
                        {
                            Type = token.InferType(out var error),
                            Value = token.Value
                        };
                        if (error != null)
                            errors.Add(error);
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
