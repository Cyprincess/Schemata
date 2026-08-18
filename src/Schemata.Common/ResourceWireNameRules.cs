using System;
using System.Linq;
using System.Linq.Expressions;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Common;

/// <summary>
///     Maps resource-specific CLR property names to their public wire names and back. Owns the
///     AIP-122 (<c>name</c>) and AIP-154 (<c>etag</c>) aliases, the plural collection field that
///     AIP-140 requires of the repeated results in AIP-132 list and AIP-231-235 batch responses,
///     and the wire-segment-to-CLR-property fallback used by AIP-161 update-mask parsing.
/// </summary>
public static class ResourceWireNameRules
{
    /// <summary>
    ///     The <seealso href="https://google.aip.dev/122">AIP-122</seealso> resource name wire field,
    ///     which every <see cref="ICanonicalName" /> type serializes its canonical name as.
    /// </summary>
    public const string Name = "name";

    /// <summary>
    ///     The <seealso href="https://google.aip.dev/154">AIP-154</seealso> entity tag wire field,
    ///     which every <see cref="IFreshness" /> type serializes its concurrency token as.
    /// </summary>
    public const string EntityTag = "etag";

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
                return Name;
            }
        }

        if (typeof(IFreshness).IsAssignableFrom(owner)
         && propertyName == nameof(IFreshness.EntityTag)) {
            return EntityTag;
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
        if (typeof(ICanonicalName).IsAssignableFrom(owner) && wireSegment == Name) {
            return nameof(ICanonicalName.CanonicalName);
        }

        if (typeof(IFreshness).IsAssignableFrom(owner) && wireSegment == EntityTag) {
            return nameof(IFreshness.EntityTag);
        }

        var carrier = owner.GetInterfaces().FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntitiesResult<>));
        if (carrier is not null && wireSegment == ResourceNameDescriptor.ForType(carrier.GetGenericArguments()[0]).Plural) {
            return nameof(IEntitiesResult<>.Entities);
        }

        return MemberAccess.Resolve(Expression.Parameter(owner), wireSegment) is MemberExpression member
                   ? member.Member.Name
                   : wireSegment.Pascalize();
    }
}
