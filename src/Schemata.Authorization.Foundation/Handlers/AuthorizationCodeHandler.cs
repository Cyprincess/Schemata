using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
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
///     Handles the <c>authorization_code</c> grant type.
///     Validates the authorization code token, runs the
///     <see cref="ITokenRequestAdvisor{TApp}" /> and <see cref="ICodeExchangeAdvisor{TApp, TToken}" />
///     pipelines, enforces PKCE, enforces scope down-scoping, and marks the code
///     as single-use when <see cref="CodeFlowOptions.RequireCodeSingleUse" /> is <c>true</c>,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.2">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.2
///     </seealso>
///     .
/// </summary>
public sealed class AuthorizationCodeHandler<TApp, TToken>(
    IClientAuthenticationService<TApp> client,
    ITokenManager<TToken>              tokens,
    IServiceProvider                   sp,
    IOptions<JsonSerializerOptions>    json,
    IOptions<CodeFlowOptions>          options
) : IGrantHandler
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IGrantHandler Members

    /// <inheritdoc cref="IGrantHandler.GrantType" />
    public string GrantType => GrantTypes.AuthorizationCode;

    /// <summary>
    ///     Exchanges an authorization code for tokens.
    ///     Authenticates the client, validates the stored code token and its payload,
    ///     enforces PKCE and scope constraints, then emits a <see cref="AuthorizationResult.SignIn" />
    ///     with claims that flow into <see cref="SchemataAuthenticationHandler{TApp, TToken}" />.
    /// </summary>
    /// <param name="request">Token request containing the authorization code.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.Code)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.Code)
            );
        }

        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId] = [request.ClientId],
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

        var token = await tokens.FindByReferenceIdAsync(request.Code, ct);
        if (string.IsNullOrWhiteSpace(token?.Payload) || string.IsNullOrWhiteSpace(token.Subject)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var payload = JsonSerializer.Deserialize<AuthorizeRequest>(token.Payload, json.Value);
        if (payload == null) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var exchange = new CodeExchangeContext<TApp, TToken> {
            Request          = request,
            Application      = application,
            CodeToken        = token,
            Payload          = payload,
            RequireSingleUse = options.Value.RequireCodeSingleUse,
        };

        switch (await Advisor.For<ICodeExchangeAdvisor<TApp, TToken>>()
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

        var granted = payload.Scope;
        if (!string.IsNullOrWhiteSpace(request.Scope)) {
            if (!ScopeParser.IsSubset(request.Scope, payload.Scope)) {
                throw new OAuthException(
                    OAuthErrors.InvalidScope,
                    SchemataResources.GetResourceString(SchemataResources.ST4006)
                );
            }

            granted = request.Scope;
        }

        // authorization codes are single-use; mark as redeemed
        // to prevent replay attacks when RequireCodeSingleUse is enabled.
        // See RFC 9700 §2.1.2.
        if (options.Value.RequireCodeSingleUse) {
            token.Status = TokenStatuses.Redeemed;
            await tokens.UpdateAsync(token, ct);
        }

        var claims = new List<Claim> {
            new(Claims.Subject, token.Subject),
            new(Claims.ClientId, application.ClientId),
        };

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        var props = new Dictionary<string, string?> {
            [Properties.GrantType]         = GrantTypes.AuthorizationCode,
            [Properties.Scope]             = granted,
            [Properties.Nonce]             = payload.Nonce,
            [Properties.SessionId]         = token.SessionId,
            [Properties.AuthorizationName] = token.AuthorizationName,
            [Properties.MaxAge]            = payload.MaxAge,
            [Properties.AuthTime]          = payload.AuthTime,
        };
        return AuthorizationResult.SignIn(identity, props);
    }

    #endregion
}
