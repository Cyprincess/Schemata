using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

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
///     (e.g. via <c>SchemataNaming.ToWireName</c>) on top of the resolved trait name.
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
        // The plural rename must win over the trait branches below for the Entities
        // property of result envelopes, so it is evaluated first.
        if (propertyName == nameof(IEntitiesResult<>.Entities)) {
            var carrier = Array.Find(owner.GetInterfaces(), static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntitiesResult<>));
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
}
