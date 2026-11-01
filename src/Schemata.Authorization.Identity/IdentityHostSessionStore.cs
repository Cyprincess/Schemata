using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Identity.Foundation;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Host-ticket lifecycle adapter for ASP.NET Core Identity. RP association is read from
///     live credentials owned by the authenticated subject and exact session. Clearing that
///     target signs out the application cookie scheme.
/// </summary>
public sealed class IdentityHostSessionStore(
    IHttpContextAccessor                   accessor,
    IOptions<SchemataAuthorizationOptions> options
) : IOpSessionStore
{
    /// <summary>Runs before browser channels so host sign-out precedes browser-state rotation.</summary>
    public const int DefaultOrder = 0;

    #region IOpSessionStore Members

    public int Order => DefaultOrder;

    public Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return Task.FromResult<OpSessionEvidence?>(null);
    }

    /// <summary>
    ///     Supplies only the host-ticket evidence itself: the authenticated application cookie
    ///     names this subject and session. Authorization-token association and online-generation
    ///     checks belong to the single session authority (<c>DefaultOpSessionService</c>).
    ///     The executing request scope names the application scheme — the global Identity
    ///     constant, or the tenant profile's <c>UseCookie</c> scheme once its closure is bound.
    /// </summary>
    public async Task<LogoutSessionEvidence?> ReadLogoutAsync(
        LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (accessor.HttpContext is not { } http) return null;
        var owned = await http.AuthenticateAsync(ApplicationScheme(http));
        if (!owned.Succeeded || owned.Principal is null) return null;
        var subject = owned.Principal.FindFirstValue(Abstractions.SchemataConstants.IdentityClaims.Subject);
        var sid = owned.Principal.FindFirstValue(options.Value.SessionIdClaimType);
        if (string.IsNullOrWhiteSpace(sid)
            || !string.Equals(subject, target.Subject, StringComparison.Ordinal)
            || target.SessionId is not null && !string.Equals(sid, target.SessionId, StringComparison.Ordinal)) return null;
        return new(target with { SessionId = sid }, true, false);
    }

    public Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return Task.CompletedTask;
    }
    public async Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        if (accessor.HttpContext is not { } http) return;
        var owned = await http.AuthenticateAsync(ApplicationScheme(http));
        if (!owned.Succeeded || owned.Principal is null) return;
        var ownedSubject = owned.Principal.FindFirstValue(Abstractions.SchemataConstants.IdentityClaims.Subject);
        var ownedSession = owned.Principal.FindFirstValue(options.Value.SessionIdClaimType);
        if (string.IsNullOrWhiteSpace(ownedSession) && http.RequestServices.GetService<IOpSessionService>() is { } authority) {
            ownedSession = await authority.ResolveAsync(owned.Principal, ownedSubject, ct);
        }
        if (!string.Equals(ownedSubject, subject, StringComparison.Ordinal)) return;
        if (!string.IsNullOrWhiteSpace(sessionId)
            && !string.Equals(ownedSession, sessionId, StringComparison.Ordinal)) return;
        await http.SignOutAsync(ApplicationScheme(http));
    }

    #endregion

    private static string ApplicationScheme(HttpContext http) {
        return http.RequestServices.GetRequiredService<IOptions<SchemataIdentityOptions>>().Value.ApplicationScheme;
    }
}
