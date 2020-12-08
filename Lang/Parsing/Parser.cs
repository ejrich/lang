using System;
using System.Collections.Generic;

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
            var function = new FunctionAst();
            var parsingArgs = false;
            var parsingBody = false;
            Variable argument = null;
            ReturnAst returnAst = null;
            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Token:
                        if (function.ReturnType == null)
                            function.ReturnType = token.Value;
                        else if (function.Name == null)
                            function.Name = token.Value;
                        else if (parsingArgs)
                        {
                            if (argument == null)
                                argument = new Variable { Type = token.Value };
                            else
                            {
                                argument.Name = token.Value;
                                function.Arguments.Add(argument);
                                argument = null;
                            }
                        }
                        else if (parsingBody)
                        {
                            if (token.Value == "return")
                            {
                                returnAst = new ReturnAst();
                            }
                            else if (returnAst != null)
                            {
                                returnAst.Children.Add(new ConstantAst { Value = token.Value });
                                function.Children.Add(returnAst);
                                returnAst = null;
                            }
                        }
                        break;
                    case TokenType.OpenParen:
                        parsingArgs = true;
                        break;
                    case TokenType.CloseParen:
                        parsingArgs = false;
                        break;
                    case TokenType.OpenBrace:
                        parsingBody = true;
                        break;
                    case TokenType.CloseBrace:
                        parsingBody = false;
                        break;
                    // TODO Below not used right now, will be once the function example is expanded upon
                    case TokenType.Not:
                        break;
                    case TokenType.And:
                        break;
                    case TokenType.Or:
                        break;
                    case TokenType.Add:
                        break;
                    case TokenType.Minus:
                        break;
                    case TokenType.Multiply:
                        break;
                    case TokenType.Divide:
                        break;
                    case TokenType.Equals:
                        break;
                    case TokenType.Colon:
                        break;
                    case TokenType.SemiColon:
                        break;
                    case TokenType.Quote:
                        break;
                    case TokenType.LessThan:
                        break;
                    case TokenType.GreaterThan:
                        break;
                    case TokenType.Comma:
                        break;
                    case TokenType.Period:
                        break;
                }
            }
            Console.WriteLine($"{function.Name} {function.ReturnType}");
        }
    }
}
