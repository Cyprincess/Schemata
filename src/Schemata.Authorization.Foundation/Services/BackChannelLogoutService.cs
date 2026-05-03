using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Shared constants for back-channel logout operations.</summary>
public static class BackChannelLogoutService
{
    /// <summary>HTTP timeout for individual back-channel logout requests.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
}

/// <summary>
///     Performs OIDC Back-Channel Logout per <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>.
///     Discovers session clients from stored tokens, resolves per-RP subject
///     identifiers (including pairwise), builds a logout token JWT, and enqueues
///     an out-of-band HTTP POST to each RP's <c>backchannel_logout_uri</c>.
///     All heavy work (DB queries, JWT signing) runs in the request scope;
///     only the HTTP call is deferred to the background queue.
/// </summary>
public sealed class BackChannelLogoutService<TApp, TToken>(
    IApplicationManager<TApp>              apps,
    ITokenManager<TToken>                  tokens,
    TokenService                           issuer,
    ISubjectIdentifierService              identifier,
    IOptions<SchemataAuthorizationOptions> options,
    BackChannelLogoutQueue                 queue
) : ILogoutNotifier
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region ILogoutNotifier Members

    /// <inheritdoc />
    public Task<List<string>>
        GetFrontChannelUrisAsync(string? subject, string? session, CancellationToken ct = default) {
        return Task.FromResult<List<string>>([]);
    }

    /// <summary>
    ///     Enqueues back-channel logout requests for all RPs that have active
    ///     sessions for the given subject or session.
    ///     Each RP receives a signed logout token containing the subject (or
    ///     pairwise identifier), session ID, and the logout event type.
    /// </summary>
    public async Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(session)) {
            return;
        }

        var clients = await LogoutSessionHelper.GetSessionClientsAsync(tokens, subject, session, ct);

        await foreach (var app in apps.ListAsync(
                           q => q.Where(a => a.BackChannelLogoutUri != null
                                          && a.Name != null
                                          && clients.Contains(a.Name)), ct)) {
            if (app.BackChannelLogoutSessionRequired && string.IsNullOrWhiteSpace(session)) {
                continue;
            }

            // Resolve per-RP subject: pairwise clients receive a pairwise sub in the logout token.
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

            // Enqueue a self-contained HTTP POST task.  All domain work (DB queries,
            // JWT signing) is done here in the request scope; the task only needs
            // IHttpClientFactory from the background scope.
            queue.Enqueue(async (sp, token) => {
                try {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    var client  = factory.CreateClient(nameof(BackChannelLogoutService<,>));
                    client.Timeout = BackChannelLogoutService.Timeout;
                    var content  = new FormUrlEncodedContent([new(Parameters.LogoutToken, jwt)]);
                    var response = await client.PostAsync(uri, content, token);

                    if (!response.IsSuccessStatusCode) {
                        var log = sp.GetRequiredService<ILogger<BackChannelLogoutService<TApp, TToken>>>();
                        log.LogWarning("Back-channel logout to {Uri} returned {StatusCode}.", uri, (int)response.StatusCode);
                    }
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    var log = sp.GetRequiredService<ILogger<BackChannelLogoutService<TApp, TToken>>>();
                    log.LogWarning(ex, "Back-channel logout to {Uri} failed.", uri);
                }
            });
        }
    }

    #endregion
}
