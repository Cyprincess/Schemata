namespace Schemata.Expressions.Cel.Tests.Conformance;

public sealed record CelSpecValue(object? Value)
{
    public static CelSpecValue Bool(bool value) { return new(value); }

    public static CelSpecValue Int(long value) { return new(value); }

    public static CelSpecValue UInt(ulong value) { return new(value); }

    public static CelSpecValue Double(double value) { return new(value); }

    public static CelSpecValue String(string value) { return new(value); }

    public static CelSpecValue Bytes(byte[] value) { return new(value); }

    public static CelSpecValue Error(string message) { return new(new CelSpecError(message)); }

    public static CelSpecValue Null() { return new((object?)null); }
}
