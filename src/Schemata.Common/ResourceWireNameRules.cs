using System;
using System.Linq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common;

/// <summary>
///     Maps resource-specific CLR property names to their public wire names and back.
/// </summary>
public static class ResourceWireNameRules
{
    /// <summary>
    ///     Resolves the wire field name for a resource property.
    /// </summary>
    /// <param name="owner">The type that declares the property.</param>
    /// <param name="propertyName">The CLR property name.</param>
    /// <param name="pluralName">Resolves the plural collection name for a list element type.</param>
    /// <returns>The wire field name, or <see langword="null" /> when the property is suppressed.</returns>
    public static string? Resolve(Type owner, string propertyName, Func<Type, string> pluralName) {
        if (propertyName == nameof(IEntitiesResult<>.Entities)) {
            var carrier = owner.GetInterfaces().FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntitiesResult<>));
            if (carrier is not null) {
                return pluralName(carrier.GetGenericArguments()[0]);
            }
        }

        if (typeof(ICanonicalName).IsAssignableFrom(owner)) {
            if (propertyName == nameof(ICanonicalName.Name)) {
                return null;
            }

            if (propertyName == nameof(ICanonicalName.CanonicalName)) {
                return Parameters.Name;
            }
        }

        if (typeof(IFreshness).IsAssignableFrom(owner)
         && propertyName == nameof(IFreshness.EntityTag)) {
            return Parameters.EntityTag;
        }

        return propertyName;
    }

    /// <summary>
    ///     Resolves a wire field name back to the CLR property it serializes from, inverting
    ///     <see cref="Resolve" /> for the AIP-122 <c>name</c> and AIP-154 <c>etag</c> aliases and the
    ///     plural collection field. Returns <see langword="null" /> when no resource-specific alias
    ///     applies so the caller can fall back to its default name conversion.
    /// </summary>
    /// <param name="owner">The type that declares the wire field.</param>
    /// <param name="wireName">The wire field name to resolve.</param>
    /// <param name="pluralName">Resolves the plural collection name for a list element type.</param>
    /// <returns>The CLR property name, or <see langword="null" /> when no alias applies.</returns>
    public static string? ResolveClr(Type owner, string wireName, Func<Type, string> pluralName) {
        if (typeof(ICanonicalName).IsAssignableFrom(owner) && wireName == Parameters.Name) {
            return nameof(ICanonicalName.CanonicalName);
        }

        if (typeof(IFreshness).IsAssignableFrom(owner) && wireName == Parameters.EntityTag) {
            return nameof(IFreshness.EntityTag);
        }

        var carrier = owner.GetInterfaces().FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntitiesResult<>));
        if (carrier is not null && wireName == pluralName(carrier.GetGenericArguments()[0])) {
            return nameof(IEntitiesResult<>.Entities);
        }

        return null;
    }
}
