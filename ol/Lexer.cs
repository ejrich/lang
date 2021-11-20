using System;
using System.Collections.Generic;
using System.IO;

namespace ol;

public static class Lexer
{
    private static readonly IDictionary<char, char> _escapableCharacters = new Dictionary<char, char>
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

    private static readonly IDictionary<string, TokenType> _reservedTokens = new Dictionary<string, TokenType>
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
        {"union", TokenType.Union},
        {"interface", TokenType.Interface},
        {"null", TokenType.Null},
        {"cast", TokenType.Cast},
        {"operator", TokenType.Operator},
        {"break", TokenType.Break},
        {"continue", TokenType.Continue}
    };

    public static List<Token> _LoadFileTokens(string filePath, int fileIndex)
    {
        var fileText = File.ReadAllText(filePath);

        var tokens = new List<Token>();
        uint line = 1, column = 0;

        for (var i = 0; i < fileText.Length; i++)
        {
            var character = fileText[i];
            column++;

            if (character == '\n')
            {
                line++;
                column = 0;
            }
            else if (!IsWhiteSpace(character))
            {
                // Handle possible comments
                if (character == '/')
                {
                    character = fileText[i+1];
                    if (character == '/')
                    {
                        i++;
                        line++;
                        column = 0;
                        while (fileText[++i] != '\n');
                    }
                    else if (character == '*')
                    {
                        i += 2;
                        column += 2;
                        while (true)
                        {
                            character = fileText[i++];
                            if (character == '*')
                            {
                                character = fileText[i];
                                if (character == '/')
                                {
                                    column++;
                                    break;
                                }
                                else if (character == '\n')
                                {
                                    line++;
                                    column = 1;
                                }
                                else
                                {
                                    column++;
                                }
                                i++;
                            }
                            else if (character == '\n')
                            {
                                line++;
                                column = 1;
                            }
                            else
                            {
                                column++;
                            }
                        }
                    }
                    else
                    {
                        var token = new Token
                        {
                            Type = TokenType.ForwardSlash,
                            Value = "/", // TODO Can this be null?
                            FileIndex = fileIndex,
                            Line = line,
                            Column = column
                        };
                        tokens.Add(token);
                    }
                }
                // Handle literals
                else if (character == '"')
                {
                    var literalEscapeToken = false;
                    var token = new Token
                    {
                        Type = TokenType.Literal,
                        Value = "",
                        FileIndex = fileIndex,
                        Line = line,
                        Column = column
                    };

                    // TODO Maybe use substring to reduce allocations?
                    while (true)
                    {
                        character = fileText[++i];
                        column++;

                        if (character == '\\' && !literalEscapeToken)
                        {
                            literalEscapeToken = true;
                        }
                        else if (character == '\n')
                        {
                            line++;
                            column = 0;
                            if (literalEscapeToken)
                            {
                                token.Error = true;
                                literalEscapeToken = false;
                            }
                        }
                        else if (literalEscapeToken)
                        {
                            if (_escapableCharacters.TryGetValue(character, out var escapedCharacter))
                            {
                                token.Value += escapedCharacter;
                            }
                            else
                            {
                                token.Error = true;
                                token.Value += character;
                            }
                            literalEscapeToken = false;
                        }
                        else
                        {
                            if (character == '"')
                            {
                                if (token.Error)
                                {
                                    ErrorReporter.Report($"Unexpected token '{token.Value}'", token);
                                }

                                tokens.Add(token);
                                break;
                            }
                            else
                            {
                                token.Value += character;
                            }
                        }
                    }
                }
                // Handle characters
                else if (character == '\'')
                {
                    character = fileText[++i];
                    if (character == '\\')
                    {
                        character = fileText[++i];
                        if (_escapableCharacters.TryGetValue(character, out var escapedCharacter))
                        {
                            var token = new Token
                            {
                                Type = TokenType.Character,
                                Character = escapedCharacter,
                                FileIndex = fileIndex,
                                Line = line,
                                Column = column
                            };
                            tokens.Add(token);
                        }
                        else
                        {
                            ErrorReporter.Report($"Unknown escaped character '\\{character}'", fileIndex, line, column);
                        }
                        column += 2;
                    }
                    else
                    {
                        column++;
                        var token = new Token
                        {
                            Type = TokenType.Character,
                            Character = character,
                            FileIndex = fileIndex,
                            Line = line,
                            Column = column
                        };
                        tokens.Add(token);
                    }

                    character = fileText[i+1];
                    if (character == '\'')
                    {
                        i++;
                        column++;
                    }
                    else
                    {
                        ErrorReporter.Report("Expected a single digit character", fileIndex, line, column);
                    }
                }
                // Handle numbers
                else if (char.IsDigit(character))
                {
                    var startIndex = i;
                    var token = new Token
                    {
                        Type = TokenType.Number,
                        FileIndex = fileIndex,
                        Line = line,
                        Column = column
                    };

                    while (true)
                    {
                        character = fileText[i+1];

                        if (!char.IsDigit(character))
                        {
                            if (character == '.')
                            {
                                // TODO Implement me
                            }
                            else if (character == 'x')
                            {
                                // TODO Implement me
                            }
                            else if (character == '\n')
                            {
                                line++;
                                column = 0;
                                break;
                            }
                            else
                            {
                                break;
                            }
                        }

                        i++;
                        column++;
                    }

                    token.Value = fileText.Substring(startIndex, i - startIndex + 1);
                    tokens.Add(token);
                }
                // Handle other characters
                else
                {
                }
            }
        }

        return tokens;
    }

    public static List<Token> LoadFileTokens(string filePath, int fileIndex)
    {
        var fileText = File.ReadAllText(filePath);

        Token currentToken = null;
        var closingMultiLineComment = false;
        var literalEscapeToken = false;
        uint line = 1, column = 1;

        var lexerStatus = new LexerStatus();
        var tokens = new List<Token>();

        for (var i = 0; i < fileText.Length; i++)
        {
            var character = fileText[i];
            column++;

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
                    line++;
                    column = 0;
                    lexerStatus.ReadingComment = false;
                    currentToken = null;
                }
            }
            else if (currentToken == null)
            {
                if (character == '\n')
                {
                    line++;
                    column = 0;
                }
                else if (!IsWhiteSpace(character))
                {
                    // Get token from character, determine to emit value
                    var tokenType = GetTokenType(character);

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
            else
            {
                var type = currentToken.Type;

                // Interpret string literals
                if (type == TokenType.Quote)
                {
                    currentToken.Type = type = TokenType.Literal;
                    currentToken.Value = string.Empty;
                }

                if (character == '\n')
                {
                    line++;
                    column = 0;
                }

                // Add characters to the string literal
                if (type == TokenType.Literal)
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
                                ErrorReporter.Report($"Unexpected token '{currentToken.Value}'", currentToken);
                            }

                            tokens.Add(currentToken);
                            currentToken = null;
                        }
                        else
                        {
                            currentToken.Value += character;
                        }
                    }
                }
                // Interpret character literals
                else if (type == TokenType.Apostrophe)
                {
                    currentToken.Type = TokenType.Character;
                    if (character == '\\')
                    {
                        character = fileText[++i];
                        if (_escapableCharacters.TryGetValue(character, out var escapedCharacter))
                        {
                            currentToken.Value = $"{escapedCharacter}";
                        }
                        else
                        {
                            ErrorReporter.Report($"Unknown escaped character '\\{character}'", currentToken);
                        }
                    }
                    else
                    {
                        currentToken.Value = $"{character}";
                    }
                }
                // Finish character literals
                else if (type == TokenType.Character)
                {
                    tokens.Add(currentToken);
                    if (character != '\'')
                    {
                        ErrorReporter.Report("Expected a single digit character", currentToken);
                    }
                    else
                    {
                        currentToken = null;
                    }
                }
                // Skip over whitespace and emit the current token if not null
                else if (IsWhiteSpace(character))
                {
                    AddToken(currentToken, tokens, fileIndex, character);
                    currentToken = null;
                }
                else
                {
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
                            AddToken(currentToken, tokens, fileIndex, character);
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
            }
        }

        if (!lexerStatus.ReadingComment && currentToken != null) tokens.Add(currentToken);

        return tokens;
    }

    private static void AddToken(Token currentToken, List<Token> tokens, int fileIndex, char character)
    {
        // Number ranges should have the number yielded first
        if (currentToken.Type == TokenType.NumberRange)
        {
            var number = currentToken.Value[..^2];
            tokens.Add(new Token
            {
                Type = TokenType.Number,
                Value = number,
                FileIndex = fileIndex,
                Line = currentToken.Line,
                Column = currentToken.Column
            });
            currentToken.Type = TokenType.Range;
            currentToken.Value = "..";
            currentToken.Column += (uint)number.Length;
        }
        else
        {
            // Check tokens for reserved keywords
            if (currentToken.Type == TokenType.Identifier && _reservedTokens.TryGetValue(currentToken.Value, out var type))
            {
                currentToken.Type = type;
            }

            if (currentToken.Error)
            {
                ErrorReporter.Report($"Unexpected token '{currentToken.Value + character}'", currentToken);
            }
        }

        tokens.Add(currentToken);
    }

    private static bool IsWhiteSpace(char character)
    {
        return character switch
        {
            ' ' => true,
            '\n' => true,
            '\r' => true,
            '\t' => true,
            _ => false
        };
    }

    private static TokenType GetTokenType(char character)
    {
        if (char.IsDigit(character)) return TokenType.Number;

        return character switch
        {
            '(' => TokenType.OpenParen,
            ')' => TokenType.CloseParen,
            '[' => TokenType.OpenBracket,
            ']' => TokenType.CloseBracket,
            '{' => TokenType.OpenBrace,
            '}' => TokenType.CloseBrace,
            '!' => TokenType.Not,
            '&' => TokenType.Ampersand,
            '|' => TokenType.Pipe,
            '^' => TokenType.Caret,
            '+' => TokenType.Plus,
            '-' => TokenType.Minus,
            '*' => TokenType.Asterisk,
            '/' => TokenType.ForwardSlash,
            '%' => TokenType.Percent,
            '=' => TokenType.Equals,
            ':' => TokenType.Colon,
            ';' => TokenType.SemiColon,
            '"' => TokenType.Quote,
            '\'' => TokenType.Apostrophe,
            '<' => TokenType.LessThan,
            '>' => TokenType.GreaterThan,
            ',' => TokenType.Comma,
            '.' => TokenType.Period,
            '#' => TokenType.Pound,
            _ => TokenType.Identifier
        };
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
    public char Character { get; set; }
    public TokenFlags Flags { get; set; }
    public int FileIndex { get; init; }
    public uint Line { get; init; }
    public uint Column { get; set; }
    public bool Error { get; set; }
}

public enum TokenType
{
    Identifier,
    Number,
    Boolean,
    Literal,
    Character,
    Struct,
    Enum,
    Union,
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
    Null,
    Cast,
    ShiftLeft,
    ShiftRight,
    RotateLeft,
    RotateRight,
    Operator,
    Break,
    Continue,
    VarArgs,
    OpenParen = '(',    // 40
    CloseParen = ')',   // 41
    OpenBracket = '[',  // 91
    CloseBracket = ']', // 93
    OpenBrace = '{',    // 123
    CloseBrace = '}',   // 125
    Not = '!',          // 33
    Ampersand = '&',    // 38
    Pipe = '|',         // 124
    Caret = '^',        // 94
    Plus = '+',         // 43
    Minus = '-',        // 45
    Asterisk = '*',     // 42
    ForwardSlash = '/', // 47
    Percent = '%',      // 37
    Equals = '=',       // 61
    Colon = ':',        // 58
    SemiColon = ';',    // 59
    Quote = '"',        // 34
    Apostrophe = '\'',  // 39
    LessThan = '<',     // 60
    GreaterThan = '>',  // 62
    Comma = ',',        // 44
    Period = '.',       // 46
    Pound = '#',        // 35
    Interface = 256,
    // Ignored by parser
    Comment,
    NumberRange
}

[Flags]
public enum TokenFlags : byte
{
    None = 0,
    Float = 1,
    HexNumber = 2
}
