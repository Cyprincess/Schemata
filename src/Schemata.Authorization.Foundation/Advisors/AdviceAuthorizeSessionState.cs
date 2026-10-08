using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceAuthorizeSessionState<TApp>(
    IHttpContextAccessor                  accessor,
    IOptions<SessionManagementOptions> options,
    IOpSessionService                    sessions,
    SessionStateFormulator               formul
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => AdviceAuthorizeGrantProfile.DefaultOrder + 1_000;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (authz.Request?.GrantProfile != GrantProfiles.OpenIdConnect) {
            return AdviseResult.Continue;
        }

        if (authz.Stage == AuthorizationRequestStage.Pushed) {
            return AdviseResult.Continue;
        }

        var request = authz.Request;
        var client  = authz.Application;
        if (request is null || client is null) {
            return AdviseResult.Continue;
        }

        var origin = SessionStateFormulator.OriginOf(request.RedirectUri);
        if (string.IsNullOrWhiteSpace(origin)) {
            return AdviseResult.Continue;
        }

        var http = accessor.HttpContext;
        if (http is null) {
            return AdviseResult.Continue;
        }

        var subject    = authz.Principal?.FindFirst(Abstractions.SchemataConstants.IdentityClaims.Subject)?.Value;
        var issuedSid = await sessions.IssueAsync(authz.Principal, subject, ct);
        if (!string.IsNullOrWhiteSpace(issuedSid)) {
            authz.SessionId = issuedSid;
            authz.SessionSubject = subject;
        }

        var opUa = OpState.GetOrCreate(http, options.Value.OpStateCookieName);
        var salt = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        authz.ResponseMode ??= "query";
        // Only the salt crosses the interaction; the success callback recomputes session_state
        // from the final browser/session state after sign-in, so a pre-login hash never persists.
        request.SessionStateSalt = salt;
        authz.SessionStateSalt = salt;
        // The initial value serves same-request error callbacks (OAuthExceptionFilter).
        SessionStateContext.Set(http, formul.Build(client.ClientId ?? string.Empty, origin, opUa, salt));
        return AdviseResult.Continue;
    }
}