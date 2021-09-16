using System.Collections.Generic;
using Lang.Parsing;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        private readonly IDictionary<string, FunctionAst> _functions = new Dictionary<string, FunctionAst>();
        
        public ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors)
        {
            errors = new List<TranslationError>();
            var graph = new ProgramGraph();

            foreach (var syntaxTree in parseResult.SyntaxTrees)
            {
                // TODO Interpret syntax tree and add to program graph
                switch (syntaxTree)
                {
                    case FunctionAst function:
                        if (function.Name == "Main")
                        {
                            graph.Main = function;
                        }
                        else
                        {
                            _functions.Add(function.Name, function);
                        }
                        break;
                }
            }

            return graph;
        }
    }
}
