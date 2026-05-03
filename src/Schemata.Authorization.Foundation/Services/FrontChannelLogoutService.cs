using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

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
public sealed class FrontChannelLogoutService<TApp, TToken>(
    IApplicationManager<TApp>              apps,
    ITokenManager<TToken>                  tokens,
    IOptions<SchemataAuthorizationOptions> options
) : ILogoutNotifier
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region ILogoutNotifier Members

    /// <summary>
    ///     Returns the set of front-channel logout URIs for all RPs that have
    ///     active tokens under the given subject or session, per
    ///     <seealso href="https://openid.net/specs/openid-connect-frontchannel-1_0.html#OPLogout">
    ///         OpenID Connect
    ///         Front-Channel Logout 1.0 §3: OpenID Provider Logout Functionality
    ///     </seealso>
    ///     .
    ///     Each URI includes <c>iss</c> and optionally <c>sid</c> as query
    ///     parameters.
    /// </summary>
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

            var separator  = uri.Contains('?') ? '&' : '?';
            var parameters = new List<string>();

            if (!string.IsNullOrWhiteSpace(options.Value.Issuer)) {
                parameters.Add($"{Claims.Issuer}={Uri.EscapeDataString(options.Value.Issuer)}");
            }

            if (app.FrontChannelLogoutSessionRequired && !string.IsNullOrWhiteSpace(session)) {
                parameters.Add($"{Claims.SessionId}={Uri.EscapeDataString(session)}");
            }

            if (parameters.Count > 0) {
                uri = $"{uri}{separator}{string.Join('&', parameters)}";
            }

            uris.Add(uri);
        }

        return uris;
    }

    /// <inheritdoc />
    public Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    #endregion
}
