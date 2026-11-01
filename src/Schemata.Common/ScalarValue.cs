using System;
using System.Globalization;
using System.Text.Json;

namespace Schemata.Common;

public enum ScalarKind { Object, String, Char, Bool, Int64, UInt64, Double, Decimal, Guid, Timestamp, DateTimeOffset, Duration, Bytes, Enum }

public static class ScalarValue
{
    public static ScalarKind Kind(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return ScalarKind.String;
        if (type == typeof(char)) return ScalarKind.Char;
        if (type == typeof(bool)) return ScalarKind.Bool;
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long)) return ScalarKind.Int64;
        if (type == typeof(ulong)) return ScalarKind.UInt64;
        if (type == typeof(float) || type == typeof(double)) return ScalarKind.Double;
        if (type == typeof(decimal)) return ScalarKind.Decimal;
        if (type == typeof(Guid)) return ScalarKind.Guid;
        if (type == typeof(DateTime)) return ScalarKind.Timestamp;
        if (type == typeof(DateTimeOffset)) return ScalarKind.DateTimeOffset;
        if (type == typeof(TimeSpan)) return ScalarKind.Duration;
        if (type == typeof(byte[])) return ScalarKind.Bytes;
        return type.IsEnum ? ScalarKind.Enum : ScalarKind.Object;
    }

    public static string Format(object value, Func<Type, Exception> unsupported) => value switch {
        string text => text,
        bool boolean => boolean ? "true" : "false",
        byte or sbyte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        float number => ((double)number).ToString("R", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        char character => character.ToString(),
        ulong number => number.ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        Guid guid => guid.ToString("D"),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
        Enum enumeration => Enum.GetName(enumeration.GetType(), enumeration) ?? throw unsupported(enumeration.GetType()),
        _ => throw unsupported(value.GetType()),
    };

    public static object Parse(ScalarKind kind, string value) => kind switch {
        ScalarKind.String or ScalarKind.Enum => value,
        ScalarKind.Char when value.Length == 1 => value[0],
        ScalarKind.Bool => bool.Parse(value),
        ScalarKind.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
        ScalarKind.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture),
        ScalarKind.Double => double.Parse(value, CultureInfo.InvariantCulture),
        ScalarKind.Decimal => decimal.Parse(value, CultureInfo.InvariantCulture),
        ScalarKind.Guid => Guid.ParseExact(value, "D"),
        ScalarKind.Timestamp => DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ScalarKind.DateTimeOffset => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture),
        ScalarKind.Duration => TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture),
        ScalarKind.Bytes => Convert.FromBase64String(value),
        _ => throw new JsonException("A scalar leaf requires a supported scalar kind."),
    };
}
