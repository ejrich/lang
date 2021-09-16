using System;
using System.Collections.Generic;
using System.IO;

namespace Lang.Parsing
{
    public interface IParser
    {
        ParseResult Parse(List<string> projectFiles);
    }

    public class Parser : IParser
    {
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
            var fileContents = File.ReadAllText(file);

            var tokens = GetTokens(fileContents);
            foreach (var token in tokens)
            {
                Console.WriteLine($"{token.Type}, {token.Value}");
            }
        }

        private IEnumerable<Token> GetTokens(string fileContents)
        {
            Token currentToken = null;
            foreach (var character in fileContents)
            {
                if (IgnoreChar(character))
                {
                    // Console.WriteLine($"Ignoring character: {character}");
                    if (currentToken != null)
                        yield return currentToken;
                    currentToken = null;
                    continue;
                }

                // Get token from character, determine to emit value
                var tokenType = GetTokenType(character);

                if (ContinueToken(currentToken, tokenType))
                {
                    currentToken.Value += character;
                }
                else
                {
                    if (currentToken != null)
                        yield return currentToken;

                    currentToken = new Token
                    {
                        Type = tokenType,
                        Value = $"{character}"
                    };
                }
            }

            if (currentToken != null) yield return currentToken;
        }

        private static bool IgnoreChar(char token)
        {
            return Char.IsWhiteSpace(token);
        }

        private static TokenType GetTokenType(char character)
        {
            var token = (TokenType) character;

            return Enum.IsDefined(typeof(TokenType), token) ? token : TokenType.Token;
        }

        private static bool ContinueToken(Token currentToken, TokenType type)
        {
            if (currentToken == null) return false;

            return currentToken.Type switch
            {
                TokenType.Token => type == TokenType.Token,
                // TODO More validation eventually
                _ => false
            };
        }
    }
}
