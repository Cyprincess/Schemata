using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>refresh_token</c> grant type.
///     Validates the refresh token via JWT signature verification (skipping
///     lifetime checks), runs the <see cref="ITokenRequestAdvisor{TApp}" />
///     and <see cref="IRefreshTokenAdvisor{TApp, TToken}" /> pipelines,
///     validates subject existence, enforces optional refresh token rotation,
///     and re-issues tokens with the stored scope,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.3
///     </seealso>
///     .
/// </summary>
public sealed class RefreshTokenHandler<TApp, TToken>(
    IClientAuthenticationService<TApp> client,
    ITokenManager<TToken>              tokens,
    TokenService                       issuer,
    IOptions<RefreshTokenFlowOptions>  options,
    IServiceProvider                   sp
) : IGrantHandler
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IGrantHandler Members

    /// <inheritdoc cref="IGrantHandler.GrantType" />
    public string GrantType => GrantTypes.RefreshToken;

    /// <summary>
    ///     Exchanges a refresh token for new tokens.
    ///     Validates the token payload (with lifetime validation disabled so
    ///     expired refresh tokens can still be inspected), checks subject
    ///     existence via <see cref="ISubjectProvider" />, and optionally
    ///     rotates the refresh token when <see cref="RefreshTokenFlowOptions.RequireRefreshTokenRotation" />
    ///     is enabled,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
    ///         RFC 9700: The OAuth 2.0 Authorization
    ///         Framework: Best Current Practice §2.1.3
    ///     </seealso>
    ///     .
    /// </summary>
    /// <param name="request">Token request containing the refresh token.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.RefreshToken)
            );
        }

        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
        }

        var ctx = new AdviceContext(sp);

        switch (await Advisor.For<ITokenRequestAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.ST4001)
                );
        }

        var token = await tokens.FindByReferenceIdAsync(request.RefreshToken, ct);
        if (string.IsNullOrWhiteSpace(token?.Payload)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        // Validate without lifetime enforcement so expired refresh tokens
        // can still be inspected for subject and scope extraction.
        var principal = await issuer.Validate(token.Payload, lifetime: false);
        if (principal is null) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var exchange = new RefreshTokenContext<TApp, TToken> {
            Request     = request,
            Application = application,
            Token       = token,
            Principal   = principal,
        };

        switch (await Advisor.For<IRefreshTokenAdvisor<TApp, TToken>>()
                             .RunAsync(ctx, exchange, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ST4008)
                );
        }

        var scope = principal.FindFirstValue(Claims.Scope);

        if (!string.IsNullOrWhiteSpace(request.Scope)) {
            if (!ScopeParser.IsSubset(request.Scope, scope)) {
                throw new OAuthException(
                    OAuthErrors.InvalidScope,
                    SchemataResources.GetResourceString(SchemataResources.ST4006)
                );
            }
        }

        // Reject if the subject referenced by the refresh token no longer exists
        // (e.g. de-provisioned account).  The ISubjectProvider is resolved
        // via DI and is optional — when absent, the check is skipped.
        if (!string.IsNullOrWhiteSpace(token.Subject)) {
            var provider = sp.GetService<ISubjectProvider>();
            if (provider is not null && !await provider.ValidateAsync(token.Subject, ct)) {
                throw new OAuthException(
                    OAuthErrors.InvalidGrant,
                    SchemataResources.GetResourceString(SchemataResources.ST4004)
                );
            }
        }

        if (options.Value.RequireRefreshTokenRotation) {
            await tokens.RevokeAsync(token, ct);
        }

        var claims = new List<Claim> {
            new(Claims.ClientId, application.ClientId),
        };

        if (!string.IsNullOrWhiteSpace(token.Subject)) {
            claims.Add(new(Claims.Subject, token.Subject));
        }

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType]         = GrantTypes.RefreshToken,
            [Properties.Scope]             = request.Scope,
            [Properties.AuthorizationName] = token.AuthorizationName,
            [Properties.SessionId]         = token.SessionId,
        });
    }

    #endregion
}
