namespace Lang.Translation
{
    public class TranslationError
    {
        public int FileIndex { get; init; }
        public string Error { get; init; }
        public int Line { get; init; }
        public int Column { get; init; }
    }
}
