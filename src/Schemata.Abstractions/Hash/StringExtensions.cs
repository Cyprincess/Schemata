using Schemata.Abstractions;
using Schemata.Abstractions.Hash;

// ReSharper disable once CheckNamespace
namespace System;

public static class StringExtensions
{
    public static ulong CityHash64(this string value) {
        return CityHash.CityHash64(value);
    }

    public static string ToCacheKey(this string value) {
        return string.Concat(SchemataConstants.Schemata, "\x1e", value.CityHash64());
    }
}
