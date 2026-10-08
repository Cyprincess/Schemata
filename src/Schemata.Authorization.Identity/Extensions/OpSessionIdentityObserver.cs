using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Bridges the Authorization OP session to ASP.NET Core Identity host sign-ins: at host
///     authentication completion the session service resolves one canonical identifier from
///     trustworthy evidence (an incoming same-subject ticket claim or a subject-bound browser
///     mirror) and stamps it on the ticket, so later authorizations reuse the host-ticket
///     session without depending on the browser mirror.
/// </summary>
public sealed class OpSessionIdentityObserver(
    IOpSessionService                                                                               sessions,
    IOpLogoutService                                                                                logout,
    Options.IOptions<Schemata.Authorization.Foundation.Authentication.SchemataAuthorizationOptions> options
) : Schemata.Identity.Skeleton.IHostSignInObserver
{
    /// <summary>
    ///     The host-ticket session claim the server is configured to read; stamping the literal
    ///     protocol claim would hide the session from handlers reading a customized claim type.
    /// </summary>
    private readonly string _sessionIdClaim = options.Value.SessionIdClaimType;

    #region IHostSignInObserver Members

    public Task OnRenewingAsync(ClaimsPrincipal fresh, ClaimsPrincipal ticket, CancellationToken ct) {
        var sid = ticket.FindFirstValue(_sessionIdClaim);
        if (string.IsNullOrWhiteSpace(sid) || fresh.Identity is not ClaimsIdentity identity) {
            return Task.CompletedTask;
        }

        // Renewal replays the issued session: carry the ticket-bound identifier onto the fresh
        // principal under the configured claim; nothing is minted or rebound.
        if (!identity.HasClaim(claim => claim.Type == _sessionIdClaim)) {
            identity.AddClaim(new(_sessionIdClaim, sid));
        }

        return Task.CompletedTask;
    }

    public async Task<Schemata.Identity.Skeleton.HostSignOutResponse?> OnSigningOutAsync(ClaimsPrincipal principal, CancellationToken ct) {
        var subject = principal.FindFirstValue(Schemata.Abstractions.SchemataConstants.IdentityClaims.Subject);
        var sid     = principal.FindFirstValue(_sessionIdClaim);
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(sid)) {
            return null;
        }

        var result = await logout.LogoutAsync(principal, subject, sid, ct);
        if (result.FrontChannelUris is not { Count: > 0 } uris) {
            return null;
        }

        // Front-channel logout notifications only happen when a browser renders the prepared
        // URIs, so the host sign-out renders the same logout page the end-session endpoint
        // produces, without a post-logout redirect.
        return new(Schemata.Authorization.Foundation.Handlers.EndSessionHandler<Schemata.Authorization.Skeleton.Entities.SchemataApplication>
                           .BuildLogoutPage(uris, null), "text/html; charset=utf-8");
    }

    public async Task OnSigningInAsync(ClaimsPrincipal principal, ClaimsPrincipal? prior, CancellationToken ct) {
        var subject = principal.FindFirstValue(Schemata.Abstractions.SchemataConstants.IdentityClaims.Subject);

        // Same-user re-login keeps the observable OP session stable: when the incoming ticket
        // already carries a session identifier for the same canonical subject, reuse it before
        // consulting any other evidence. An account switch inherits nothing and mints fresh.
        var priorSubject = prior?.FindFirstValue(Schemata.Abstractions.SchemataConstants.IdentityClaims.Subject);
        var priorSid     = prior?.FindFirstValue(_sessionIdClaim);
        if (!string.IsNullOrWhiteSpace(priorSubject)
         && string.Equals(priorSubject, subject, System.StringComparison.Ordinal)
         && !string.IsNullOrWhiteSpace(priorSid)
         && principal.Identity is ClaimsIdentity reuse) {
            reuse.AddClaim(new(_sessionIdClaim, priorSid));
        }
        var sid = await sessions.IssueAsync(principal, subject, ct);
        if (string.IsNullOrWhiteSpace(sid) || principal.Identity is not ClaimsIdentity identity) {
            return;
        }

        if (identity.FindFirst(_sessionIdClaim) is { } current && !string.Equals(current.Value, sid, System.StringComparison.Ordinal)) {
            identity.RemoveClaim(current);
        }

        if (!identity.HasClaim(claim => claim.Type == _sessionIdClaim)) {
            identity.AddClaim(new(_sessionIdClaim, sid));
        }
    }

    #endregion
}