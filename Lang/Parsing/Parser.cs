using System;
using System.Collections.Generic;
using System.Linq;

namespace Lang.Parsing
{
    public class ParseResult
    {
        // TODO Implement various fields
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
            var result = new ParseResult();

            foreach (var file in projectFiles)
            {
                ParseFile(file);
            }

            return result;
        }

        private void ParseFile(string file)
        {
            // 1. Load file tokens
            var tokens = _lexer.LoadFileTokens(file);

            // 2. Iterate through tokens, tracking function definitions
            var asts = new List<IAst>();
            IEnumerator<Token> enumerator = tokens.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current!;
                switch (token!.Type)
                {
                    case TokenType.Token:
                        asts.Add(ParseFunction(ref enumerator));
                        break;
                    default:
                        Console.WriteLine($"Unexpected token in file \"{file}\": {enumerator.Current}");
                        Environment.Exit(ErrorCodes.ParsingError);
                        break;
                }
            }
        }

        private static IAst ParseFunction(ref IEnumerator<Token> enumerator)
        {
            // 1. Determine return type and name of the function
            var function = new FunctionAst
            {
                ReturnType = ParseType(ref enumerator),
                Name = enumerator.Current?.Value
            };

            // 2. Find open paren to start parsing arguments
            enumerator.MoveNext();
            if (enumerator.Current.Type != TokenType.OpenParen)
            {
                // Add an error to the function AST and continue until open paren
                function.Errors.Add(new ParseError
                {
                    Error = "Unexpected token in function definition",
                    Token = enumerator.Current
                });
                while (enumerator.Current != null && enumerator.Current.Type != TokenType.OpenParen)
                    enumerator.MoveNext();
            }

            // 3. Parse arguments until a close paren
            var addedArgument = false;
            Variable currentArgument = null;
            while (enumerator.MoveNext())
            {
                var token = enumerator.Current;

                if (token.Type == TokenType.CloseParen)
                {
                    if (!addedArgument && function.Arguments.Any())
                    {
                        function.Errors.Add(new ParseError
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
                        if (addedArgument)
                        {
                            function.Errors.Add(new ParseError
                            {
                                Error = "Comma required after declaring an argument",
                                Token = token
                            });
                        }
                        else if (currentArgument == null)
                        {
                            currentArgument = new Variable {Type = ParseType(ref enumerator)};
                        }
                        else
                        {
                            currentArgument.Name = token.Value;
                            function.Arguments.Add(currentArgument);
                            currentArgument = null;
                            addedArgument = true;
                        }
                        break;
                    case TokenType.Comma:
                        if (!addedArgument)
                        {
                            function.Errors.Add(new ParseError
                            {
                                Error = "Unexpected comma in arguments",
                                Token = token
                            });
                        }
                        addedArgument = false;
                        break;
                    default:
                        function.Errors.Add(new ParseError
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
                function.Errors.Add(new ParseError
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
                            function.Children.Add(ParseReturn(ref enumerator));
                        }
                        // TODO Add more cases
                        break;
                }
            }
            return function;
        }

        private static TypeDefinition ParseType(ref IEnumerator<Token> enumerator)
        {
            var typeDefinition = new TypeDefinition
            {
                Type = enumerator.Current!.Value
            };
            // Determine whether to parse a generic type, otherwise return
            enumerator.MoveNext();
            if (enumerator.Current!.Type == TokenType.LessThan)
            {
                var addedGeneric = false;
                while (enumerator.MoveNext())
                {
                    var token = enumerator.Current;
                    if (!addedGeneric)
                    {
                        switch (token.Type)
                        {
                            case TokenType.Token:
                                typeDefinition.Generics.Add(token.Value);
                                addedGeneric = true;
                                break;
                            case TokenType.GreaterThan:
                                return typeDefinition;
                            default:
                                typeDefinition.Errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition",
                                    Token = token
                                });
                                addedGeneric = true;
                                break;
                        }
                    }
                    else
                    {
                        switch (token.Type)
                        {
                            case TokenType.GreaterThan:
                                return typeDefinition;
                            case TokenType.Comma:
                                addedGeneric = false;
                                break;
                            default:
                                typeDefinition.Errors.Add(new ParseError
                                {
                                    Error = "Unexpected token in type definition",
                                    Token = token
                                });
                                addedGeneric = false;
                                break;
                        }
                    }
                }
            }

            return typeDefinition;
        }

        private static IAst ParseReturn(ref IEnumerator<Token> enumerator)
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
                        returnAst.Children.Add(new ConstantAst {Value = token.Value});
                        // TODO Add support for expressions and calls
                        break;
                    default:
                        returnAst.Errors.Add(new ParseError
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
