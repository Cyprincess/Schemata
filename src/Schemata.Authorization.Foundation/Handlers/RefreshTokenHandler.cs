using System;
using System.Collections.Generic;
using System.Linq;
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
using Schemata.Authorization.Foundation.Commands;
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
///     Handles the <c>refresh_token</c> grant type.
///     Validates the refresh token via JWT signature verification (skipping
///     lifetime checks), runs the <see cref="ITokenRequestAdvisor{TApp}" />
///     and <see cref="IRefreshTokenAdvisor{TApp}" /> pipelines,
///     validates subject existence, enforces optional refresh token rotation,
///     and re-issues tokens with the stored scope, enforcing RFC 8707 §2.2 resource subsetting,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.3
///     </seealso>
///     .
/// </summary>
public sealed class RefreshTokenHandler<TApp>(
    IClientAuthenticationService<TApp> client,
    ITokenStore<SchemataToken>         tokens,
    TokenService                       issuer,
    IOptions<RefreshTokenFlowOptions>  options,
    IServiceProvider                   sp,
    TimeProvider?                      time = null
) : IGrantHandler
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IGrantHandler Members

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
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.RefreshToken });
        }

        var application = (await client.AuthenticateAsync(null, ClientAuthenticationForm.Build(
                                                              request.ClientId, request.ClientSecret,
                                                              request.ClientAssertion, request.ClientAssertionType), headers, ct))?.Application;
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
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
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        var token = await tokens.FindByReferenceIdAsync(request.RefreshToken, ct);
        if (string.IsNullOrWhiteSpace(token?.Payload) || token.Type != TokenTypes.RefreshToken) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }
        if (token.Status == TokenStatuses.Redeemed) {
            if (!string.IsNullOrWhiteSpace(token.Application)
                && !string.Equals(token.Application, application.CanonicalName, StringComparison.Ordinal)) {
                throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
            }
            if (string.IsNullOrWhiteSpace(token.Family)) {
                throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
            }

            await tokens.InvalidateFamilyAsync(token.Family, ct);
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        if (token.ExpireTime is { } expiredAt && expiredAt <= _time.GetUtcNow().UtcDateTime) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        // Client binding precedes every destructive action: a token presented by a different
        // client fails as invalid_grant and must never trigger the replay revocation below.
        if (!string.IsNullOrWhiteSpace(token.Application)
         && !string.Equals(token.Application, application.CanonicalName, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var principal = await issuer.Validate(token.Payload, lifetime: false);
        var original = AuthorizationGrantContexts.Deserialize(token.GrantContext);
        if (principal is null
         || original is null
         || !string.Equals(original.Subject, token.Parent, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var scope = original.Scope;
        if (!string.IsNullOrWhiteSpace(request.Scope) && !ScopeParser.IsSubset(request.Scope, scope)) {
            throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.INVALID_SCOPE);
        }
        var issuedScope = string.IsNullOrWhiteSpace(request.Scope) ? scope : request.Scope;
        var grant = AuthorizationGrantContexts.Narrow(original, issuedScope, token.SessionId);
        grant.ExpiresAt = original.ExpiresAt ?? (token.ExpireTime is { } deadline
            ? new DateTimeOffset(DateTime.SpecifyKind(deadline, DateTimeKind.Utc))
            : null);
        ctx.Set(grant);

        var exchange = new RefreshTokenContext<TApp> {
            Request     = request,
            Application = application,
            Token       = token,
            Principal   = principal,
        };

        switch (await Advisor.For<IRefreshTokenAdvisor<TApp>>()
                             .RunAsync(ctx, exchange, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.AccessDenied, SchemataResources.ACCESS_DENIED);
        }

        // RFC 8707 §2.2: a refresh request may narrow the token to any subset of the resources of
        // the original grant ("...or a subset thereof"); values outside the original set are
        // invalid_target, and an omitted parameter adopts the original set. The original set rides
        // the refresh token's space-joined `resources` claim.
        var grantedResources = principal.FindFirstValue(Claims.Resources)?
                                        .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (request.Resource is { Count: > 0 }) {
            var requested = new HashSet<string>(request.Resource, StringComparer.Ordinal);
            if (!requested.IsSubsetOf(grantedResources)) {
                throw new OAuthException(OAuthErrors.InvalidTarget, SchemataResources.INVALID_TARGET);
            }
        }

        var resources = request.Resource is { Count: > 0 } ? request.Resource : grantedResources;

        if (!string.IsNullOrWhiteSpace(token.Parent)) {
            var provider = sp.GetService<ISubjectProvider>();
            if (provider is not null && !await provider.ValidateAsync(token.Parent, ct)) {
                throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
            }
        }

        var claims = new List<Claim> {
            new(Claims.ClientId, application.ClientId),
        };

        if (!string.IsNullOrWhiteSpace(token.Parent)) {
            claims.Add(new(IdentityClaims.Subject, token.Parent));
        }

        AuthenticationContextExtensions.Apply(claims, grant.Authentication, destinations: false);

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));

        // RFC 9396 §6.1: the token advisor publishes the actual set — the request's narrowing
        // of the presented token's details, or the retained prior actual set when omitted.
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType]      = GrantTypes.RefreshToken,
            [Properties.RefreshPredecessor] = options.Value.RequireRefreshTokenRotation
                ? System.Text.Json.JsonSerializer.Serialize(token, Common.SchemataJson.Default)
                : null,
            [Properties.Scope]          = issuedScope,
            [Properties.Resources]      = grantedResources.Length > 0 ? string.Join(" ", grantedResources) : null,
            [Properties.AccessResources] = resources.Count > 0 ? string.Join(" ", resources) : null,
            [Properties.AuthorizationName] = token.Authorization,
            [Properties.SessionId]      = token.SessionId,
            [Properties.AuthorizationDetails] = exchange.AuthorizationDetails,

            [Properties.GrantContext]   = AuthorizationGrantContexts.Serialize(grant),

            // §5.5: republish the UserInfo claim names persisted on the refresh token so the
            // claim advisors keep carrying them onto the renewed access token.
            [Properties.UserinfoClaims] = principal.FindFirstValue(Claims.UserinfoRequest),
        });
    }

    #endregion
}
