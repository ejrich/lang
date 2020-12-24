namespace Lang.Parsing
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
        Boolean,
        Literal,
        Return,
        Var,
        If,
        Else,
        Then,
        While,
        Each,
        And,
        Or,
        Equality,
        Comment,
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
