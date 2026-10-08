using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Scheduling.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Shared constants for back-channel logout operations.</summary>
public static class BackChannelLogoutService
{
    /// <summary>HTTP timeout for individual back-channel logout requests.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Logout token validity window, measured from job execution time.</summary>
    internal static readonly TimeSpan LogoutTokenLifetime = TimeSpan.FromMinutes(2);
}

/// <summary>
///     Performs OIDC Back-Channel Logout per
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>.
///     Discovers session clients from stored participation facts, resolves per-RP subject
///     identifiers (pairwise when the pairwise flow feature is installed), and triggers one
///     <see cref="BackChannelLogoutJob" /> per relying party through <see cref="IScheduler" />
///     carrying the recipient facts; the job signs the logout token at execution time.
///     <c>UseScheduling()</c> must be configured at host bootstrap; when the scheduler is absent,
///     <c>DispatchAsync</c> raises <c>FAILED_PRECONDITION</c> before any notification is attempted.
/// </summary>
public sealed class BackChannelLogoutService<TApp>(
    IApplicationManager<TApp>  apps,
    ITokenStore<SchemataToken> tokens,
    IServiceProvider           services
) : ILogoutNotifier
    where TApp : SchemataApplication
{
    public async Task<LogoutNotificationSnapshot> PrepareAsync(
        string? subject, string? session, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(session)) {
            return new([], []);
        }
        var clients = await LogoutSessionHelper.GetSessionClientsAsync(tokens, subject, session, ct);
        var messages = new List<BackChannelLogoutMessage>();
        await foreach (var app in apps.ListAsync(
                           q => q.Where(a => a.BackChannelLogoutUri != null
                                          && a.CanonicalName != null
                                          && clients.Contains(a.CanonicalName)), ct)) {
            if (app.BackChannelLogoutSessionRequired && string.IsNullOrWhiteSpace(session)) continue;
            var translator = services.GetService<PairwiseSubjectTranslator<TApp>>();
            var sub = translator is not null && !string.IsNullOrWhiteSpace(subject)
                ? await translator.EnsureMappingAsync(app, subject, ct)
                : subject;
            messages.Add(new(app.BackChannelLogoutUri!, app.ClientId, sub, session, app.IdTokenSignedResponseAlg));
        }
        return new([], messages);
    }

    public async Task DispatchAsync(LogoutNotificationSnapshot snapshot, CancellationToken ct = default) {
        if (snapshot.BackChannelMessages.Count == 0) return;
        var scheduler = services.GetService<IScheduler>();
        if (scheduler is null) {
            throw new FailedPreconditionException(SchemataResources.BACK_CHANNEL_LOGOUT_REQUIRES_SCHEDULING);
        }
        foreach (var message in snapshot.BackChannelMessages) {
            await scheduler.TriggerAsync<BackChannelLogoutJob>(new() {
                Variables = new Dictionary<string, string?> {
                    [BackChannelLogoutJob.VariableKeys.Uri]              = message.Uri,
                    [BackChannelLogoutJob.VariableKeys.Audience]         = message.Audience,
                    [BackChannelLogoutJob.VariableKeys.Subject]          = message.Subject,
                    [BackChannelLogoutJob.VariableKeys.SessionId]        = message.SessionId,
                    [BackChannelLogoutJob.VariableKeys.SigningAlgorithm] = message.SigningAlgorithm,
                },
                Principal = SchedulingSystemPrincipal.Instance,
            }, ct);
        }
    }
}
