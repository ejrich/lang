namespace Lang.Parsing
{
    public interface IPrimitive
    {
        byte Bytes { get; }
        bool Signed { get; }
    }

    public class IntegerType : IPrimitive
    {
        public byte Bytes { get; init; }
        public bool Signed { get; init; }
    }

    public class FloatType : IPrimitive
    {
        public byte Bytes { get; set; }
        public bool Signed => true;
    }

    public class EnumType : IPrimitive
    {
        public byte Bytes { get; init; }
        public bool Signed { get; init; }
    }
}
