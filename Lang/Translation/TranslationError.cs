namespace Lang.Translation
{
    public class TranslationError
    {
        public string File { get; set; }
        public string Error { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
