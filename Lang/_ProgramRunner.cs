namespace Lang
{
    public class _ProgramRunner //: IProgramRunner
    {
        public void Init()
        {
            // How this should work
            // - When a type/function is added to the TypeTable, add the TypeInfo object to the array
            // - When function IR is built and the function is extern, create the function ref
            // - When a global variable is added, store them in the global space
        }

        public void RunProgram()
        {
        }

        public bool ExecuteCondition()
        {
            return true;
        }
    }
}
