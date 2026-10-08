using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Default <see cref="IOpLogoutService" />. When the caller does not name a session, one
///     canonical target is resolved from session evidence first, so notifier preparation,
///     invalidation, and retirement all act on that same session. Snapshots every registered
///     <see cref="ILogoutNotifier" /> before invalidation removes their evidence, invalidates the
///     canonical OP session and its host/browser adapters, dispatches the prepared notifications,
///     and retires the targeted participation facts. A session that cannot be terminated closes
///     the logout: neither dispatch nor retirement runs.
/// </summary>
public sealed class DefaultOpLogoutService(
    IOpSessionService                sessions,
    ITokenStore<SchemataToken>       tokens,
    IServiceProvider                 sp,
    ILogger<DefaultOpLogoutService>  logger
) : IOpLogoutService
{
    #region IOpLogoutService Members

    public async Task<OpLogoutResult> LogoutAsync(
        ClaimsPrincipal?  principal,
        string?           subject,
        string?           sessionId,
        CancellationToken ct = default
    ) {
        var targeted = !string.IsNullOrWhiteSpace(subject) || !string.IsNullOrWhiteSpace(sessionId);

        // Resolve one canonical target before notifier preparation, so a subject-only logout
        // whose SID lives in the browser adapter prepares, invalidates, and retires that session
        // instead of spanning every session of the subject.
        if (targeted && string.IsNullOrWhiteSpace(sessionId)) {
            sessionId = await sessions.ResolveAsync(principal, subject, ct);
        }

        var notifiers = targeted ? sp.GetServices<ILogoutNotifier>().ToArray() : [];
        var snapshots = new List<LogoutNotificationSnapshot>(notifiers.Length);
        foreach (var notifier in notifiers) {
            snapshots.Add(await notifier.PrepareAsync(subject, sessionId, ct));
        }

        try {
            await sessions.InvalidateAsync(principal, subject, sessionId, ct);
        } catch (Exception ex) {
            logger.LogError(ex, "OP session invalidation failed for subject {Subject} session {Session}.",
                            subject, sessionId);
            throw new OAuthException(OAuthErrors.ServerError, SchemataResources.INTERNAL);
        }

        var uris = new List<string>();
        for (var i = 0; i < notifiers.Length; i++) {
            uris.AddRange(snapshots[i].FrontChannelUris);
            await notifiers[i].DispatchAsync(snapshots[i], ct);
        }

        if (targeted) {
            await tokens.RetireParticipantsAsync(subject, sessionId, ct);
        }

        return new(uris);
    }

    #endregion
}
