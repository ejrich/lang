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
        public List<Token> LoadFileTokens(string filePath)
        {
            var fileContents = File.ReadAllText(filePath);

            return GetTokens(fileContents).ToList();
        }

        private IEnumerable<Token> GetTokens(string fileContents)
        {
            var lexerStatus = new LexerStatus();

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
                if (lexerStatus.ReadingComment)
                {
                    if (lexerStatus.MultiLineComment)
                    {
                        if (closingMultiLineComment && character == '/')
                        {
                            lexerStatus.MultiLineComment = false;
                            lexerStatus.ReadingComment = false;
                            currentToken = null;
                        }

                        closingMultiLineComment = character == '*';
                    }
                    else if (character == '\n')
                    {
                        lexerStatus.ReadingComment = false;
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

                if (ContinueToken(currentToken, tokenType, lexerStatus))
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

        private bool ContinueToken(Token currentToken, TokenType type, LexerStatus lexerStatus)
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
                            lexerStatus.ReadingComment = true;
                            return true;
                        case TokenType.Multiply:
                            currentToken.Type = TokenType.Comment;
                            lexerStatus.ReadingComment = true;
                            lexerStatus.MultiLineComment = true;
                            return true;
                        default:
                            return false;
                    }
                // TODO More validation eventually
                default:
                    return false;
            }
        }

        private class LexerStatus
        {
            public bool ReadingComment { get; set; }
            public bool MultiLineComment { get; set; }
        }
    }
}
