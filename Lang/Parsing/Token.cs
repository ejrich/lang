﻿namespace Lang.Parsing
{
    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
        public int Line { get; init; }
        public int Column { get; init; }
        public bool Error { get; set; }
    }

    public enum TokenType
    {
        Token,
        Number,
        Comment,
        Literal,
        OpenParen = '(',
        CloseParen = ')',
        OpenBrace = '{',
        CloseBrace = '}',
        Not = '!',
        And = '&',
        Or = '|',
        Add = '+',
        Minus = '-',
        Multiply = '*',
        Divide = '/',
        Equals = '=',
        Colon = ':',
        SemiColon = ';',
        Quote = '"',
        LessThan = '<',
        GreaterThan = '>',
        Comma = ',',
        Period = '.',
    }
}
