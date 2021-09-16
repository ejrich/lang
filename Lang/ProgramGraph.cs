using System.Collections.Generic;
using Lang.Parsing;
using Lang.Translation;

namespace Lang
{
    public class ProgramGraph
    {
        public string Name { get; set; }
        public List<DeclarationAst> Variables { get; } = new();
        public Dictionary<string, IType> Types { get; } = new();
        public Dictionary<string, List<FunctionAst>> Functions { get; } = new();
        public List<string> Dependencies { get; set; }
        public List<TranslationError> Errors { get; } = new();
    }
}
