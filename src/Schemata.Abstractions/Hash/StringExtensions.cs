using Schemata.Abstractions.Hash;

// ReSharper disable once CheckNamespace
namespace System;

public static class StringExtensions
{
    public static ulong CityHash64(this string value) {
        return CityHash.CityHash64(value);
    }
}
