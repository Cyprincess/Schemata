using Schemata.Abstractions;
using Schemata.Common.Hash;

// ReSharper disable once CheckNamespace
namespace System;

/// <summary>
///     Extension methods for string hashing and cache key generation.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    ///     Computes the 64-bit CityHash for the string.
    /// </summary>
    /// <param name="value">The string to hash.</param>
    /// <returns>The 64-bit hash value.</returns>
    public static ulong CityHash64(this string value) { return CityHash.CityHash64(value); }

    /// <summary>
    ///     Generates a Schemata cache key by combining the framework GUID with the CityHash of the string.
    /// </summary>
    /// <param name="value">The string to generate a cache key for.</param>
    /// <returns>A unique cache key string.</returns>
    public static string ToCacheKey(this string value) {
        return string.Concat(SchemataConstants.Schemata, "\x1e", value.CityHash64());
    }
}
