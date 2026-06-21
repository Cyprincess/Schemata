using System;
using System.Linq.Expressions;
using Humanizer;
using Schemata.Common;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Resolves AIP-161 field-mask wire segments to CLR property names. Applies the resource-name
///     aliases (<c>name</c> to the canonical-name property, <c>etag</c> to the entity-tag property,
///     and the plural collection field) before falling back to the shared member resolver, so a mask
///     such as <c>name,etag</c> targets the same properties the response serializes them from.
/// </summary>
internal static class ResourceWireMask
{
    /// <summary>Maps a wire segment declared on <paramref name="owner" /> to its CLR property name.</summary>
    /// <param name="owner">The type that declares the wire field.</param>
    /// <param name="wireSegment">The wire-format mask segment.</param>
    /// <returns>The CLR property name.</returns>
    public static string Convert(Type owner, string wireSegment) {
        var alias = ResourceWireNameRules.ResolveClr(owner, wireSegment, static type => ResourceNameDescriptor.ForType(type).Plural);
        if (alias is not null) {
            return alias;
        }

        var member = MemberAccess.Resolve(Expression.Parameter(owner), wireSegment) as MemberExpression;
        return member?.Member.Name ?? wireSegment.Pascalize();
    }
}
