using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lang.Parsing
{
    public interface ILexer
    {
        List<Token> LoadFileTokens(string filePath);
    }

    public class Lexer : ILexer
    {
        private bool _readingComment;
        private bool _multiLineComment;

        public List<Token> LoadFileTokens(string filePath)
        {
            var fileContents = File.ReadAllText(filePath);

            return GetTokens(fileContents).ToList();
        }

        private IEnumerable<Token> GetTokens(string fileContents)
        {
            Token currentToken = null;
            var closingMultiLineComment = false;
            var line = 1;
            var column = 0;
            foreach (var character in fileContents)
            {
                column++;
                if (character == '\n')
                {
                    line++;
                    column = 0;
                }
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

                if (char.IsWhiteSpace(character))
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
                        Value = $"{character}",
                        Line = line,
                        Column = column
                    };
                }
            }

            if (currentToken != null) yield return currentToken;
        }

        private static TokenType GetTokenType(char character)
        {
            var token = (TokenType)character;

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
