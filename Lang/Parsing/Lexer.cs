using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lang.Parsing
{
    public interface ILexer
    {
        List<Token> LoadFileTokens(string path);
    }

    public class Lexer : ILexer
    {
        public List<Token> LoadFileTokens(string path)
        {
            var fileContents = File.ReadAllText(path);

            #if DEBUG
            var tokens = GetTokens(fileContents).ToList();
            foreach (var token in tokens)
                Console.WriteLine($"{token.Type}, {token.Value}");

            return tokens;
            #else
            return GetTokens(fileContents).ToList();
            #endif
        }

        private static IEnumerable<Token> GetTokens(string fileContents)
        {
            Token currentToken = null;
            foreach (var character in fileContents)
            {
                if (IgnoreChar(character))
                {
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
