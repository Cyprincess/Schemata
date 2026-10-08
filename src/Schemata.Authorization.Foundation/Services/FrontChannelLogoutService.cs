using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

public sealed class FrontChannelLogoutService<TApp>(
    IApplicationManager<TApp>              apps,
    ITokenStore<SchemataToken>             tokens,
    IOptions<Authentication.SchemataAuthorizationOptions> options
) : ILogoutNotifier
    where TApp : SchemataApplication
{
    public async Task<LogoutNotificationSnapshot> PrepareAsync(
        string? subject, string? session, CancellationToken ct = default) {
        var clients = await LogoutSessionHelper.GetSessionClientsAsync(tokens, subject, session, ct);
        var uris = new List<string>();
        await foreach (var app in apps.ListAsync(
                           q => q.Where(a => a.FrontChannelLogoutUri != null
                                          && a.CanonicalName != null
                                          && clients.Contains(a.CanonicalName)), ct)) {
            var uri = app.FrontChannelLogoutUri;
            if (string.IsNullOrWhiteSpace(uri)) continue;
            if (app.FrontChannelLogoutSessionRequired && string.IsNullOrWhiteSpace(session)) continue;
            if (!string.IsNullOrWhiteSpace(session)) {
                var separator = uri.Contains('?') ? '&' : '?';
                uri = $"{uri}{separator}{Claims.Issuer}={Uri.EscapeDataString(options.Value.Issuer!)}"
                    + $"&{Claims.SessionId}={Uri.EscapeDataString(session)}";
            }
            uris.Add(uri);
        }
        return new(uris, []);
    }

    public Task DispatchAsync(LogoutNotificationSnapshot snapshot, CancellationToken ct = default) {
        return Task.CompletedTask;
    }
}
