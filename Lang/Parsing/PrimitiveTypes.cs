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
        public ushort Bytes { get; set; }
        public bool Signed => true;
    }

    public class EnumType : IPrimitive
    {
        public ushort Bytes => 4;
        public bool Signed => true;
    }
}
