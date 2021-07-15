namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }

    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        public ProgramIR Program { get; } = new();

    }
}
