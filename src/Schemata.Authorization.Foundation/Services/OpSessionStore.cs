using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Cookie-backed browser mirror for the OP session identifier, per
///     <seealso href="https://openid.net/specs/openid-connect-session-1_0.html#OPRequest">
///         OpenID Connect Session Management 1.0 §3.2: OP Request
///     </seealso>
///     : the sid lives in an HttpOnly <c>{opstate}.sid</c> cookie bound to the subject it was
///     persisted for, memoized per request so repeated issuance within one request observes one
///     identifier without cookie churn. The mirror is untrusted evidence — it never overrides a
///     verified host ticket, and the subject binding keeps an account switch (A to B) from
///     reusing the previous account's session. Clearing the canonical target deletes the cookie
///     and rotates the OP user-agent state.
/// </summary>
internal sealed class OpSessionStore(
    IHttpContextAccessor               accessor,
    IOptions<SessionManagementOptions> session
) : IOpSessionStore
{
    /// <summary>Runs after host-ticket adapters so sign-out precedes browser-state rotation.</summary>
    public const int DefaultOrder = 100;

    private static readonly object SidKey = new();

    private readonly string _opStateCookie = session.Value.OpStateCookieName;
    private readonly string _sidCookie     = $"{session.Value.OpStateCookieName}.sid";

    #region IOpSessionStore Members

    public int Order => DefaultOrder;

    public Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        var (sid, bound) = ReadMirror();
        return Task.FromResult(sid is { Length: > 0 }
            ? new OpSessionEvidence(bound, sid, OpSessionProvenance.BrowserMirror)
            : null);
    }

    public Task<LogoutSessionEvidence?> ReadLogoutAsync(
        LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        return Task.FromResult<LogoutSessionEvidence?>(null);
    }

    public Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        var http = accessor.HttpContext;
        if (http is null) {
            return Task.CompletedTask;
        }

        var (storedSid, _) = ReadMirror();
        if (!string.Equals(storedSid, sessionId, StringComparison.Ordinal)) {
            WriteMirror(http, sessionId, subject);
            OpState.Rotate(http, _opStateCookie);
        } else {
            OpState.GetOrCreate(http, _opStateCookie);
        }

        Memoize(http, sessionId, subject);

        return Task.CompletedTask;
    }

    public Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        var http = accessor.HttpContext;
        if (http is null) return Task.CompletedTask;
        var (storedSid, storedSubject) = ReadMirror();
        if (!string.IsNullOrWhiteSpace(storedSid) && !string.IsNullOrWhiteSpace(sessionId)
            && !string.Equals(storedSid, sessionId, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(storedSubject) && !string.IsNullOrWhiteSpace(subject)
               && !string.Equals(storedSubject, subject, StringComparison.Ordinal)) {
            return Task.CompletedTask;
        }

        http.Response.Cookies.Delete(_sidCookie, new() {
            Secure   = true,
            SameSite = SameSiteMode.None,
            HttpOnly = true,
            Path     = "/",
        });
        OpState.Rotate(http, _opStateCookie);

        // Record the cleared state in the request-local mirror so a later Read in this same
        // request falls back to neither the memo nor the stale incoming cookie, and a later
        // Persist observes a change and writes a fresh cookie.
        Memoize(http, string.Empty, null);

        return Task.CompletedTask;
    }

    #endregion

    private (string? Sid, string? Subject) ReadMirror() {
        var http = accessor.HttpContext;
        if (http is null) {
            return (null, null);
        }

        if (http.Items.TryGetValue(SidKey, out var current) && current is Memoized memo) {
            return (memo.Sid, memo.Subject);
        }

        var raw = http.Request.Cookies[_sidCookie];
        return Decode(raw);
    }

    private static void Memoize(HttpContext http, string sid, string? subject) {
        http.Items[SidKey] = new Memoized(sid, subject);
    }

    private void WriteMirror(HttpContext http, string sid, string? subject) {
        var raw = $"{sid}|{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(subject ?? string.Empty))}";
        http.Response.Cookies.Append(_sidCookie, raw, new() {
            Secure   = true,
            SameSite = SameSiteMode.None,
            HttpOnly = true,
            Path     = "/",
        });
    }

    private static (string? Sid, string? Subject) Decode(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return (null, null);
        }

        var separator = raw.IndexOf('|');
        if (separator <= 0) {
            // A legacy value without a subject binding carries no identity fact; treat it as
            // absent rather than trusting an unbound session identifier.
            return (null, null);
        }

        var sid = raw[..separator];
        string? subject;
        try {
            subject = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw[(separator + 1)..]));
        } catch (FormatException) {
            return (null, null);
        }

        return string.IsNullOrEmpty(subject) ? (sid, null) : (sid, subject);
    }

    private sealed record Memoized(string Sid, string? Subject);
}
