using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lang.Parsing
{
    public interface ILexer
    {
        List<Token> LoadFileTokens(string filePath);
    }

    public class Lexer : ILexer
    {
        private readonly Regex _escapableCharacters = new(@"['""\\abfnrtv]");

        public List<Token> LoadFileTokens(string filePath)
        {
            var fileContents = File.ReadAllText(filePath);

            return GetTokens(fileContents, filePath).ToList();
        }

        private IEnumerable<Token> GetTokens(string fileContents, string filePath)
        {
            var lexerStatus = new LexerStatus();

            Token currentToken = null;
            var closingMultiLineComment = false;
            var literalEscapeToken = false;
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

                // Skip through comments
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

                // Interpret string literals
                if (currentToken?.Type == TokenType.Quote)
                {
                    currentToken.Type = TokenType.Literal;
                    currentToken.Value = string.Empty;
                }
                if (currentToken?.Type == TokenType.Literal)
                {
                    if (character == '\\' && !literalEscapeToken)
                    {
                        currentToken.Value += character;
                        literalEscapeToken = true;
                    }
                    else if (literalEscapeToken)
                    {
                        if (!_escapableCharacters.IsMatch(character.ToString()))
                        {
                            currentToken.Error = true;
                        }
                        currentToken.Value += character;
                        literalEscapeToken = false;
                    }
                    else
                    {
                        if (character == '"')
                        {
                            yield return currentToken;
                            currentToken = null;
                        }
                        else
                        {
                            currentToken.Value += character;
                        }
                    }
                    continue;
                }

                // Skip over whitespace and emit the current token if not null
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
                    currentToken!.Value += character;
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

            if (char.IsDigit(character)) return TokenType.Number;

            return Enum.IsDefined(typeof(TokenType), token) ? token : TokenType.Token;
        }

        private static bool ContinueToken(Token currentToken, TokenType type, LexerStatus lexerStatus)
        {
            if (currentToken == null) return false;

            switch (currentToken.Type)
            {
                case TokenType.Token:
                    return type == TokenType.Token || type == TokenType.Number;
                case TokenType.Number:
                    return type == TokenType.Number || type == TokenType.Period;
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
