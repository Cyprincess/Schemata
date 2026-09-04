using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Authenticates clients presenting a JWT client assertion HMAC-signed with the client
///     secret (<c>client_secret_jwt</c>), per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ClientAuthentication">
///         OpenID Connect Core 1.0 §9: Client Authentication
///     </seealso>
///     .  The UTF-8 bytes of the raw secret in the client's registered <c>secret</c> security
///     row are the HMAC verification key; hashed password rows cannot verify assertions.
/// </summary>
public sealed class ClientSecretJwtAuthentication<TApp>(
    IApplicationManager<TApp>              apps,
    IOptions<SchemataAuthorizationOptions> options,
    IHttpClientFactory                     http,
    ICacheProvider                         cache,
    IOptions<SchemataSecurityOptions>      security,
    ISecurityStore<SchemataSecurity>       securities,
    ClientAssertionValidator               assertions,
    ClientAssertionChannel                 channel
) : IClientAuthentication<TApp>
    where TApp : SchemataApplication
{
    #region IClientAuthentication<TApp> Members

    public string Method => ClientAuthMethods.ClientSecretJwt;

    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (!options.Value.AllowedClientAuthMethods.Contains(ClientAuthMethods.ClientSecretJwt)) {
            return null;
        }

        if (!channel.Presents(form)) {
            return null;
        }

        var assertion = form![Parameters.ClientAssertion][0]!;
        var clientId  = channel.ResolveClientId(form, assertion);
        var app       = await channel.FindApplicationAsync(apps, clientId, ct);

        var key = await SelectKeyAsync(app, assertion, ct);
        if (key is null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ASSERTION_KEY_NOT_FOUND)
            );
        }

        var token = await assertions.ValidateAsync(
            assertion,
            clientId,
            clientId,
            channel.Audiences(options.Value),
            ClientAssertionAlgorithms.SymmetricAlgorithms,
            ct);

        await channel.VerifySignatureAsync(
            assertion,
            new() {
                IssuerSigningKey = key,
                ValidateIssuer   = false,
                ValidateAudience = false,
                ValidateLifetime = false,
            });

        await assertions.BurnJtiAsync(token, ct);

        return app;
    }

    #endregion

    // Mirrors SelectKeys in PrivateKeyJwtAuthentication: an assertion kid narrows the
    // candidates to rows carrying it; otherwise exactly one row must remain.
    private async Task<SymmetricSecurityKey?> SelectKeyAsync(TApp app, string assertion, CancellationToken ct) {
        var kid  = channel.Peek(assertion)?.Kid;
        var rows = new List<SchemataSecurity>();

        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Application(app),
                           SecurityConstants.Kinds.Secret,
                           SecurityConstants.Usages.Authentication,
                           null,
                           ct)) {
            if (row.Status is not (SecurityConstants.Statuses.Valid or SecurityConstants.Statuses.Retired)) {
                continue;
            }

            if (!string.IsNullOrEmpty(kid) && row.Kid != kid) {
                continue;
            }

            rows.Add(row);
        }

        if (rows.Count != 1) {
            return null;
        }

        var material = await rows[0].ToKeyMaterialAsync(
            http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName),
            cache,
            security.Value.KeyCacheLifetime,
            ct);

        return material?.Material is SecurityKeyMaterial.Symmetric key
            ? new SymmetricSecurityKey(key.Key)
            : null;
    }
}
