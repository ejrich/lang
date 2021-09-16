using System.Collections.Generic;
using Lang.Parsing;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(List<ParseResult> parseResults, out List<TranslationError> error);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        public ProgramGraph CreateProgramGraph(List<ParseResult> parseResults, out List<TranslationError> error)
        {
            throw new System.NotImplementedException();
        }
    }
}
