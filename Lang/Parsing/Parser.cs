using System;
using System.Collections.Generic;

namespace Lang.Parsing
{
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
            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Token:
                        if (function.ReturnType == null)
                            function.ReturnType = token.Value;
                        else if (function.Name == null)
                            function.Name = token.Value;
                        break;
                    case TokenType.OpenParen:
                        break;
                    case TokenType.CloseParen:
                        break;
                    case TokenType.OpenBrace:
                        break;
                    case TokenType.CloseBrace:
                        break;
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
        }
    }
}
