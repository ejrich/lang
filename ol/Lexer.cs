using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ol;

public static class Lexer
{
    private static readonly Dictionary<char, string> _escapableCharacters = new()
    {
        {'"', "\""},
        {'\\', "\\"},
        {'a', "\a"},
        {'b', "\b"},
        {'f', "\f"},
        {'n', "\n"},
        {'r', "\r"},
        {'t', "\t"},
        {'v', "\v"},
        {'0', "\0"}
    };

    private static readonly ConcurrentDictionary<char, String> _characterCache = new();

    private static readonly Dictionary<String, TokenType> _reservedTokens = new()
    {
        {"return", TokenType.Return},
        {"true", TokenType.Boolean},
        {"false", TokenType.Boolean},
        {"if", TokenType.If},
        {"else", TokenType.Else},
        {"while", TokenType.While},
        {"each", TokenType.Each},
        {"in", TokenType.In},
        {"out", TokenType.Out},
        {"struct", TokenType.Struct},
        {"enum", TokenType.Enum},
        {"union", TokenType.Union},
        {"interface", TokenType.Interface},
        {"null", TokenType.Null},
        {"cast", TokenType.Cast},
        {"operator", TokenType.Operator},
        {"break", TokenType.Break},
        {"continue", TokenType.Continue},
        {"asm", TokenType.Asm},
        {"switch", TokenType.Switch},
        {"case", TokenType.Case},
        {"default", TokenType.Default},
    };

    public static List<Token> LoadFileTokens(string filePath, int fileIndex)
    {
        var file = File.ReadAllText(filePath);
        Allocator.ReserveString(file);
        var fileText = file.AsSpan();

        var tokens = new List<Token>(fileText.Length / 4);
        uint line = 1, column = 0;

        for (var i = 0; i < fileText.Length; i++)
        {
            var character = fileText[i];
            column++;

            Token token;
            switch (character)
            {
                case '\n':
                    line++;
                    column = 0;
                    break;
                case ' ':
                case '\r':
                case '\t':
                    break;
                // Handle possible comments
                case '/':
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
                        while (i < fileText.Length)
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
                                if (character == '\n')
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
                        token = new Token
                        {
                            Type = TokenType.ForwardSlash,
                            Value = "/",
                            Line = line,
                            Column = column
                        };
                        tokens.Add(token);
                    }
                    break;
                }
                // Handle literals
                case '"':
                {
                    var literalEscapeToken = false;
                    var completed = false;
                    var error = false;
                    var hasEscapeTokens = false;
                    var startIndex = i + 1;
                    StringBuilder stringBuilder = null;
                    token = new Token
                    {
                        Type = TokenType.Literal,
                        Line = line,
                        Column = column
                    };

                    while (i < fileText.Length - 1)
                    {
                        character = fileText[++i];
                        column++;

                        if (character == '\\' && !literalEscapeToken)
                        {
                            hasEscapeTokens = true;
                            literalEscapeToken = true;
                        }
                        else if (character == '\n')
                        {
                            line++;
                            column = 0;
                            if (literalEscapeToken)
                            {
                                error = true;
                                literalEscapeToken = false;
                            }
                        }
                        else if (literalEscapeToken)
                        {
                            stringBuilder ??= new();
                            stringBuilder.Append(fileText.Slice(startIndex, i - startIndex - 1));
                            if (_escapableCharacters.TryGetValue(character, out var escapedCharacter))
                            {
                                stringBuilder.Append(escapedCharacter.AsSpan());
                            }
                            else
                            {
                                error = true;
                            }
                            startIndex = i + 1;
                            literalEscapeToken = false;
                        }
                        else if (character == '"')
                        {
                            if (hasEscapeTokens)
                            {
                                stringBuilder.Append(fileText.Slice(startIndex, i - startIndex));
                                token.Value = Allocator.ReserveString(stringBuilder);
                            }
                            else
                            {
                                token.Value = fileText.Slice(startIndex, i - startIndex);
                            }
                            completed = true;
                            if (error)
                            {
                                ErrorReporter.Report($"Unexpected token '{token.Value}'", fileIndex, token);
                            }

                            tokens.Add(token);
                            break;
                        }
                    }
                    if (!completed)
                    {
                        ErrorReporter.Report("Expected to close string literal with \"", fileIndex, token);
                    }
                    break;
                }
                // Handle characters
                case '\'':
                {
                    character = fileText[++i];
                    if (character == '\\')
                    {
                        character = fileText[++i];
                        if (_escapableCharacters.TryGetValue(character, out var escapedCharacter))
                        {
                            token = new Token
                            {
                                Type = TokenType.Character,
                                Value = escapedCharacter,
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
                        token = new Token
                        {
                            Type = TokenType.Character,
                            Line = line,
                            Column = column
                        };

                        if (!_characterCache.TryGetValue(character, out token.Value))
                        {
                            token.Value = _characterCache[character] = fileText.Slice(i, 1);
                        }

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
                    break;
                }
                // Handle ranges and varargs
                case '.':
                {
                    token = new Token {Line = line, Column = column};

                    if (fileText[i+1] == '.')
                    {
                        if (fileText[i+2] == '.')
                        {
                            token.Type = TokenType.VarArgs;
                            token.Value = "...";
                            i += 2;
                            column += 2;
                        }
                        else
                        {
                            token.Type = TokenType.Range;
                            token.Value = "..";
                            i++;
                            column++;
                        }
                    }
                    else
                    {
                        token.Type = TokenType.Period;
                        token.Value = ".";
                    }

                    tokens.Add(token);
                    break;
                }
                case '!':
                {
                    token = new Token {Line = line, Column = column};

                    if (fileText[i+1] == '=')
                    {
                        token.Type = TokenType.NotEqual;
                        token.Value = "!=";
                        i++;
                        column++;
                    }
                    else
                    {
                        token.Type = TokenType.Not;
                        token.Value = "!";
                    }

                    tokens.Add(token);
                    break;
                }
                case '&':
                {
                    token = new Token {Line = line, Column = column};

                    if (fileText[i+1] == '&')
                    {
                        token.Type = TokenType.And;
                        token.Value = "&&";
                        i++;
                        column++;
                    }
                    else
                    {
                        token.Type = TokenType.Ampersand;
                        token.Value = "&";
                    }

                    tokens.Add(token);
                    break;
                }
                case '|':
                {
                    token = new Token {Line = line, Column = column};

                    if (fileText[i+1] == '|')
                    {
                        token.Type = TokenType.Or;
                        token.Value = "||";
                        i++;
                        column++;
                    }
                    else
                    {
                        token.Type = TokenType.Pipe;
                        token.Value = "|";
                    }

                    tokens.Add(token);
                    break;
                }
                case '+':
                {
                    token = new Token {Line = line, Column = column};

                    if (fileText[i+1] == '+')
                    {
                        token.Type = TokenType.Increment;
                        token.Value = "++";
                        i++;
                        column++;
                    }
                    else
                    {
                        token.Type = TokenType.Plus;
                        token.Value = "+";
                    }

                    tokens.Add(token);
                    break;
                }
                case '-':
                {
                    token = new Token {Line = line, Column = column};

                    character = fileText[i+1];
                    if (character == '-')
                    {
                        token.Type = TokenType.Decrement;
                        token.Value = "--";
                        i++;
                        column++;
                    }
                    else if (char.IsDigit(character))
                    {
                        token.Type = TokenType.Number;
                        var startIndex = i++;
                        var offset = 1;

                        while (true)
                        {
                            character = fileText[i+1];

                            if (!char.IsDigit(character))
                            {
                                if (character == '.')
                                {
                                    if (token.Flags.Has(TokenFlags.Float) || fileText[i+2] == '.')
                                    {
                                        break;
                                    }
                                    token.Flags |= TokenFlags.Float;
                                }
                                else
                                {
                                    if (character == '\n')
                                    {
                                        i++;
                                        line++;
                                        column = 0;
                                        offset = 0;
                                    }
                                    else if (character == ' ' || character == '\r' || character == '\t')
                                    {
                                        i++;
                                        column++;
                                        offset = 0;
                                    }
                                    break;
                                }
                            }

                            i++;
                            column++;
                        }

                        token.Value = fileText.Slice(startIndex, i - startIndex + offset);
                    }
                    else
                    {
                        token.Type = TokenType.Minus;
                        token.Value = "-";
                    }

                    tokens.Add(token);
                    break;
                }
                case '=':
                {
                    token = new Token {Line = line, Column = column};

                    if (fileText[i+1] == '=')
                    {
                        token.Type = TokenType.Equality;
                        token.Value = "==";
                        i++;
                        column++;
                    }
                    else
                    {
                        token.Type = TokenType.Equals;
                        token.Value = "=";
                    }

                    tokens.Add(token);
                    break;
                }
                case '<':
                {
                    token = new Token {Line = line, Column = column};

                    character = fileText[i+1];
                    if (character == '=')
                    {
                        token.Type = TokenType.LessThanEqual;
                        token.Value = "<=";
                        i++;
                        column++;
                    }
                    else if (character == '<')
                    {
                        if (fileText[i+2] == '<')
                        {
                            token.Type = TokenType.RotateLeft;
                            token.Value = "<<<";
                            i += 2;
                            column += 2;
                        }
                        else
                        {
                            token.Type = TokenType.ShiftLeft;
                            token.Value = "<<";
                            i++;
                            column++;
                        }
                    }
                    else
                    {
                        token.Type = TokenType.LessThan;
                        token.Value = "<";
                    }

                    tokens.Add(token);
                    break;
                }
                case '>':
                {
                    token = new Token {Line = line, Column = column};

                    character = fileText[i+1];
                    if (character == '=')
                    {
                        token.Type = TokenType.GreaterThanEqual;
                        token.Value = ">=";
                        i++;
                        column++;
                    }
                    else if (character == '>')
                    {
                        if (fileText[i+2] == '>')
                        {
                            token.Type = TokenType.RotateRight;
                            token.Value = ">>>";
                            i += 2;
                            column += 2;
                        }
                        else
                        {
                            token.Type = TokenType.ShiftRight;
                            token.Value = ">>";
                            i++;
                            column++;
                        }
                    }
                    else
                    {
                        token.Type = TokenType.GreaterThan;
                        token.Value = ">";
                    }

                    tokens.Add(token);
                    break;
                }
                case '(':
                {
                    token = new Token
                    {
                        Type = TokenType.OpenParen,
                        Value = "(",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case ')':
                {
                    token = new Token
                    {
                        Type = TokenType.CloseParen,
                        Value = ")",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '[':
                {
                    token = new Token
                    {
                        Type = TokenType.OpenBracket,
                        Value = "[",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case ']':
                {
                    token = new Token
                    {
                        Type = TokenType.CloseBracket,
                        Value = "]",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '{':
                {
                    token = new Token
                    {
                        Type = TokenType.OpenBrace,
                        Value = "{",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '}':
                {
                    token = new Token
                    {
                        Type = TokenType.CloseBrace,
                        Value = "}",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '^':
                {
                    token = new Token
                    {
                        Type = TokenType.Caret,
                        Value = "^",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '*':
                {
                    token = new Token
                    {
                        Type = TokenType.Asterisk,
                        Value = "*",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '%':
                {
                    token = new Token
                    {
                        Type = TokenType.Percent,
                        Value = "%",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case ':':
                {
                    token = new Token
                    {
                        Type = TokenType.Colon,
                        Value = ":",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case ';':
                {
                    token = new Token
                    {
                        Type = TokenType.SemiColon,
                        Value = ";",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case ',':
                {
                    token = new Token
                    {
                        Type = TokenType.Comma,
                        Value = ",",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                case '#':
                {
                    token = new Token
                    {
                        Type = TokenType.Pound,
                        Value = "#",
                        Line = line,
                        Column = column
                    };
                    tokens.Add(token);
                    break;
                }
                // Handle numbers
                case >= '0' and <= '9':
                {
                    var startIndex = i;
                    var offset = 1;
                    token = new Token
                    {
                        Type = TokenType.Number,
                        Line = line,
                        Column = column
                    };

                    while (true)
                    {
                        var nextCharacter = fileText[i+1];

                        if (!char.IsDigit(nextCharacter))
                        {
                            if (nextCharacter == '.')
                            {
                                if (token.Flags.Has(TokenFlags.Float) || fileText[i+2] == '.')
                                {
                                    break;
                                }
                                else
                                {
                                    token.Flags |= TokenFlags.Float;
                                }
                            }
                            else if (nextCharacter == 'x')
                            {
                                if (i == startIndex && character == '0')
                                {
                                    token.Flags |= TokenFlags.HexNumber;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            else if (nextCharacter == '\n')
                            {
                                i++;
                                line++;
                                column = 0;
                                offset = 0;
                                break;
                            }
                            else if (nextCharacter == ' ' || nextCharacter == '\r' || nextCharacter == '\t')
                            {
                                i++;
                                column++;
                                offset = 0;
                                break;
                            }
                            else if (!token.Flags.Has(TokenFlags.HexNumber) || !IsHexLetter(nextCharacter))
                            {
                                break;
                            }
                        }

                        i++;
                        column++;
                    }

                    token.Value = fileText.Slice(startIndex, i - startIndex + offset);
                    tokens.Add(token);
                    break;
                }
                // Handle other characters
                default:
                {
                    var startIndex = i;
                    var offset = 1;
                    token = new Token {Line = line, Column = column};

                    while (true)
                    {
                        character = fileText[i+1];
                        if (character == '\n')
                        {
                            i++;
                            line++;
                            column = 0;
                            offset = 0;
                            break;
                        }
                        if (character == ' ' || character == '\r' || character == '\t')
                        {
                            i++;
                            column++;
                            offset = 0;
                            break;
                        }
                        if (IsNotIdentifierCharacter(character))
                        {
                            break;
                        }

                        i++;
                        column++;
                    }

                    token.Value = fileText.Slice(startIndex, i - startIndex + offset);

                    if (_reservedTokens.TryGetValue(token.Value, out var type))
                    {
                        token.Type = type;
                    }

                    tokens.Add(token);
                    break;
                }
            }
        }

        return tokens;
    }

    private static bool IsHexLetter(char character)
    {
        return (character >= 'A' && character <= 'F') || (character >= 'a' && character <= 'f');
    }

    private static bool IsNotIdentifierCharacter(char character)
    {
        return character switch
        {
            '(' => true,
            ')' => true,
            '[' => true,
            ']' => true,
            '{' => true,
            '}' => true,
            '!' => true,
            '&' => true,
            '|' => true,
            '^' => true,
            '+' => true,
            '-' => true,
            '*' => true,
            '/' => true,
            '%' => true,
            '=' => true,
            ':' => true,
            ';' => true,
            '"' => true,
            '\'' => true,
            '<' => true,
            '>' => true,
            ',' => true,
            '.' => true,
            '#' => true,
            _ => false
        };
    }
}

public struct Token
{
    public TokenType Type;
    public String Value;
    public TokenFlags Flags;
    public uint Line;
    public uint Column;
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
    Continue,           // 32
    Not = '!',          // 33
    Pound = '#',        // 35
    Percent = '%',      // 37
    Ampersand = '&',    // 38
    OpenParen = '(',    // 40
    CloseParen = ')',   // 41
    Asterisk = '*',     // 42
    Plus = '+',         // 43
    Comma = ',',        // 44
    Minus = '-',        // 45
    Period = '.',       // 46
    ForwardSlash = '/', // 47
    Colon = ':',        // 58
    SemiColon = ';',    // 59
    LessThan = '<',     // 60
    Equals = '=',       // 61
    GreaterThan = '>',  // 62
    OpenBracket = '[',  // 91
    CloseBracket = ']', // 93
    Caret = '^',        // 94
    OpenBrace = '{',    // 123
    Pipe = '|',         // 124
    CloseBrace = '}',   // 125
    VarArgs = 256,
    Interface,
    Out,
    Asm,
    Switch,
    Case,
    Default
}

[Flags]
public enum TokenFlags : byte
{
    None = 0,
    Float = 1,
    HexNumber = 2
}
