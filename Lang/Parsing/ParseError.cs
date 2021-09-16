namespace Lang.Parsing
{
    public class ParseError
    {
        public int FileIndex { get; set; }
        public string Error { get; init; }
        public Token Token { get; init; }
    }
}
