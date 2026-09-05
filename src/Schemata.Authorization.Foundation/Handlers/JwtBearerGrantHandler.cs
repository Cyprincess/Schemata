using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>urn:ietf:params:oauth:grant-type:jwt-bearer</c> grant type: a JWT minted by a
///     trusted third-party issuer is exchanged for an access token whose subject is the assertion
///     subject, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-3.1">
///         RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0 Client
///         Authentication and Authorization Grants §3.1: Authorization Grant Processing
///     </seealso>
///     . The <see cref="SchemataAuthorizationOptions.JwtBearerTrustedIssuers" /> table is the trust
///     anchor: each assertion issuer must have an entry supplying its verification key.
///     This grant is a compatibility layer for third-party-issued assertions; first-party
///     delegation uses the RFC 8693 token exchange.
/// </summary>
public sealed class JwtBearerGrantHandler<TApp>(
    IClientAuthenticationService<TApp>     client,
    ClientAssertionValidator               assertions,
    ClientAssertionChannel                 channel,
    IOptions<SchemataAuthorizationOptions> options
) : IGrantHandler
    where TApp : SchemataApplication
{
    private static readonly JsonWebTokenHandler Tokens = new();

    #region IGrantHandler Members

    public string GrantType => GrantTypes.JwtBearer;

    /// <summary>
    ///     Validates the assertion against the trusted-issuer table and issues an access token
    ///     for the assertion subject. No refresh token is issued, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7521.html#section-4.1">
    ///         RFC 7521: Assertion Framework for OAuth 2.0 Client Authentication
    ///         and Authorization Grants §4.1: Using Assertions as Authorization Grants
    ///     </seealso>
    ///     .
    /// </summary>
    /// <param name="request">Token request carrying the <c>assertion</c> grant.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.Assertion)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.Assertion)
            );
        }

        // RFC 7523 §3.1: client credentials are optional for this grant, but when present they
        // MUST be validated — this server requires an authenticated client and stamps its
        // identifier into the access token.
        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }

        var presented = channel.Peek(request.Assertion!);
        if (presented?.Issuer is not { Length: > 0 } issuer
         || !options.Value.JwtBearerTrustedIssuers.TryGetValue(issuer, out var key)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ASSERTION_ISSUER_UNTRUSTED)
            );
        }

        // RFC 7521 §4.1.1 and RFC 7523 §3.1 require invalid_grant for a rejected
        // authorization-grant assertion. The validator also serves the client-authentication
        // channels and reports invalid_client, so its error code is translated here.
        try {
            var token = await assertions.ValidateAsync(
                request.Assertion!,
                presented.Subject ?? string.Empty,
                issuer,
                channel.Audiences(options.Value),
                ClientAssertionAlgorithms.AsymmetricAlgorithms,
                ct);

            // The validator owns structure, claims, and algorithm; the signature remains for
            // the trust-table key to answer. Burning the jti before that answer would let a
            // forged assertion poison the replay cache.
            var verified = await Tokens.ValidateTokenAsync(request.Assertion!, new() {
                ValidIssuer         = issuer,
                IssuerSigningKey    = key,
                ValidateAudience    = false,
                ValidateLifetime    = false,
                RequireSignedTokens = true,
            });
            if (!verified.IsValid) {
                throw new OAuthException(
                    OAuthErrors.InvalidGrant,
                    SchemataResources.GetResourceString(SchemataResources.ASSERTION_SIGNATURE_INVALID));
            }

            await assertions.BurnJtiAsync(token, ct);
        } catch (OAuthException ex) when (ex.Status == OAuthErrors.InvalidClient) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS));
        }

        var ctx = AdviceContext.Require();

        switch (await Advisor.For<ITokenRequestAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidGrant,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
                );
        }

        var subject = presented.Subject;
        if (string.IsNullOrWhiteSpace(subject)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        var claims = new List<Claim> {
            new(Claims.ClientId, application.ClientId),
            new(IdentityClaims.Subject, subject),
        };

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType] = GrantTypes.JwtBearer,
            [Properties.Scope]     = request.Scope,
            [Properties.Resources] = request.Resource is { Count: > 0 } ? string.Join(" ", request.Resource) : null,
        });
    }

    #endregion
}
