namespace Lang.Parsing
{
    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
    }

    public enum TokenType
    {
        Token,
        Comment,
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
