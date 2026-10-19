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
using Schemata.Authorization.Skeleton.Models;
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
    IAuthenticationContextProvider?        contexts = null,
    IOpSessionService?                     sessions = null
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

        var application = authz.Application;
        if (application is null || string.IsNullOrWhiteSpace(application.ClientId)) {
            return AdviseResult.Continue;
        }

        var request = authz.Request;
        if (request is null) {
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
            new(Claims.ClientId, application.ClientId),
        };

        var sid = authz.Principal?.FindFirstValue(authOptions.Value.SessionIdClaimType)
                  ?? request.OpSessionId;
        if (string.IsNullOrWhiteSpace(sid) && sessions is not null) {
            // The host principal is authenticated and consent covers the request, so the OP
            // session is established here rather than minting a synthetic principal without one.
            sid = await sessions.IssueAsync(authz.Principal, subject, ct);
        }
        var response = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));

        // The validating advisor owns the parameter: without its accepted grant set on the
        // context, the request carries no authorization details.
        var json = request.AuthorizationDetails;
        var authorization = new TAuth {
            Application          = application.CanonicalName,
            Subject              = subject,
            Type                 = AuthorizationTypes.AdHoc,
            Status               = TokenStatuses.Valid,
            Scopes               = request.Scope,
            RedirectUri          = request.RedirectUri,
            ResponseType         = request.ResponseType,
            CodeChallengeMethod  = request.CodeChallengeMethod,
            AcrValues            = request.AcrValues,
            AuthorizationDetails = json,
        };

        await authorizations.CreateAsync(authorization, ct);

        var authentication = await AuthenticationContextExtensions.ResolveAsync(
            authz.Principal,
            request.Authentication,
            sid,
            authOptions.Value.SessionIdClaimType,
            contexts,
            ct);
        request.Authentication = authentication;
        var grantType = AuthorizationGrantContexts.SourceForResponseTypes(request.ResponseTypes);
        var grant = AuthorizationGrantContexts.Create(
            subject, request.Scope, sid, grantType, authentication,
            request.GrantProfile);

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]            = grantType,
            [Properties.Scope]                = request.Scope,
            [Properties.Resources]            = request.Resource is { Count: > 0 } ? string.Join(" ", request.Resource) : null,
            [Properties.ResponseType]         = request.ResponseType,
            [Properties.Nonce]                = request.Nonce,
            [Properties.RedirectUri]          = request.RedirectUri,
            [Properties.ResponseMode]         = authz.ResponseMode,
            [Properties.State]                = request.State,
            [Properties.CodeChallenge]        = request.CodeChallenge,
            [Properties.CodeChallengeMethod]  = request.CodeChallengeMethod,
            [Properties.DpopJkt]              = request.DpopJkt,
            [Properties.AuthorizationName]    = authorization.CanonicalName,
            [Properties.SessionId]            = sid,
            [Properties.GrantContext]         = AuthorizationGrantContexts.Serialize(grant),
            [Properties.GrantProfile]         = grant.Profile,
            [Properties.MaxAge]               = request.MaxAge,
            [Properties.ClaimsRequest]        = request.RequestedClaims is not null ? request.Claims : null,
            [Properties.SessionStateSalt]     = request.SessionStateSalt,
            [Properties.AuthorizationDetails] = json,
        };

        authz.Result = AuthorizationResult.SignIn(response, properties);

        return AdviseResult.Handle;
    }

    #endregion
}
