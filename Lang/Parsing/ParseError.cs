namespace Lang.Parsing
{
    public class ParseError
    {
        public string File { get; set; }
        public string Error { get; init; }
        public Token Token { get; init; }
    }
}
