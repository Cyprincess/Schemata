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
    ///     Generates a Schemata cache key by combining the framework GUID, domain, and the CityHash of the string.
    /// </summary>
    /// <param name="key">The string to generate a cache key for.</param>
    /// <param name="domain">The cache domain.</param>
    /// <returns>A unique cache key string.</returns>
    public static string ToCacheKey(this string key, string domain) {
        return string.Concat(SchemataConstants.Schemata, "\x1e", domain, "\x1e", CityHash.CityHash128(key));
    }
}
