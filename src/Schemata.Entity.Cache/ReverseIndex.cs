using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Schemata.Abstractions;
using Schemata.Entity.Repository;

namespace Schemata.Entity.Cache;

/// <summary>
///     Reverse index mapping <c>(entity type, primary key)</c> to the set of cache keys holding results for
///     that entity, enabling immediate eviction on update and remove.
/// </summary>
public static class ReverseIndex
{
    /// <summary>
    ///     Builds the reverse-index key. Returns <see langword="null" /> if no key properties can be resolved.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="entity">The entity instance whose primary key is used.</param>
    /// <returns>The reverse-index key, or <see langword="null" />.</returns>
    public static string? BuildKey(Type entityType, object entity) {
        var properties = RepositoryBase.KeyPropertiesCache(entityType);
        if (properties.Count == 0) {
            return null;
        }

        var pk = FormatKey(properties, entity);
        if (pk is null) {
            return null;
        }

        return $"{entityType.FullName}\x1e{pk}".ToCacheKey(SchemataConstants.Keys.Entity);
    }

    private static string? FormatKey(IReadOnlyList<PropertyInfo> properties, object entity) {
        if (properties.Count == 1) {
            return FormatValue(properties[0].GetValue(entity));
        }

        var parts = new string[properties.Count];
        for (var i = 0; i < properties.Count; i++) {
            var value = properties[i].GetValue(entity);
            if (value is null) {
                return null;
            }

            parts[i] = FormatValue(value) ?? string.Empty;
        }

        // \x1f (ASCII Unit Separator) separates multi-column composite key values.
        return string.Join("\x1f", parts);
    }
    private static string? FormatValue(object? value) {
        return value switch {
            null              => null,
            IFormattable f    => f.ToString(null, CultureInfo.InvariantCulture),
            var _             => value.ToString(),
        };
    }
}
