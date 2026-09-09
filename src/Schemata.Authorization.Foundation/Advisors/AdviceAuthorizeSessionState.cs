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

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceAuthorizeSessionState<TApp>(
    IHttpContextAccessor                  accessor,
    IOptions<SessionManagementOptions> options,
    IOpSessionService                    sessions,
    SessionStateFormulator               formul
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => AdviceAuthorizeClientAndRedirect.DefaultOrder + 5_000;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (ctx.Has<ParEndpointValidation>()) {
            return AdviseResult.Continue;
        }

        var request = authz.Request;
        var client  = authz.Application;
        if (request is null || client is null) {
            return AdviseResult.Continue;
        }

        var origin = OriginOf(request.RedirectUri);
        if (string.IsNullOrWhiteSpace(origin)) {
            return AdviseResult.Continue;
        }

        var http = accessor.HttpContext;
        if (http is null) {
            return AdviseResult.Continue;
        }

        await sessions.IssueAsync(
            authz.Principal,
            authz.Principal?.FindFirst(Schemata.Abstractions.SchemataConstants.IdentityClaims.Subject)?.Value,
            ct);

        var opUa    = OpState.GetOrCreate(http, options.Value.OpStateCookieName);
        var salt    = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var session = formul.Build(client.ClientId ?? string.Empty, origin, opUa, salt);
        authz.ResponseMode ??= "query";
        ctx.Set(new SessionStateFormulation(client.ClientId ?? string.Empty, origin, opUa, salt, session));
        SessionStateContext.Set(http, session);
        return AdviseResult.Continue;
    }

    private static string? OriginOf(string? redirectUri) {
        if (string.IsNullOrWhiteSpace(redirectUri) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) {
            return null;
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }
}

public sealed record SessionStateFormulation(string ClientId, string Origin, string OpUaState, string Salt, string Value);