namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }
        FunctionIR AddFunction(FunctionAst function);
        void EmitIR(FunctionIR function, IAst ast);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        public ProgramIR Program { get; } = new();

        public FunctionIR AddFunction(FunctionAst function)
        {
            var functionName = function.Name switch
            {
                "main" => "__main",
                "__start" => "main",
                _ => function.OverloadIndex > 0 ? $"{function.Name}.{function.OverloadIndex}" : function.Name
            };

            var functionIR = new FunctionIR();
            if (!function.Extern)
            {
                functionIR.Allocations = new();
                functionIR.BasicBlocks = new();
            }

            if (functionName == "main")
            {
                Program.EntryPoint = functionIR;
            }
            else
            {
                Program.Functions[functionName] = functionIR;
            }

            return functionIR;
        }

        public void EmitIR(FunctionIR function, IAst ast)
        {
        }
    }
}
