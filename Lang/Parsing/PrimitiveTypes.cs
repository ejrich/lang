namespace Lang.Parsing
{
    public interface IPrimitive
    {
        ushort Bytes { get; init; }
    }

    public class IntegerType : IPrimitive
    {
        public ushort Bytes { get; init; }
        public bool Signed { get; init; }
    }

    public class FloatType : IPrimitive
    {
        public ushort Bytes { get; init; }
        public bool Double => Bytes == 64;
    }
}
