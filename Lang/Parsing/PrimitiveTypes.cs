namespace Lang.Parsing
{
    public interface IPrimitive
    {
        ushort Bytes { get; }
        public bool Signed { get; }
    }

    public class IntegerType : IPrimitive
    {
        public ushort Bytes { get; init; }
        public bool Signed { get; init; }
    }

    public class FloatType : IPrimitive
    {
        public ushort Bytes { get; init; }
        public bool Signed => true;
    }
}
