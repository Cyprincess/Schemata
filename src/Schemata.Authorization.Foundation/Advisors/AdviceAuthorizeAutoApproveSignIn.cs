using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeAutoApproveSignIn{TApp, TAuth}" />.</summary>
public static class AdviceAuthorizeAutoApproveSignIn
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeConsent.DefaultOrder + 10_000_000;
}

/// <summary>
///     Handles auto-approval of authorization when existing consent covers the request and the current session is reusable.
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
    IAuthorizationManager<TAuth>           authorizations,
    IAuthenticationContextProvider? contexts = null
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeAutoApproveSignIn.DefaultOrder;

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

        // No session: fall through so the handler redirects to the interaction URI, where the user
        // signs in and the authorize request resumes. Only prompt=none turns this into
        // login_required, which AdviceAuthorizePrompt raises on its own.
        var subject = authz.Principal?.FindFirstValue(IdentityClaims.Subject);
        if (string.IsNullOrWhiteSpace(subject)) {
            return AdviseResult.Continue;
        }

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, subject),
            new(Claims.ClientId, authz.Application.ClientId),
        };

        var sid = authz.Principal?.FindFirstValue(authOptions.Value.SessionIdClaimType);
        if (contexts is not null) {
            claims.Stamp(await contexts.GetContextAsync(authz.Principal, ct));
        }

        var response = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));

        // The validating advisor owns the parameter: without its accepted grant set on the
        // context, the request carries no authorization details.
        var json = ctx.TryGet<AuthorizationDetailsGrant>(out var details) ? details?.Json : null;
        var authorization = new TAuth {
            Name                = Guid.NewGuid().ToString("n"),
            Application         = authz.Application!.CanonicalName,
            Subject             = subject,
            Type                = AuthorizationTypes.AdHoc,
            Status              = TokenStatuses.Valid,
            Scopes              = authz.Request?.Scope,
            RedirectUri         = authz.Request?.RedirectUri,
            ResponseType        = authz.Request?.ResponseType,
            CodeChallengeMethod = authz.Request?.CodeChallengeMethod,
            AcrValues           = authz.Request?.AcrValues,
        };

        authorization.AuthorizationDetails = json;

        await authorizations.CreateAsync(authorization, ct);

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]           = GrantTypes.AuthorizationCode,
            [Properties.Scope]               = authz.Request?.Scope,
            [Properties.Resources]           = authz.Request?.Resource is { Count: > 0 } ? string.Join(" ", authz.Request.Resource) : null,
            [Properties.ResponseType]        = authz.Request?.ResponseType,
            [Properties.Nonce]               = authz.Request?.Nonce,
            [Properties.RedirectUri]         = authz.Request?.RedirectUri,
            [Properties.ResponseMode]        = authz.ResponseMode,
            [Properties.State]               = authz.Request?.State,
            [Properties.CodeChallenge]       = authz.Request?.CodeChallenge,
            [Properties.CodeChallengeMethod] = authz.Request?.CodeChallengeMethod,
            [Properties.DpopJkt]             = authz.Request?.DpopJkt,
            [Properties.AuthorizationName]   = authorization.CanonicalName,
            [Properties.SessionId]           = sid,
            [Properties.MaxAge]              = authz.Request?.MaxAge,
        };

        properties[Properties.AuthorizationDetails] = json;

        ctx.Set(AuthorizationResult.SignIn(response, properties));

        return AdviseResult.Handle;
    }

    #endregion
}
