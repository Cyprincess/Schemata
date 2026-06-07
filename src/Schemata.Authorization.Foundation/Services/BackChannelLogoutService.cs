using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Shared constants for back-channel logout operations.</summary>
public static class BackChannelLogoutService
{
    /// <summary>HTTP timeout for individual back-channel logout requests.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
}

/// <summary>
///     Performs OIDC Back-Channel Logout per
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>.
///     Discovers session clients from stored tokens, resolves per-RP subject
///     identifiers (including pairwise), builds a logout token JWT, and triggers
///     one <see cref="BackChannelLogoutJob" /> per relying party through
///     <see cref="IScheduler" />. <c>UseScheduling()</c> must be configured at
///     host bootstrap; when the scheduler is absent, <c>EnqueueBackChannelAsync</c>
///     raises <c>FAILED_PRECONDITION</c> before any notification is attempted.
/// </summary>
public sealed class BackChannelLogoutService<TApp, TToken>(
    IApplicationManager<TApp>              apps,
    ITokenManager<TToken>                  tokens,
    TokenService                           issuer,
    ISubjectIdentifierService              identifier,
    IOptions<SchemataAuthorizationOptions> options,
    IServiceProvider                       services
) : ILogoutNotifier
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    private static Guid NextJobId() {
#if NET10_0_OR_GREATER
        return Guid.CreateVersion7();
#else
        return Guid.NewGuid();
#endif
    }

    #region ILogoutNotifier Members

    public Task<List<string>>
        GetFrontChannelUrisAsync(string? subject, string? session, CancellationToken ct = default) {
        return Task.FromResult<List<string>>([]);
    }

    public async Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(session)) {
            return;
        }

        var clients = await LogoutSessionHelper.GetSessionClientsAsync(tokens, subject, session, ct);

         var scheduler = services.GetService<IScheduler>();
         if (scheduler == null) {
             throw new FailedPreconditionException(
                 message: "Back-channel logout requires Scheduling; call UseScheduling() at host bootstrap."
             );
         }

         await foreach (var app in apps.ListAsync(
                            q => q.Where(a => a.BackChannelLogoutUri != null
                                           && a.Name != null
                                           && clients.Contains(a.Name)), ct)) {
            if (app.BackChannelLogoutSessionRequired && string.IsNullOrWhiteSpace(session)) {
                continue;
            }

            var sub = !string.IsNullOrWhiteSpace(subject) ? identifier.Resolve(subject, app) : null;

            var claims = new List<Claim> {
                new(Claims.JwtId, Guid.NewGuid().ToString("N")),
                new(Claims.Events, "{\"" + EventTypes.LogoutEvent + "\":{}}", JsonClaimValueTypes.Json),
            };

            if (!string.IsNullOrWhiteSpace(options.Value.Issuer)) {
                claims.Add(new(Claims.Issuer, options.Value.Issuer));
            }

            if (!string.IsNullOrWhiteSpace(sub)) {
                claims.Add(new(Claims.Subject, sub));
            }

            if (!string.IsNullOrWhiteSpace(app.ClientId)) {
                claims.Add(new(Claims.Audience, app.ClientId));
            }

            if (!string.IsNullOrWhiteSpace(session)) {
                claims.Add(new(Claims.SessionId, session));
            }

            var uri = app.BackChannelLogoutUri;
            var jwt = issuer.CreateToken(claims, TimeSpan.FromMinutes(2));

            await scheduler.TriggerAsync<BackChannelLogoutJob>(new() {
                Job = $"authorization/back-channel-logout/{NextJobId():N}",
                Variables = new Dictionary<string, object?> {
                    [BackChannelLogoutJob.VariableKeys.Uri]         = uri,
                    [BackChannelLogoutJob.VariableKeys.LogoutToken] = jwt,
                },
            }, ct);
        }
    }

    #endregion
}
