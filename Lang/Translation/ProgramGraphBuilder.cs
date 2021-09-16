using System.Collections.Generic;
using Lang.Parsing;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(List<ParseResult> parseResults, out List<TranslationError> errors);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        public ProgramGraph CreateProgramGraph(List<ParseResult> parseResults, out List<TranslationError> errors)
        {
            // TODO Implement me
            errors = new List<TranslationError>();
            return new ProgramGraph();
        }
    }
}
