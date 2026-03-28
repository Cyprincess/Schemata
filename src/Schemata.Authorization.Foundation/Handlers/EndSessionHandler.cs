using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class EndSessionHandler<TApp>(
    IApplicationManager<TApp>              apps,
    TokenService                           issuer,
    IOptions<SchemataAuthorizationOptions> config,
    IServiceProvider                       sp
) : EndSessionEndpoint
    where TApp : SchemataApplication
{
    public override async Task<AuthorizationResult> HandleAsync(
        EndSessionRequest request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    ) {
        var subject = principal.FindFirstValue(Claims.Subject);
        var session = principal.FindFirstValue(config.Value.SessionIdClaimType);

        TApp? application = null;

        if (!string.IsNullOrWhiteSpace(request.IdTokenHint)) {
            // Validate hint with client_id as audience when provided (tightens acceptance).
            var hint = await issuer.Validate(request.IdTokenHint, request.ClientId, false);

            var client = hint?.FindFirstValue(Claims.ClientId) ?? hint?.FindFirstValue(Claims.Audience);
            if (!string.IsNullOrWhiteSpace(client)) {
                application = await apps.FindByCanonicalNameAsync(client, ct);
            }

            // When client_id is also in the request, verify consistency.
            if (!string.IsNullOrWhiteSpace(request.ClientId) && application is not null) {
                var requested = await apps.FindByCanonicalNameAsync(request.ClientId, ct);
                if (requested?.Id != application.Id) {
                    application = null;
                }
            }

            // Subject from hint may be pairwise; fan-out prefers session when available.
            subject ??= hint?.FindFirstValue(Claims.Subject);
            session ??= hint?.FindFirstValue(Claims.SessionId);
        }

        // Fallback: no hint but client_id provided.
        if (application is null && !string.IsNullOrWhiteSpace(request.ClientId)) {
            application = await apps.FindByCanonicalNameAsync(request.ClientId, ct);
        }

        string? redirect = null;
        if (await apps.ValidatePostLogoutRedirectUriAsync(application, request.PostLogoutRedirectUri, ct)) {
            redirect = request.PostLogoutRedirectUri;
        }

        var uri       = BuildRedirectUri(redirect, request.State);
        var notifiers = sp.GetServices<ILogoutNotifier>();

        var uris = new List<string>();

        if (!string.IsNullOrWhiteSpace(subject) || !string.IsNullOrWhiteSpace(session)) {
            foreach (var notifier in notifiers) {
                uris.AddRange(await notifier.GetFrontChannelUrisAsync(subject, session, ct));
                await notifier.EnqueueBackChannelAsync(subject, session, ct);
            }
        }

        if (uris is { Count: > 0 }) {
            return AuthorizationResult.Content(BuildLogoutPage(uris, uri));
        }

        if (string.IsNullOrWhiteSpace(uri)) {
            return AuthorizationResult.Content(null);
        }

        return AuthorizationResult.Redirect(uri);
    }

    private static string? BuildRedirectUri(string? uri, string? state) {
        if (string.IsNullOrWhiteSpace(uri)) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(state)) {
            return uri;
        }

        var separator = uri.Contains('?') ? "&" : "?";
        return $"{uri}{separator}{Parameters.State}={Uri.EscapeDataString(state)}";
    }

    public static string BuildLogoutPage(List<string> uris, string? redirect) {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.Append("<html><head><title>Logging out</title>");

        if (!string.IsNullOrWhiteSpace(redirect)) {
            sb.Append("<meta http-equiv=\"refresh\" content=\"5;url=");
            sb.Append(WebUtility.HtmlEncode(redirect));
            sb.Append("\">");
        }

        sb.AppendLine("</head><body>");

        foreach (var uri in uris) {
            sb.Append("<iframe src=\"");
            sb.Append(WebUtility.HtmlEncode(uri));
            sb.AppendLine("\" style=\"display:none\"></iframe>");
        }

        sb.AppendLine("<p>Logging out…</p>");

        if (!string.IsNullOrWhiteSpace(redirect)) {
            sb.Append("<p><a href=\"");
            sb.Append(WebUtility.HtmlEncode(redirect));
            sb.AppendLine("\">Continue</a></p>");

            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");
            sb.AppendLine("var f=document.querySelectorAll('iframe'),d=0,t=f.length;");
            sb.AppendLine("function c(){if(++d>=t)r();}");
            sb.Append("function r(){window.location.href=\"");
            sb.Append(EscapeJs(redirect));
            sb.AppendLine("\";}");
            sb.AppendLine("for(var i=0;i<t;i++){f[i].onload=c;f[i].onerror=c;}");
            sb.AppendLine("setTimeout(r,5000);");
            sb.AppendLine("if(!t)r();");
            sb.AppendLine("})();");
            sb.AppendLine("</script>");
        }

        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string EscapeJs(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
