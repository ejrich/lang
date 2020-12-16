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
            errors = new List<TranslationError>();
            var graph = new ProgramGraph();

            foreach (var parseResult in parseResults)
            {
                foreach (var syntaxTree in parseResult.SyntaxTrees)
                {
                    // TODO Interpret syntax tree and add to program graph
                }
            }

            return graph;
        }
    }
}
