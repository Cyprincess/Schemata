using System;
using System.Collections.Generic;
using System.Linq;

namespace Schemata.Entity.Repository.Conversions;

/// <summary>
///     Shared JSON-column type detection for repository provider bridges.
/// </summary>
public static class JsonColumnTypes
{
    private static readonly HashSet<Type> Scalars = [
        typeof(string),
        typeof(bool),
        typeof(char),
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
    ];

    /// <summary>
    ///     Returns whether <paramref name="type" /> is a supported scalar JSON value type.
    ///     Enums qualify as scalars: their underlying integral value round-trips through JSON.
    /// </summary>
    public static bool IsScalar(Type type) {
        return Scalars.Contains(type) || type.IsEnum;
    }

    /// <summary>
    ///     Returns whether <paramref name="type" /> is a supported dictionary or collection
    ///     shape for a provider-managed JSON column.
    /// </summary>
    public static bool IsSupported(Type type) {
        if (type == typeof(string)) {
            return false;
        }

        if (IsSupportedDictionary(type)) {
            return true;
        }

        return TryGetCollectionElement(type) is { } element
            && element != typeof(byte)
            && IsScalarOrNullableScalar(element);
    }

    private static bool IsSupportedDictionary(Type type) {
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Dictionary<,>)) {
            return false;
        }

        var arguments = type.GetGenericArguments();
        var key       = arguments[0];
        var value     = arguments[1];

        return Nullable.GetUnderlyingType(key) is null
            && IsScalar(key)
            && IsScalarOrNullableScalar(value);
    }

    private static bool IsScalarOrNullableScalar(Type type) {
        return IsScalar(Nullable.GetUnderlyingType(type) ?? type);
    }

    private static Type? TryGetCollectionElement(Type type) {
        if (type.IsArray) {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>)) {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
                   .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>))
                   .Select(i => i.GetGenericArguments()[0])
                   .FirstOrDefault();
    }
}
