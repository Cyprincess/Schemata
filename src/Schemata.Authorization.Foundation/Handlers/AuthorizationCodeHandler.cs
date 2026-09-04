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
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>authorization_code</c> grant type.
///     Validates the authorization code token, runs the
///     <see cref="ITokenRequestAdvisor{TApp}" /> and <see cref="ICodeExchangeAdvisor{TApp}" />
///     pipelines, enforces PKCE, enforces scope down-scoping, enforces RFC 8707 §2.2 resource
///     consistency, and marks the code
///     as single-use when <see cref="CodeFlowOptions.RequireCodeSingleUse" /> is <c>true</c>,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.2">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice Section 2.1.2
///     </seealso>
///     .
/// </summary>
public sealed class AuthorizationCodeHandler<TApp>(
    IClientAuthenticationService<TApp> client,
    ITokenStore<SchemataToken>                tokens,
    IOptions<JsonSerializerOptions>    json,
    IOptions<CodeFlowOptions>          options
) : IGrantHandler
    where TApp : SchemataApplication
{
    #region IGrantHandler Members

    public string GrantType => GrantTypes.AuthorizationCode;

    /// <summary>
    ///     Exchanges an authorization code for tokens.
    ///     Authenticates the client, validates the stored code token and its payload,
    ///     enforces PKCE and scope constraints, then emits a <see cref="AuthorizationResult.SignIn" />
    ///     with claims that flow into <see cref="SchemataAuthenticationHandler{TApp}" />.
    /// </summary>
    /// <param name="request">Token request containing the authorization code.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.Code)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.Code)
            );
        }

        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId] = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
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
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
                );
        }

        var token = await tokens.FindByReferenceIdAsync(request.Code, ct);
        if (string.IsNullOrWhiteSpace(token?.Payload) || string.IsNullOrWhiteSpace(token.Parent)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        var clear = token.Payload;

        var wrapper = JsonSerializer.Deserialize<AuthorizationCodePayload>(clear, json.Value);
        var payload = wrapper?.Request;
        if (payload is null) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        // RFC 8707 §2.2: the resources requested on the code exchange must equal the set granted
        // at the authorization endpoint (set semantics, order-insensitive); this server applies
        // the strictest discretionary reading and requires full equality, while an omitted
        // parameter adopts the granted set. Anything else is invalid_target.
        var grantedResources   = payload.Resource ?? [];
        var requestedResources = request.Resource;
        if (requestedResources is { Count: > 0 } && !ResourcesEqual(requestedResources, grantedResources)) {
            throw new OAuthException(
                OAuthErrors.InvalidTarget,
                SchemataResources.GetResourceString(SchemataResources.INVALID_TARGET)
            );
        }

        var resources = requestedResources is { Count: > 0 } ? requestedResources : grantedResources;
        if (resources.Count > 0) {
            ctx.Set(new ResourceIndicators([.. resources]));
        }

        var exchange = new CodeExchangeContext<TApp> {
            Request          = request,
            Application      = application,
            CodeToken        = token,
            Payload          = payload,
            RequireSingleUse = options.Value.RequireCodeSingleUse,
        };

        switch (await Advisor.For<ICodeExchangeAdvisor<TApp>>()
                             .RunAsync(ctx, exchange, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ACCESS_DENIED)
                );
        }

        var granted = payload.Scope;
        if (!string.IsNullOrWhiteSpace(request.Scope)) {
            if (!ScopeParser.IsSubset(request.Scope, payload.Scope)) {
                throw new OAuthException(
                    OAuthErrors.InvalidScope,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_SCOPE)
                );
            }

            granted = request.Scope;
        }

        // Read from the exchange so ICodeExchangeAdvisor can toggle the policy per request.
        if (exchange.RequireSingleUse && !await tokens.TryRedeemAsync(token, ct)) {
            // A lost redemption means the code was already consumed — a replay: revoke
            // every token derived from the same authorization grant before rejecting,
            // per RFC 6749 §4.1.2.
            if (!string.IsNullOrWhiteSpace(token.Authorization)) {
                await tokens.RevokeByAuthorizationAsync(token.Authorization, ct);
            }

            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, token.Parent),
            new(Claims.ClientId, application.ClientId),
        };

        // The context approved with the code rides again: stamped here, the claims advisor
        // re-tags it for both token destinations at claim assembly.
        if (wrapper?.Context is { } approved) {
            claims.Stamp(approved);
        }

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        var props = new Dictionary<string, string?> {
            [Properties.GrantType]         = GrantTypes.AuthorizationCode,
            [Properties.Resources]         = resources.Count > 0 ? string.Join(" ", resources) : null,
            [Properties.Scope]             = granted,
            [Properties.Nonce]             = payload.Nonce,
            [Properties.SessionId]         = token.SessionId,
            [Properties.AuthorizationName] = token.Authorization,
            [Properties.MaxAge]            = payload.MaxAge,
        };

        props[Properties.AuthorizationDetails] = payload.AuthorizationDetails;
        return AuthorizationResult.SignIn(identity, props);
    }

    /// <summary>Set-semantics comparison: same elements, any order.</summary>
    private static bool ResourcesEqual(ICollection<string> requested, ICollection<string> granted) {
        return new HashSet<string>(requested, StringComparer.Ordinal).SetEquals(granted);
    }

    #endregion
}
