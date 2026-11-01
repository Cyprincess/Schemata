using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Client authentication orchestrator. Determines the mechanism the request actually
///     presented — a Basic header, a POSTed secret, a jwt-bearer client assertion, identification
///     by <c>client_id</c> alone, or a custom authenticator's own presentation — and delegates to
///     that single registered authenticator; mechanisms are never inferred by running every
///     authenticator and counting successes. Valueless fields are normalized away per RFC 6749
///     §3.2 before detection, so an empty <c>client_secret</c> never counts as a mechanism.
///     Genuinely multiple presented credentials are rejected — <c>invalid_client</c> when a client
///     assertion is among them (RFC 7521 §4.2.1), <c>invalid_request</c> otherwise — and the
///     registered <c>token_endpoint_auth_method</c> (defaulting to <c>client_secret_basic</c>,
///     the OIDC Dynamic Client Registration default, when absent) pins every client to one
///     channel on both DCR and legacy rows.
/// </summary>
public sealed class ClientAuthenticationService<TApp>(
    IEnumerable<IClientAuthentication<TApp>> authenticators,
    ClientAssertionChannel                  assertions,
    IApplicationManager<TApp>               apps
) : IClientAuthenticationService<TApp>
    where TApp : SchemataApplication
{
    private static readonly HashSet<string> StandardMethods = new(StringComparer.Ordinal) {
        ClientAuthMethods.ClientSecretBasic,
        ClientAuthMethods.ClientSecretPost,
        ClientAuthMethods.None,
        ClientAuthMethods.PrivateKeyJwt,
        ClientAuthMethods.ClientSecretJwt,
    };

    private readonly Dictionary<string, IClientAuthentication<TApp>> _authenticators =
        authenticators.ToDictionary(a => a.Method, StringComparer.Ordinal);

    private readonly List<IClientAuthentication<TApp>> _custom =
        authenticators.Where(a => !StandardMethods.Contains(a.Method)).ToList();

    private readonly ClientAssertionChannel    _assertions = assertions;
    private readonly IApplicationManager<TApp> _apps       = apps;

    #region IClientAuthenticationService<TApp> Members

    public async Task<ClientAuthenticationResult<TApp>?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct,
        string? endpointAudience = null
    ) {
        var basic      = TryReadBasic(headers, out var basicId, out var basicSecret);
        var posted     = HasValue(query, Parameters.ClientSecret) || HasValue(form, Parameters.ClientSecret);
        var assertion  = _assertions.Presents(form) || _assertions.Presents(query);

        // Custom methods participate in the same presentation-selection contract: their probe
        // runs before any authentication, so a custom credential counts in multiple-mechanism
        // detection and a client_id-only request never diverts a custom credential from its
        // authenticator.
        var customPresented = _custom.Where(a => a.Presents(query, form, headers)).ToList();

        var identified = HasValue(query, Parameters.ClientId)
                      || HasValue(form, Parameters.ClientId)
                      || (basic && !string.IsNullOrWhiteSpace(basicId));

        var credentials = new[] { basic, posted, assertion }.Count(presented => presented) + customPresented.Count;
        if (credentials > 1) {
            // RFC 7521 §4.2.1 keeps the assertion-specific invalid_client category for mixed
            // client-assertion requests; RFC 6749 §3.2.1's generic multiple-method case is
            // invalid_request.
            if (assertion) {
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS, code: (int)HttpStatusCode.Unauthorized);
            }

            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.MULTIPLE_CLIENT_AUTH_METHODS);
        }

        IClientAuthentication<TApp> authenticator;
        var                          verified = false;
        TApp?                        asserted = null;

        if (basic) {
            authenticator = Require(ClientAuthMethods.ClientSecretBasic);
            // A Basic header without a secret identifies the client only: ClientSecretValidator
            // accepts that for public clients without verifying anything.
            verified = !string.IsNullOrWhiteSpace(basicSecret);
        } else if (posted) {
            authenticator = Require(ClientAuthMethods.ClientSecretPost);
            verified      = true;
        } else if (assertion) {
            // The assertion identifies the client; the effective registered method decides which
            // jwt-bearer validator runs — no guessing from implementation presence.
            asserted    = await ResolveAssertionTargetAsync(form ?? query, ct);
            authenticator = Require(EffectiveMethod(asserted));
            verified    = true;
        } else if (customPresented.Count == 1) {
            authenticator = customPresented[0];
            verified      = authenticator.VerifiesCredential;
        } else if (identified) {
            authenticator = Require(ClientAuthMethods.None);
        } else {
            // RFC 6749 §5.2: no client identity at all is a failed client authentication.
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.NOT_EMPTY,
                new Dictionary<string, string?> { ["value"] = Parameters.ClientId },
                (int)HttpStatusCode.Unauthorized
            );
        }

        // The pre-resolved assertion target is only a routing hint: the chosen authenticator
        // always runs and performs the signature, issuer/subject, and replay validation itself.
        var app = await authenticator.AuthenticateAsync(query, form, headers, ct, endpointAudience);

        // The chosen authenticator not claiming its own presented mechanism is a failed
        // authentication, never a silent fallback to another mechanism.
        if (app is null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.INVALID_CLIENT_CREDENTIALS,
                code: (int)HttpStatusCode.Unauthorized
            );
        }

        if (EffectiveMethod(app) != authenticator.Method) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.UNAUTHORIZED_CLIENT_AUTH_METHOD, code: (int)HttpStatusCode.Unauthorized);
        }

        return new() {
            Application   = app,
            Method        = authenticator.Method,
            Authenticated = verified && authenticator.Method != ClientAuthMethods.None
                && !(app.ApplicationType == ApplicationTypes.Native
                    && authenticator.Method is ClientAuthMethods.ClientSecretBasic or ClientAuthMethods.ClientSecretPost or ClientAuthMethods.ClientSecretJwt),
        };
    }

    #endregion

    /// <summary>
    ///     The single effective-method rule for every storage path: the registered
    ///     <c>token_endpoint_auth_method</c>, or the OIDC Dynamic Client Registration default
    ///     (<c>client_secret_basic</c>) when the row carries none — a null method never bypasses
    ///     the constraint.
    /// </summary>
    internal static string EffectiveMethod(TApp app) {
        return app.TokenEndpointAuthMethod ?? ClientAuthMethods.ClientSecretBasic;
    }

    private async Task<TApp> ResolveAssertionTargetAsync(
        Dictionary<string, List<string?>>? form,
        CancellationToken                  ct
    ) {
        var assertion = FirstValue(form, Parameters.ClientAssertion)
                        ?? throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        var clientId = _assertions.ResolveClientId(form, assertion);
        var app      = await _apps.FindByClientIdAsync(clientId, ct);
        if (app is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS, code: (int)HttpStatusCode.Unauthorized);
        }

        var effective = EffectiveMethod(app);
        if (effective != ClientAuthMethods.PrivateKeyJwt && effective != ClientAuthMethods.ClientSecretJwt) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.UNAUTHORIZED_CLIENT_AUTH_METHOD, code: (int)HttpStatusCode.Unauthorized);
        }

        return app;
    }

    private IClientAuthentication<TApp> Require(string method) {
        return _authenticators.TryGetValue(method, out var authenticator)
            ? authenticator
            : throw new OAuthException(
                  OAuthErrors.InvalidClient,
                  SchemataResources.UNAUTHORIZED_CLIENT_AUTH_METHOD,
                  code: (int)HttpStatusCode.Unauthorized
              );
    }

    private static bool TryReadBasic(Dictionary<string, List<string?>>? headers, out string? id, out string? secret) {
        id = secret = null;
        if (headers is null || !headers.TryGetValue(nameof(Authorization), out var values)) {
            return false;
        }

        var header = values.FirstOrDefault(v => v?.StartsWith(Schemes.Basic + " ", StringComparison.OrdinalIgnoreCase) == true);
        if (header is null) {
            return false;
        }

        try {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[(Schemes.Basic + " ").Length..].Trim()));
            var colon   = decoded.IndexOf(':');
            if (colon < 0) {
                id = WebUtility.UrlDecode(decoded);
                return true;
            }

            id     = WebUtility.UrlDecode(decoded[..colon]);
            secret = WebUtility.UrlDecode(decoded[(colon + 1)..]);
            return true;
        } catch (FormatException) {
            return true;
        }
    }

    private static bool HasValue(Dictionary<string, List<string?>>? input, string field) {
        return input is not null
            && input.TryGetValue(field, out var values)
            && values.Count > 0
            && !string.IsNullOrWhiteSpace(values[0]);
    }

    private static string? FirstValue(Dictionary<string, List<string?>>? input, string field) {
        return input is not null && input.TryGetValue(field, out var values) && values.Count > 0
            ? values[0]
            : null;
    }
}
