using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lang
{
    public interface ILexer
    {
        List<Token> LoadFileTokens(string filePath, int fileIndex, out List<ParseError> errors);
    }

    public class Lexer : ILexer
    {
        private readonly IDictionary<char, char> _escapableCharacters = new Dictionary<char, char>
        {
            {'"', '"'},
            {'\\', '\\'},
            {'a', '\a'},
            {'b', '\b'},
            {'f', '\f'},
            {'n', '\n'},
            {'r', '\r'},
            {'t', '\t'},
            {'v', '\v'}
        };

        private readonly IDictionary<string, TokenType> _reservedTokens = new Dictionary<string, TokenType>
        {
            {"return", TokenType.Return},
            {"true", TokenType.Boolean},
            {"false", TokenType.Boolean},
            {"if", TokenType.If},
            {"else", TokenType.Else},
            {"then", TokenType.Then},
            {"while", TokenType.While},
            {"each", TokenType.Each},
            {"in", TokenType.In},
            {"struct", TokenType.Struct},
            {"enum", TokenType.Enum},
            {"null", TokenType.Null},
            {"cast", TokenType.Cast},
            {"operator", TokenType.Operator}
        };

        public List<Token> LoadFileTokens(string filePath, int fileIndex, out List<ParseError> errors)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(fileStream);

            errors = new List<ParseError>();
            return GetTokens(reader, fileIndex, errors).ToList();
        }

        private IEnumerable<Token> GetTokens(StreamReader reader, int fileIndex, List<ParseError> errors)
        {
            var lexerStatus = new LexerStatus();

            Token currentToken = null;
            var closingMultiLineComment = false;
            var literalEscapeToken = false;
            var line = 1;
            var column = 0;

            while (reader.Peek() > 0)
            {
                var character = (char)reader.Read();

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

                // Add characters to the string literal
                if (currentToken?.Type == TokenType.Literal)
                {
                    if (character == '\\' && !literalEscapeToken)
                    {
                        literalEscapeToken = true;
                    }
                    else if (literalEscapeToken)
                    {
                        if (_escapableCharacters.TryGetValue(character, out var escapedCharacter))
                        {
                            currentToken.Value += escapedCharacter;
                        }
                        else
                        {
                            currentToken.Error = true;
                            currentToken.Value += character;
                        }
                        literalEscapeToken = false;
                    }
                    else
                    {
                        if (character == '"')
                        {
                            if (currentToken.Error)
                            {
                                errors.Add(new ParseError
                                {
                                    Error = $"Unexpected token '{currentToken.Value}'", Token = currentToken
                                });
                            }

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

                // Interpret character literals
                if (currentToken?.Type == TokenType.Apostrophe)
                {
                    currentToken.Type = TokenType.Character;
                    currentToken.Value = $"{character}";
                    continue;
                }

                if (currentToken?.Type == TokenType.Character)
                {
                    yield return currentToken;
                    if (character != '\'')
                    {
                        errors.Add(new ParseError {Error = $"Expected a single digit character", Token = currentToken});
                    }
                    else
                    {
                        currentToken = null;
                    }
                    continue;
                }

                // Skip over whitespace and emit the current token if not null
                if (char.IsWhiteSpace(character))
                {
                    if (currentToken != null)
                    {
                        // Number ranges should have the number yielded first
                        if (currentToken.Type == TokenType.NumberRange)
                        {
                            var number = currentToken.Value[..^2];
                            yield return new Token
                            {
                                Type = TokenType.Number,
                                Value = number,
                                FileIndex = fileIndex,
                                Line = currentToken.Line,
                                Column = currentToken.Column
                            };
                            currentToken.Type = TokenType.Range;
                            currentToken.Value = "..";
                            currentToken.Column += number.Length;
                        }
                        else
                        {
                            CheckForReservedTokensAndErrors(currentToken, errors, character);
                        }

                        yield return currentToken;
                    }
                    currentToken = null;
                    continue;
                }

                // Get token from character, determine to emit value
                var tokenType = GetTokenType(character);

                if (ContinueToken(currentToken, tokenType, character, lexerStatus))
                {
                    currentToken!.Value += character;
                }
                else
                {
                    if (currentToken != null)
                    {
                        // Number ranges should have the number yielded first
                        if (currentToken.Type == TokenType.NumberRange)
                        {
                            var number = currentToken.Value[..^2];
                            yield return new Token
                            {
                                Type = TokenType.Number,
                                FileIndex = fileIndex,
                                Value = number,
                                Line = currentToken.Line,
                                Column = currentToken.Column
                            };
                            currentToken.Type = TokenType.Range;
                            currentToken.Value = "..";
                            currentToken.Column += number.Length;
                        }
                        else
                        {
                            CheckForReservedTokensAndErrors(currentToken, errors, character);
                        }

                        yield return currentToken;
                    }

                    currentToken = new Token
                    {
                        Type = tokenType,
                        Value = character.ToString(),
                        FileIndex = fileIndex,
                        Line = line,
                        Column = column
                    };
                }
            }

            if (!lexerStatus.ReadingComment && currentToken != null) yield return currentToken;
        }

        private void CheckForReservedTokensAndErrors(Token token, List<ParseError> errors, char character = default)
        {
            // Check tokens for reserved keywords
            if (token.Type == TokenType.Identifier)
            {
                if (_reservedTokens.TryGetValue(token.Value, out var type))
                {
                    token.Type = type;
                }
            }

            if (token.Error)
            {
                errors.Add(new ParseError {Error = $"Unexpected token '{token.Value + character}'", Token = token});
            }
        }

        private static TokenType GetTokenType(char character)
        {
            var token = (TokenType)character;

            if (char.IsDigit(character)) return TokenType.Number;

            return Enum.IsDefined(typeof(TokenType), token) ? token : TokenType.Identifier;
        }

        private static bool ContinueToken(Token currentToken, TokenType type, char character, LexerStatus lexerStatus)
        {
            if (currentToken == null) return false;

            switch (currentToken.Type)
            {
                case TokenType.Identifier:
                    return type == TokenType.Identifier || type == TokenType.Number;
                case TokenType.Number:
                    switch (type)
                    {
                        case TokenType.Number:
                            return true;
                        case TokenType.Period:
                            if (currentToken.Flags.HasFlag(TokenFlags.Float))
                            {
                                // Handle number ranges
                                if (currentToken.Value[^1] == '.')
                                {
                                    currentToken.Flags &= ~TokenFlags.Float;
                                    currentToken.Type = TokenType.NumberRange;
                                    return true;
                                }
                                currentToken.Error = true;
                                return false;
                            }
                            currentToken.Flags |= TokenFlags.Float;
                            return true;
                        case TokenType.Identifier:
                            if (currentToken.Value == "0" && character == 'x')
                            {
                                currentToken.Flags |= TokenFlags.HexNumber;
                                return true;
                            }
                            if (currentToken.Flags.HasFlag(TokenFlags.HexNumber))
                            {
                                return true;
                            }
                            currentToken.Error = true;
                            return false;
                        default:
                            return false;
                    }
                case TokenType.ForwardSlash:
                    switch (type)
                    {
                        case TokenType.ForwardSlash:
                            currentToken.Type = TokenType.Comment;
                            lexerStatus.ReadingComment = true;
                            return true;
                        case TokenType.Asterisk:
                            currentToken.Type = TokenType.Comment;
                            lexerStatus.ReadingComment = true;
                            lexerStatus.MultiLineComment = true;
                            return true;
                        default:
                            return false;
                    }
                case TokenType.Not:
                    if (type == TokenType.Equals)
                    {
                        currentToken.Type = TokenType.NotEqual;
                        return true;
                    }
                    return false;
                case TokenType.GreaterThan:
                    if (type == TokenType.Equals)
                    {
                        currentToken.Type = TokenType.GreaterThanEqual;
                        return true;
                    }
                    return ChangeTypeIfSame(currentToken, type, TokenType.ShiftRight);
                case TokenType.LessThan:
                    if (type == TokenType.Equals)
                    {
                        currentToken.Type = TokenType.LessThanEqual;
                        return true;
                    }
                    return ChangeTypeIfSame(currentToken, type, TokenType.ShiftLeft);
                case TokenType.Ampersand:
                    return ChangeTypeIfSame(currentToken, type, TokenType.And);
                case TokenType.Pipe:
                    return ChangeTypeIfSame(currentToken, type, TokenType.Or);
                case TokenType.Equals:
                    return ChangeTypeIfSame(currentToken, type, TokenType.Equality);
                case TokenType.Plus:
                    return ChangeTypeIfSame(currentToken, type, TokenType.Increment);
                case TokenType.Range:
                    if (type == TokenType.Period)
                    {
                        currentToken.Type = TokenType.VarArgs;
                        return true;
                    }
                    return false;
                case TokenType.Minus:
                    if (type == TokenType.Number)
                    {
                        currentToken.Type = type;
                        return true;
                    }
                    return ChangeTypeIfSame(currentToken, type, TokenType.Decrement);
                case TokenType.Period:
                    return ChangeTypeIfSame(currentToken, type, TokenType.Range);
                case TokenType.ShiftLeft:
                    if (type == TokenType.LessThan)
                    {
                        currentToken.Type = TokenType.RotateLeft;
                        return true;
                    }
                    return false;
                case TokenType.ShiftRight:
                    if (type == TokenType.GreaterThan)
                    {
                        currentToken.Type = TokenType.RotateRight;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool ChangeTypeIfSame(Token token, TokenType type, TokenType newType)
        {
            if (token.Type != type)
                return false;

            token.Type = newType;
            return true;
        }

        private class LexerStatus
        {
            public bool ReadingComment { get; set; }
            public bool MultiLineComment { get; set; }
        }
    }

    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
        public TokenFlags Flags { get; set; }
        public int FileIndex { get; init; }
        public int Line { get; init; }
        public int Column { get; set; }
        public bool Error { get; set; }
    }

    public enum TokenType
    {
        Identifier,
        Struct,
        Comment, // Ignored by parser
        Number,
        Boolean,
        Literal,
        Character,
        Return,
        If,
        Else,
        Then,
        While,
        Each,
        And,
        Or,
        Equality,
        NotEqual,
        Increment,
        Decrement,
        GreaterThanEqual,
        LessThanEqual,
        In,
        Range,
        Enum,
        Null,
        Cast,
        ShiftLeft,
        ShiftRight,
        RotateLeft,
        RotateRight,
        Operator,
        NumberRange, // Ignored by parser
        VarArgs, // Ignored by parser
        OpenParen = '(',
        CloseParen = ')',
        OpenBracket = '[',
        CloseBracket = ']',
        OpenBrace = '{',
        CloseBrace = '}',
        Not = '!',
        Ampersand = '&',
        Pipe = '|',
        Caret = '^',
        Plus = '+',
        Minus = '-',
        Asterisk = '*',
        ForwardSlash = '/',
        Percent = '%',
        Equals = '=',
        Colon = ':',
        SemiColon = ';',
        Quote = '"',
        Apostrophe = '\'',
        LessThan = '<',
        GreaterThan = '>',
        Comma = ',',
        Period = '.',
        Pound = '#'
    }

    [Flags]
    public enum TokenFlags : byte
    {
        None = 0,
        Float = 1,
        HexNumber = 2
    }
}
