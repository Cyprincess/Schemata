using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     JWKS endpoint per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7517.html">RFC 7517: JSON Web Key (JWK)</seealso>.
///     Publishes the public key material of the signing rows stored under the issuer:
///     valid and retired rows publish, revoked rows never do. Any symmetric row keeps
///     the whole set private and yields an empty <c>keys</c> array, per RFC 7517 §4.2.
/// </summary>
/// <exception cref="NotSupportedException">A signing row carries no publishable material or
/// cannot be imported under its declared key algorithm.</exception>
public sealed class JwksHandler(
    ISecurityStore<SchemataSecurity>       securities,
    IHttpClientFactory                     http,
    ICacheProvider                         cache,
    IOptions<SchemataSecurityOptions>      security,
    IOptions<SchemataAuthorizationOptions> options
)
{
    /// <summary>Returns the JSON Web Key Set for tokens issued by this OP.</summary>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> ExecuteAsync(CancellationToken ct = default) {
        var rows = new List<SchemataSecurity>();
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(options.Value.Issuer!), null, SecurityConstants.Usages.Signing, null, ct)) {
            if (row.Status is SecurityConstants.Statuses.Valid or SecurityConstants.Statuses.Retired) {
                rows.Add(row);
            }
        }

        if (rows.Count == 0) {
            throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key"));
        }

        var client    = http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName);
        var materials = new List<SchemataKeyMaterial>();
        foreach (var row in rows) {
            materials.Add(await row.ToKeyMaterialAsync(client, cache, security.Value.KeyCacheLifetime, ct)
                       ?? throw new NotSupportedException(
                           string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), row.Kind)));
        }

        // Symmetric keys MUST NOT appear in a public JWKS.
        // See RFC 7517 §4.2. Any symmetric key in the set keeps the whole set private.
        if (materials.Any(material => material.Material is SecurityKeyMaterial.Symmetric)) {
            return AuthorizationResult.Content(new Dictionary<string, object> {
                ["keys"] = Array.Empty<object>(),
            });
        }

        var entries = new List<JwkEntry>();
        foreach (var material in materials) {
            var jwk = SecurityKeyAdapter.ToJsonWebKeySet([material]).Keys.FirstOrDefault()
                      ?? throw new NotSupportedException(
                          string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), material.Security.Kind));

            entries.Add(new() {
                Kty = jwk.Kty,
                Use = "sig",
                Alg = SecurityKeyAdapter.ToSigningAlgorithm(material.Security.Algorithm),
                Kid = material.Security.Kid ?? jwk.Kid,
                N   = jwk.N,
                E   = jwk.E,
                Crv = jwk.Crv,
                X   = jwk.X,
                Y   = jwk.Y,
            });
        }

        return AuthorizationResult.Content(new Dictionary<string, object> {
            ["keys"] = entries,
        });
    }

    /// <summary>
    ///     Wire DTO for a single JWKS entry. <see cref="JsonPropertyNameAttribute" /> pins the
    ///     RFC 7517 member names against any configured naming policy, and the
    ///     <see cref="JsonIgnoreAttribute" /> conditions pin member presence: metadata members
    ///     always serialize (nulls included), while key-material members serialize only when
    ///     populated.
    /// </summary>
    private sealed class JwkEntry
    {
        [JsonPropertyName(JsonWebKeyParameterNames.Kty)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Kty { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Use)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Use { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Alg)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Alg { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Kid)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Kid { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.N)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? N { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.E)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? E { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Crv)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Crv { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.X)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? X { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Y)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Y { get; set; }
    }
}
