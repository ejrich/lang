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
        Comment,
        OpenParen = '(',
        CloseParen = ')',
        OpenBrace = '{',
        CloseBrace = '}',
        Not = '!',
        And = '&',
        Or = '|',
        Xor = '^',
        Add = '+',
        Minus = '-',
        Multiply = '*',
        Divide = '/',
        Ampersand = '%',
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
