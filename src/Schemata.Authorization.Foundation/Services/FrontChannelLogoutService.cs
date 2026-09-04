using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     OIDC Front-Channel Logout per
///     <seealso href="https://openid.net/specs/openid-connect-frontchannel-1_0.html">
///         OpenID Connect Front-Channel Logout
///         1.0
///     </seealso>
///     .
///     Discovers session clients from stored tokens and returns their
///     <c>frontchannel_logout_uri</c> values with appended <c>iss</c>
///     and <c>sid</c> parameters.  The caller renders these as iframes.
/// </summary>
public sealed class FrontChannelLogoutService<TApp>(
    IApplicationManager<TApp>              apps,
    ITokenStore<SchemataToken>                    tokens,
    IOptions<SchemataAuthorizationOptions> options
) : ILogoutNotifier
    where TApp : SchemataApplication
{
    #region ILogoutNotifier Members

    public async Task<List<string>> GetFrontChannelUrisAsync(
        string?           subject,
        string?           session,
        CancellationToken ct = default
    ) {
        var clients = await LogoutSessionHelper.GetSessionClientsAsync(tokens, subject, session, ct);

        var uris = new List<string>();

        await foreach (var app in apps.ListAsync(
                           q => q.Where(a => a.FrontChannelLogoutUri != null
                                          && a.Name != null
                                          && clients.Contains(a.Name)), ct)) {
            var uri = app.FrontChannelLogoutUri;
            if (string.IsNullOrWhiteSpace(uri)) {
                continue;
            }

            if (app.FrontChannelLogoutSessionRequired && string.IsNullOrWhiteSpace(session)) {
                continue;
            }

            // OpenID Connect Front-Channel Logout 1.0 §2: iss and sid are appended as a pair —
            // both are included, or neither.
            if (!string.IsNullOrWhiteSpace(session)) {
                var separator = uri.Contains('?') ? '&' : '?';
                uri = $"{uri}{separator}{Claims.Issuer}={Uri.EscapeDataString(options.Value.Issuer!)}"
                    + $"&{Claims.SessionId}={Uri.EscapeDataString(session)}";
            }

            uris.Add(uri);
        }

        return uris;
    }

    public Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    #endregion
}
