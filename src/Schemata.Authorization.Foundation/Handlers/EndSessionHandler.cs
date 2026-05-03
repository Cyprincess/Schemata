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

/// <summary>
///     OIDC RP-Initiated Logout endpoint.
///     Validates the optional <c>id_token_hint</c>, resolves the OP session to
///     discover relying parties, and performs front-channel and back-channel logout
///     via registered <see cref="ILogoutNotifier" /> services,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-session-1_0.html#ImplementationConsiderations">
///         OpenID Connect Session
///         Management 1.0 §5: Implementation Considerations
///     </seealso>
///     and
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
///     .
/// </summary>
public sealed class EndSessionHandler<TApp>(
    IApplicationManager<TApp>              apps,
    TokenService                           issuer,
    IOptions<SchemataAuthorizationOptions> config,
    IServiceProvider                       sp
) : EndSessionEndpoint
    where TApp : SchemataApplication
{
    /// <summary>
    ///     Processes an RP-initiated logout request.
    ///     When front-channel logout URIs are discovered, returns an HTML page
    ///     with hidden <c>iframe</c> elements that trigger each RP's logout endpoint;
    ///     the page meta-refreshes to the <c>post_logout_redirect_uri</c> after
    ///     all iframes load (or after a 5-second timeout).
    ///     Otherwise performs a direct redirect or returns an empty response.
    /// </summary>
    /// <param name="request">The logout request.</param>
    /// <param name="principal">The authenticated user principal.</param>
    /// <param name="ct">Cancellation token.</param>
    public override async Task<AuthorizationResult> HandleAsync(
        EndSessionRequest request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    ) {
        var subject = principal.FindFirstValue(Claims.Subject);
        var session = principal.FindFirstValue(config.Value.SessionIdClaimType);

        TApp? application = null;

        if (!string.IsNullOrWhiteSpace(request.IdTokenHint)) {
            // Validate the id_token_hint. When a client_id is also provided,
            // verify the audience matches for consistency (tightens acceptance
            // against misdirected hints).
            var hint = await issuer.Validate(request.IdTokenHint, request.ClientId, false);

            var client = hint?.FindFirstValue(Claims.ClientId) ?? hint?.FindFirstValue(Claims.Audience);
            if (!string.IsNullOrWhiteSpace(client)) {
                application = await apps.FindByCanonicalNameAsync(client, ct);
            }

            // When client_id is provided alongside the hint, verify consistency
            // to prevent cross-RP logout injection.
            if (!string.IsNullOrWhiteSpace(request.ClientId) && application is not null) {
                var requested = await apps.FindByCanonicalNameAsync(request.ClientId, ct);
                if (requested?.Id != application.Id) {
                    application = null;
                }
            }

            // Pairwise subjects in the hint may differ from the session principal;
            // prefer the session-level subject when available.
            subject ??= hint?.FindFirstValue(Claims.Subject);
            session ??= hint?.FindFirstValue(Claims.SessionId);
        }

        // Fallback: no hint but client_id provided — resolve the application
        // solely for post_logout_redirect_uri validation.
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

    /// <summary>
    ///     Builds an HTML page for front-channel logout.  Each RP URI is rendered
    ///     as a hidden iframe.  The page automatically redirects to the
    ///     <paramref name="redirect" /> URI after all iframes finish loading or
    ///     a 5-second timeout elapses.
    /// </summary>
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
