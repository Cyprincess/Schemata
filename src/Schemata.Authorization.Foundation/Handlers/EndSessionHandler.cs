using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     OIDC RP-Initiated Logout endpoint.
///     Validates the optional <c>id_token_hint</c>, requires end-user confirmation through the
///     interaction boundary when the request alone cannot be trusted, resolves the OP session to
///     discover relying parties, and performs front-channel and back-channel logout via registered
///     <see cref="ILogoutNotifier" /> services,
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
    IOpLogoutService                       logout,
    ITokenStore<SchemataToken>             tokens,
    IOptions<JsonSerializerOptions>        json,
    ILogger<EndSessionHandler<TApp>>       logger,
    IPairwiseSubjectTranslator?            pairwise = null,
    TimeProvider?                          time = null,
    IOpSessionService?                     sessions = null
) : EndSessionEndpoint
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public override async Task<AuthorizationResult> HandleAsync(
        EndSessionRequest request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    ) {
        var subject = principal.FindFirstValue(IdentityClaims.Subject);
        var session = principal.FindFirstValue(config.Value.SessionIdClaimType);

        TApp? application = null;

        if (!string.IsNullOrWhiteSpace(request.IdTokenHint)) {
            // RP-Initiated Logout §2 requires issuer validation before trusting the hint's identity.
            var (hint, _) = await issuer.ValidateForLogout(request.IdTokenHint, request.ClientId);
            if (hint is null) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
            }
            application = await ResolveClientAsync(request, strict: true, ct, hint);

            var validatedHint = hint;
            var hintSubject   = validatedHint.FindFirstValue(IdentityClaims.Subject);
            var hintSession   = validatedHint.FindFirstValue(Claims.SessionId);
            if (pairwise is not null && application is not null) {
                var caller = new ClaimsPrincipal(new ClaimsIdentity([
                    new(Claims.ClientId, application.ClientId ?? string.Empty),
                ], "logout-hint"));
                hintSubject = await pairwise.ToCanonicalAsync(hintSubject, caller, ct);
            }

            var target = new LogoutSessionTarget(hintSubject, hintSession, application!.CanonicalName!);
            var evidence = sessions is null ? null : await sessions.ResolveLogoutAsync(target, principal, ct);
            var current = evidence?.MatchesCurrent == true;
            if (!current) {
                var sameSubject = string.Equals(hintSubject, subject, StringComparison.Ordinal);
                var confirmedSubject = string.IsNullOrWhiteSpace(subject) ? hintSubject : subject;
                var confirmedSession = string.IsNullOrWhiteSpace(subject) ? hintSession : session;
                if (principal.Identity?.IsAuthenticated == true && sessions is not null) {
                    confirmedSession = await sessions.ResolveAsync(principal, confirmedSubject, ct);
                }
                return await RequireConfirmationAsync(
                    request, confirmedSubject, confirmedSession,
                    preserveRpAuthority: string.IsNullOrWhiteSpace(subject) || sameSubject, ct, application);
            }

            subject = evidence!.Target.Subject;
            session = evidence.Target.SessionId;
        } else {

            application = await ResolveClientAsync(request, strict: false, ct);
            if (principal.Identity?.IsAuthenticated == true && sessions is not null) {
                session = await sessions.ResolveAsync(principal, subject, ct);
            }
            return await RequireConfirmationAsync(request, subject, session, preserveRpAuthority: true, ct, application);
        }

        return await ExecuteLogoutAsync(request, principal, application, subject, session, ct);
    }

    internal async Task<AuthorizationResult> ExecuteApprovedAsync(
        EndSessionRequest request,
        ClaimsPrincipal principal,
        LogoutSessionTarget target,
        CancellationToken ct
    ) {
        var application = await ResolveClientAsync(request, strict: !string.IsNullOrWhiteSpace(request.IdTokenHint), ct);
        if (!string.Equals(application?.CanonicalName ?? string.Empty, target.Application, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
        }
        return await ExecuteLogoutAsync(request, principal, application, target.Subject, target.SessionId, ct);
    }

    /// <summary>
    ///     Resolves the client the post-logout redirect is validated against. With a hint present
    ///     the hint's client is authoritative and an explicit client_id must agree with it; in the
    ///     strict pass a hint that fails to validate or resolve aborts the logout.
    /// </summary>
    private async Task<TApp?> ResolveClientAsync(
        EndSessionRequest request, bool strict, CancellationToken ct, ClaimsPrincipal? validated = null) {
        TApp? application = null;

        if (!string.IsNullOrWhiteSpace(request.IdTokenHint)) {
            var hint = validated ?? await issuer.Validate(request.IdTokenHint, request.ClientId, false);

            var client = hint?.FindFirstValue(Claims.ClientId) ?? hint?.FindFirstValue(Claims.Audience);
            if (!string.IsNullOrWhiteSpace(client)) {
                application = await apps.FindByClientIdAsync(client, ct);
            }

            if (strict && (hint is null || application is null)) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
            }

            if (application is not null && !string.IsNullOrWhiteSpace(request.ClientId)
                && !string.Equals(request.ClientId, application.ClientId, StringComparison.Ordinal)) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
            }

            return application;
        }

        if (!string.IsNullOrWhiteSpace(request.ClientId)) {
            application = await apps.FindByClientIdAsync(request.ClientId, ct);
        }

        return application;
    }

    private async Task<AuthorizationResult> RequireConfirmationAsync(
        EndSessionRequest request,
        string?           subject,
        string?           session,
        bool              preserveRpAuthority,
        CancellationToken ct,
        TApp?             application
    ) {
        if (string.IsNullOrWhiteSpace(config.Value.InteractionUri)) {
            logger.LogError("Logout confirmation required but no interaction URI is configured.");
            throw new OAuthException(OAuthErrors.ServerError, SchemataResources.INTERNAL);
        }

        var reference = issuer.CreateReference();
        var approved = preserveRpAuthority ? request : new EndSessionRequest();
        var target = new LogoutSessionTarget(subject, session, preserveRpAuthority ? application?.CanonicalName ?? string.Empty : string.Empty);
        var payload = JsonSerializer.Serialize(new LogoutConfirmationPayload(approved, target), json.Value);

        var confirmation = new SchemataToken {
            Type        = TokenTypes.Logout,
            Status      = TokenStatuses.Valid,
            ReferenceId = reference,
            Payload     = payload,
            Parent      = subject,
            SessionId   = session,
            ExpireTime  = _time.GetUtcNow().UtcDateTime + config.Value.InteractionTokenLifetime,
        };

        await tokens.CreateAsync(confirmation, ct);

        var query = QueryString.Create(new Dictionary<string, string?> {
            { Parameters.Code, reference },
            { Parameters.CodeType, TokenTypeUris.Logout },
        });

        return AuthorizationResult.Redirect($"{config.Value.InteractionUri}{query.ToUriComponent()}");
    }

    private async Task<AuthorizationResult> ExecuteLogoutAsync(
        EndSessionRequest request,
        ClaimsPrincipal   principal,
        TApp?             application,
        string?           subject,
        string?           session,
        CancellationToken ct
    ) {
        string? redirect = null;
        if (application is not null && await apps.ValidatePostLogoutRedirectUriAsync(application, request.PostLogoutRedirectUri, ct)) {
            redirect = request.PostLogoutRedirectUri;
        }

        var result = await logout.LogoutAsync(principal, subject, session, ct);
        var uri = BuildRedirectUri(redirect, request.State);
        if (result.FrontChannelUris is { Count: > 0 } uris) {
            return AuthorizationResult.Content(new LogoutPage(BuildLogoutPage(uris, uri, CultureInfo.CurrentCulture)));
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
    public static string BuildLogoutPage(IReadOnlyList<string> uris, string? redirect, CultureInfo? culture = null) {
        var title  = SchemataResources.GetResourceString(SchemataResources.LOGOUT_PAGE_TITLE);
        var text   = SchemataResources.GetResourceString(SchemataResources.LOGOUT_PAGE_TEXT);
        var cont   = SchemataResources.GetResourceString(SchemataResources.LOGOUT_PAGE_CONTINUE);
        var lang   = culture?.Name;
        var encode = (string? value) => WebUtility.HtmlEncode(value);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.Append("<html");
        if (!string.IsNullOrEmpty(lang)) {
            sb.Append(" lang=\"").Append(encode(lang)).Append('"');
        }
        sb.Append("><head><title>").Append(encode(title)).Append("</title>");

        if (!string.IsNullOrWhiteSpace(redirect)) {
            sb.Append("<meta http-equiv=\"refresh\" content=\"5;url=");
            sb.Append(encode(redirect));
            sb.Append("\">");
        }

        sb.AppendLine("</head><body>");

        foreach (var uri in uris) {
            sb.Append("<iframe src=\"");
            sb.Append(encode(uri));
            sb.AppendLine("\" style=\"display:none\"></iframe>");
        }

        sb.Append("<p>").Append(encode(text)).Append("</p>");

        if (!string.IsNullOrWhiteSpace(redirect)) {
            sb.Append("<p><a href=\"");
            sb.Append(encode(redirect));
            sb.Append("\">").Append(encode(cont)).Append("</a></p>");

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
