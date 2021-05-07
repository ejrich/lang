using System;

namespace Lang.Parsing
{
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
        Token,
        Struct,
        Comment, // Ignored by parser
        Number,
        Boolean,
        Literal,
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
