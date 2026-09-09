using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Resolves the JSON Web Key Set for a registered client application. Centralizes the
///     <c>security rows → material → JsonWebKeySet</c> chain used by both the
///     <c>private_key_jwt</c> token-endpoint authentication path and the JAR request-object
///     verifier,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7521.html#section-5">
///         RFC 7521: Assertion Framework for OAuth 2.0 Client Authentication and
///         Authorization Grants §5: Client Use of Assertions
///     </seealso>
///     .
/// </summary>
public static class ClientKeyResolver<TApp> where TApp : SchemataApplication
{
    /// <summary>
    ///     Collects every JWK / JWKS security row under the client, converts them to a
    ///     <see cref="JsonWebKeySet" />, and selects the single key matching the assertion's
    ///     <c>kid</c> header (no <c>kid</c> requires exactly one key).
    /// </summary>
    /// <exception cref="OAuthException">
    ///     <c>invalid_client</c> when no JWK row is registered or the kid does not match.
    /// </exception>
    public static async Task<IReadOnlyList<SecurityKey>> ResolveAsync(
        TApp                            application,
        string                          assertion,
        ISecurityStore<SchemataSecurity> securities,
        IHttpClientFactory              http,
        ICacheProvider                  cache,
        IOptions<SchemataSecurityOptions> security,
        CancellationToken               ct
    ) {
        var client    = http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName);
        var materials = new List<SchemataKeyMaterial>();

        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Application(application),
                           null,
                           SecurityConstants.Usages.Authentication,
                           null,
                           ct)) {
            if (row.Status is not (SecurityConstants.Statuses.Valid or SecurityConstants.Statuses.Retired)) {
                continue;
            }

            var material = await row.ToKeyMaterialAsync(client, cache, security.Value.KeyCacheLifetime, ct);
            if (material is null) {
                continue;
            }

            switch (material.Material) {
                case SecurityKeyMaterial.JwkJson or SecurityKeyMaterial.JwksJson:
                    materials.Add(material);
                    break;
                case SecurityKeyMaterial.RsaKey rsa:
                    rsa.Key.Dispose();
                    break;
                case SecurityKeyMaterial.EcKey ec:
                    ec.Key.Dispose();
                    break;
            }
        }

        if (materials.Count == 0) {
            throw new OAuthException(
                AuthorizationConstants.OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ASSERTION_KEY_NOT_FOUND));
        }

        var keyset = SecurityKeyAdapter.ToJsonWebKeySet(materials);

        var headerKid = TryKid(assertion);
        var selected = string.IsNullOrEmpty(headerKid)
            ? keyset.Keys
            : keyset.Keys.Where(key => key.Kid == headerKid).ToList();

        if (selected.Count != 1) {
            throw new OAuthException(
                AuthorizationConstants.OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ASSERTION_KEY_NOT_FOUND));
        }

        return [selected[0]];
    }

    private static string? TryKid(string assertion) {
        try {
            return new JsonWebToken(assertion).Kid;
        } catch (Exception) {
            return null;
        }
    }
}