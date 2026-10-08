using System;
using System.Linq;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Shared metadata predicates and canonicalization for <see cref="SchemataApplication" />,
///     consumed by <see cref="SchemataApplicationManager{TApplication,TAuthorization}" />, the dynamic client
///     registration pipeline, and test fakes so all of them judge grant, response-type, and scope
///     admission identically.
/// </summary>
internal static class SchemataApplicationMetadata
{
    /// <summary>
    ///     Canonicalizes a space-delimited <c>response_type</c> value by trimming, splitting, and
    ///     sorting its components ordinally, so <c>"code id_token"</c> and
    ///     <c>"id_token code"</c> compare equal.
    /// </summary>
    public static string CanonicalizeResponseType(string value) {
        var components = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Array.Sort(components, StringComparer.Ordinal);
        return string.Join(' ', components);
    }

    /// <summary>Whether the application lists <paramref name="grantType" /> in its <c>grant_types</c>.</summary>
    public static bool HasGrantType(SchemataApplication application, string grantType) {
        return application.GrantTypes?.Contains(grantType) == true;
    }

    /// <summary>
    ///     Whether the application lists <paramref name="responseType" /> in its
    ///     <c>response_types</c>, comparing both sides in canonical component order.
    /// </summary>
    public static bool HasResponseType(SchemataApplication application, string responseType) {
        var types = application.ResponseTypes;
        if (types is null || types.Count == 0) {
            return false;
        }

        var canonical = CanonicalizeResponseType(responseType);
        return types.Select(CanonicalizeResponseType).Contains(canonical);
    }

    /// <summary>Whether the application lists <paramref name="scope" /> in its <c>scope</c> string.</summary>
    public static bool HasScope(SchemataApplication application, string scope) {
        return application.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Contains(scope) == true;
    }
}
