using System.Collections.Generic;
using System.Linq;
using Lang.Parsing;
using Type = Lang.Parsing.Type;

namespace Lang.Translation
{
    public interface IProgramGraphBuilder
    {
        ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors);
    }

    public class ProgramGraphBuilder : IProgramGraphBuilder
    {
        private readonly IDictionary<string, FunctionAst> _functions = new Dictionary<string, FunctionAst>();
        private readonly List<CallAst> _undefinedCalls = new();
        private readonly ISet<string> _undefinedTypes = new HashSet<string>();

        public ProgramGraph CreateProgramGraph(ParseResult parseResult, out List<TranslationError> errors)
        {
            errors = new List<TranslationError>();
            var graph = new ProgramGraph();

            foreach (var syntaxTree in parseResult.SyntaxTrees)
            {
                switch (syntaxTree)
                {
                    case FunctionAst function:
                        if (function.Name == "Main")
                        {
                            graph.Main = function;
                            var functionErrors = StepThroughFunction(function, true);
                            if (functionErrors.Any())
                                errors.AddRange(functionErrors);
                        }
                        else
                        {
                            _functions.Add(function.Name, function);
                            var functionErrors = StepThroughFunction(function);
                            if (functionErrors.Any())
                                errors.AddRange(functionErrors);
                        }
                        break;
                    // TODO Handle more type of ASTs
                }
            }

            // TODO Verify undefined calls and types

            return graph;
        }

        private List<TranslationError> StepThroughFunction(FunctionAst function, bool main = false)
        {
            var translationErrors = new List<TranslationError>();

            if (main)
            {
                var type = function.ReturnType.InferType(out _);
                if (!(type == Type.Void || type == Type.Int))
                {
                    translationErrors.Add(new TranslationError
                    {
                        Error = "The main function should be of type 'int' or 'void'"
                    });
                }
            }

            foreach (var syntaxTree in function.Children)
            {
                switch (syntaxTree)
                {
                    case ReturnAst returnAst:
                        var error = VerifyReturnStatement(returnAst, function.ReturnType);
                        if (error != null)
                            translationErrors.Add(error);
                        break;
                    // TODO Handle more syntax trees
                }
            }

            return translationErrors;
        }

        private static TranslationError VerifyReturnStatement(ReturnAst returnAst, TypeDefinition functionReturnType)
        {
            var type = functionReturnType.InferType(out _);

            if (type == Type.Void)
            {
                return returnAst.Value == null ? null : new TranslationError {Error = "Function return should be void"};
            }

            switch (returnAst.Value)
            {
                case ConstantAst constant:
                    return constant.Type == type ? null : new TranslationError {Error = $"Expected to return type '{type}', but returned type '{constant.Type}'"};
                // TODO Implement these branches
                case CallAst call:
                    break;
                case ExpressionAst expression:
                    break;
                case null:
                    return new TranslationError {Error = $"Expected to return type: {type}"};
            }

            return null;
        }
    }
}
