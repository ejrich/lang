namespace Lang.Parsing
{
    public class Token
    {
        public TokenType Type { get; init; }
        public string Value { get; set; }
    }

    public enum TokenType
    {
        Token,
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
        Period = '.'
    }
}
