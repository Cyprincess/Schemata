using System;
using System.Collections.Generic;
using System.Linq;
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
///     Authenticates clients presenting a JWT client assertion signed with a key from the
///     client's registered key material (<c>private_key_jwt</c>), per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ClientAuthentication">
///         OpenID Connect Core 1.0 §9: Client Authentication
///     </seealso>
///     .  Keys come from the client's <c>jwks</c> / <c>jwks_uri</c> security rows, selected by
///     the assertion <c>kid</c> header (RFC 7517 §4.5); without a <c>kid</c> the key set must
///     hold exactly one key.  When the client registers <c>token_endpoint_auth_signing_alg</c>,
///     the algorithm allow-list narrows to that single value.
/// </summary>
public sealed class PrivateKeyJwtAuthentication<TApp>(
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

    public string Method => ClientAuthMethods.PrivateKeyJwt;

    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (!options.Value.AllowedClientAuthMethods.Contains(ClientAuthMethods.PrivateKeyJwt)) {
            return null;
        }

        if (!channel.Presents(form)) {
            return null;
        }

        var assertion = form![Parameters.ClientAssertion][0]!;
        var clientId  = channel.ResolveClientId(form, assertion);
        var app       = await channel.FindApplicationAsync(apps, clientId, ct);

        var keys = await ClientKeyResolver<TApp>.ResolveAsync(app, assertion, securities, http, cache, security, ct);

        var token = await assertions.ValidateAsync(
            assertion,
            clientId,
            clientId,
            channel.Audiences(options.Value),
            AllowedAlgorithms(app),
            ct);

        await channel.VerifySignatureAsync(
            assertion,
            new() {
                IssuerSigningKeys = keys,
                ValidateIssuer    = false,
                ValidateAudience  = false,
                ValidateLifetime  = false,
            });

        await assertions.BurnJtiAsync(token, ct);

        return app;
    }

    #endregion


    private ISet<string> AllowedAlgorithms(TApp app) {
        return string.IsNullOrWhiteSpace(app.TokenEndpointAuthSigningAlg)
            ? ClientAssertionAlgorithms.AsymmetricAlgorithms
            : new(StringComparer.Ordinal) { app.TokenEndpointAuthSigningAlg };
    }

}
