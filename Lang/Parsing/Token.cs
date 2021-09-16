namespace Lang.Parsing
{
    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
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
        Increment,
        Decrement,
        In,
        Range,
        NumberRange, // Ignored by parser
        OpenParen = '(',
        CloseParen = ')',
        OpenBrace = '{',
        CloseBrace = '}',
        Not = '!',
        Ampersand = '&',
        Pipe = '|',
        Caret = '^',
        Plus = '+',
        Minus = '-',
        Multiply = '*',
        Divide = '/',
        Percent = '%',
        Equals = '=',
        Colon = ':',
        SemiColon = ';',
        Quote = '"',
        LessThan = '<',
        GreaterThan = '>',
        Comma = ',',
        Period = '.'
    }
}
