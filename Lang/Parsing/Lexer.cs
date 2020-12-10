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
        private bool _readingComment;
        private bool _multiLineComment;

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

        private IEnumerable<Token> GetTokens(string fileContents)
        {
            Token currentToken = null;
            var closingMultiLineComment = false;
            foreach (var character in fileContents)
            {
                if (_readingComment)
                {
                    if (_multiLineComment)
                    {
                        if (closingMultiLineComment && character == '/')
                        {
                            _multiLineComment = false;
                            _readingComment = false;
                            currentToken = null;
                        }
                        closingMultiLineComment = character == '*';
                    }
                    else if (character == '\n')
                    {
                        _readingComment = false;
                        currentToken = null;
                    }
                    continue;
                }
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

        private bool ContinueToken(Token currentToken, TokenType type)
        {
            if (currentToken == null) return false;

            switch (currentToken.Type)
            {
                case TokenType.Token:
                    return type == TokenType.Token;
                case TokenType.Divide:
                    switch (type)
                    {
                        case TokenType.Divide:
                            currentToken.Type = TokenType.Comment;
                            _readingComment = true;
                            return true;
                        case TokenType.Multiply:
                            currentToken.Type = TokenType.Comment;
                            _readingComment = true;
                            _multiLineComment = true;
                            return true;
                        default:
                            return false;
                    }
                // TODO More validation eventually
                default:
                    return false;
            }
        }
    }
}
