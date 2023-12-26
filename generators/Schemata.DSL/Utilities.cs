using System;
using System.Linq;
using System.Numerics;
using static System.StringSplitOptions;

namespace Schemata.DSL;

public static class Utilities
{
    public static Type? GetClrType(string type) {
        return type.ToLowerInvariant() switch {
            "string" or "text"                      => typeof(string),
            "integer" or "int" or "int32" or "int4" => typeof(int),
            "long" or "int64" or "int8"             => typeof(long),
            "float" or "float32" or "float4"        => typeof(float),
            "double" or "float64" or "float8"       => typeof(double),
            "decimal" or "numeric" or "number"      => typeof(decimal),
            "boolean" or "bool"                     => typeof(bool),
            "datetime" or "timestamp"               => typeof(DateTimeOffset),
            "uuid" or "guid"                        => typeof(Guid),
            "biginteger" or "bigint"                => typeof(BigInteger),
            var _                                   => null,
        };
    }

    public static string ToCamelCase(string @string) {
        return @string.Split(new[] { "_", " " }, RemoveEmptyEntries)
                      .Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1, s.Length - 1))
                      .Aggregate(string.Empty, (s1, s2) => s1 + s2);
    }
}
