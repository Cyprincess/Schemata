using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeAutoApproveSignIn{TApp, TAuth}" />.</summary>
public static class AdviceAuthorizeAutoApproveSignIn
{
    public const int DefaultOrder = AdviceAuthorizeConsent.DefaultOrder + 10_000_000;
}

/// <summary>
///     Handles auto-approval of authorization when the user has previously granted consent and reauthentication is
///     not required.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <remarks>
///     An auto-approved grant must still materialize a <typeparamref name="TAuth" /> so tokens are revocable
///     per-authorization and reachable by the end-session logout helper.
/// </remarks>
/// <seealso cref="AdviceAuthorizeConsent" />
public sealed class AdviceAuthorizeAutoApproveSignIn<TApp, TAuth>(
    IOptions<SchemataAuthorizationOptions> authOptions,
    IAuthorizationManager<TAuth>           authorizations
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizeAutoApproveSignIn.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (authz.RequireReauthentication) {
            return AdviseResult.Continue;
        }

        if (authz.ConsentDecision != ConsentDecision.Granted) {
            return AdviseResult.Continue;
        }

        if (string.IsNullOrWhiteSpace(authz.Application?.ClientId)) {
            return AdviseResult.Continue;
        }

        var subject = authz.Principal?.FindFirstValue(Claims.Subject);
        if (string.IsNullOrWhiteSpace(subject)) {
            throw new OAuthException(
                OAuthErrors.LoginRequired,
                SchemataResources.GetResourceString(SchemataResources.ST4011)
            ) {
                RedirectUri  = authz.Request?.RedirectUri,
                State        = authz.Request?.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        var claims = new List<Claim> {
            new(Claims.Subject, subject),
            new(Claims.ClientId, authz.Application.ClientId),
        };

        var response = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));

        var sid = authz.Principal?.FindFirstValue(authOptions.Value.SessionIdClaimType);
        var at  = authz.Principal?.FindFirstValue(Claims.AuthTime);

        var authorization = new TAuth {
            ApplicationName     = authz.Application.Name,
            Subject             = subject,
            Type                = AuthorizationTypes.AdHoc,
            Status              = TokenStatuses.Valid,
            Scopes              = authz.Request?.Scope,
            RedirectUri         = authz.Request?.RedirectUri,
            ResponseType        = authz.Request?.ResponseType,
            CodeChallengeMethod = authz.Request?.CodeChallengeMethod,
            AcrValues           = authz.Request?.AcrValues,
        };

        await authorizations.CreateAsync(authorization, ct);

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]           = GrantTypes.AuthorizationCode,
            [Properties.Scope]               = authz.Request?.Scope,
            [Properties.ResponseType]        = authz.Request?.ResponseType,
            [Properties.Nonce]               = authz.Request?.Nonce,
            [Properties.RedirectUri]         = authz.Request?.RedirectUri,
            [Properties.ResponseMode]        = authz.ResponseMode,
            [Properties.State]               = authz.Request?.State,
            [Properties.CodeChallenge]       = authz.Request?.CodeChallenge,
            [Properties.CodeChallengeMethod] = authz.Request?.CodeChallengeMethod,
            [Properties.AuthorizationName]   = authorization.Name,
            [Properties.SessionId]           = sid,
            [Properties.MaxAge]              = authz.Request?.MaxAge,
            [Properties.AuthTime]            = at,
        };

        ctx.Set(AuthorizationResult.SignIn(response, properties));

        return AdviseResult.Handle;
    }

    #endregion
}
