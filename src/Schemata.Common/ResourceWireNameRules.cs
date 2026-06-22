using System;
using System.Linq;
using System.Linq.Expressions;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common;

/// <summary>
///     Maps resource-specific CLR property names to their public wire names and back. Owns the
///     AIP-122 (<c>name</c>), AIP-154 (<c>etag</c>), and AIP-132/231-235 (collection plural)
///     aliases plus the wire-segment-to-CLR-property fallback used by AIP-157 / AIP-161 mask
///     parsing.
/// </summary>
public static class ResourceWireNameRules
{
    /// <summary>
    ///     Resolves the public wire field name for a CLR property declared (or inherited) on
    ///     <paramref name="owner" />.
    /// </summary>
    /// <param name="owner">The type that declares the property.</param>
    /// <param name="propertyName">The CLR property name.</param>
    /// <returns>The wire field name, or <see langword="null" /> when the property is suppressed.</returns>
    public static string? ResolveWireName(Type owner, string propertyName) {
        if (propertyName == nameof(IEntitiesResult<>.Entities)) {
            var carrier = owner.GetInterfaces().FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntitiesResult<>));
            if (carrier is not null) {
                return ResourceNameDescriptor.ForType(carrier.GetGenericArguments()[0]).Plural;
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
    ///     Resolves a wire-format mask segment to the CLR property it serializes from. Applies the
    ///     resource-name aliases first (<c>name</c>, <c>etag</c>, collection plural) and otherwise
    ///     falls back to a Pascal-cased member lookup so a mask such as <c>name,etag,first_name</c>
    ///     targets <c>CanonicalName</c>, <c>EntityTag</c>, and <c>FirstName</c>.
    /// </summary>
    /// <param name="owner">The type that declares the wire field.</param>
    /// <param name="wireSegment">The wire-format mask segment.</param>
    /// <returns>The CLR property name; never <see langword="null" />.</returns>
    public static string ResolveClrName(Type owner, string wireSegment) {
        if (typeof(ICanonicalName).IsAssignableFrom(owner) && wireSegment == Parameters.Name) {
            return nameof(ICanonicalName.CanonicalName);
        }

        if (typeof(IFreshness).IsAssignableFrom(owner) && wireSegment == Parameters.EntityTag) {
            return nameof(IFreshness.EntityTag);
        }

        var carrier = owner.GetInterfaces().FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntitiesResult<>));
        if (carrier is not null && wireSegment == ResourceNameDescriptor.ForType(carrier.GetGenericArguments()[0]).Plural) {
            return nameof(IEntitiesResult<>.Entities);
        }

        var member = MemberAccess.Resolve(Expression.Parameter(owner), wireSegment) as MemberExpression;
        return member?.Member.Name ?? wireSegment.Pascalize();
    }
}
