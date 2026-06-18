using System;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Transport.Grpc.Proto;

/// <summary>
///     Schemata trait -> protobuf wire-name resolver shared by every code-first
///     gRPC surface (Resource, Flow, ...). Mirrors the JSON wire-name rewrites
///     performed by <c>Schemata.Transport.Http.SchemataJsonTraits</c>:
///     <list type="bullet">
///         <item><c>ICanonicalName.Name</c> is suppressed (it is a pattern, not a wire field).</item>
///         <item><c>ICanonicalName.CanonicalName</c> is surfaced as <c>"name"</c> (AIP-122).</item>
///         <item><c>IFreshness.EntityTag</c> is surfaced as <c>"etag"</c> (AIP-154).</item>
///         <item>
///             The <c>Entities</c> property of <see cref="IEntitiesResult{TItem}" />
///             implementors is surfaced as
///             <see cref="ResourceNameDescriptor.Plural" /> of <c>TItem</c>
///             (AIP-132 / AIP-231..235).
///         </item>
///     </list>
///     Callers are still responsible for applying the global snake_case wire convention
///     (e.g. via <c>InflectorExtensions.Underscore</c>) on top of the resolved trait name.
/// </summary>
public static class SchemataProtoTraits
{
    /// <summary>
    ///     Resolves the trait-aware logical wire name for <paramref name="propertyName" />
    ///     declared (or inherited) on <paramref name="owner" />.
    /// </summary>
    /// <param name="owner">The entity / DTO type owning the property.</param>
    /// <param name="propertyName">The CLR property name.</param>
    /// <returns>
    ///     The logical wire name to use, or <see langword="null" /> when the property must
    ///     be omitted from the wire entirely.
    /// </returns>
    public static string? ResolveWireName(Type owner, string propertyName) {
        return ResourceWireNameRules.Resolve(owner, propertyName, static type => ResourceNameDescriptor.ForType(type).Plural);
    }
}
