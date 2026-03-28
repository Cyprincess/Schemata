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

public sealed class FrontChannelLogoutService<TApp, TToken>(
    IApplicationManager<TApp>              apps,
    ITokenManager<TToken>                  tokens,
    IOptions<SchemataAuthorizationOptions> options
) : ILogoutNotifier
    where TApp : SchemataApplication
    where TToken : SchemataToken
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

    public Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    #endregion
}
