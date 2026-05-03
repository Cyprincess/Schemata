using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Background service that periodically prunes expired, revoked, and
///     consumed tokens from storage.  Runs every hour to prevent unbounded
///     growth of the token store.
/// </summary>
public class TokenCleanupService<TToken>(IServiceProvider sp) : BackgroundService
    where TToken : SchemataToken
{
    /// <summary>Executes the token pruning loop: waits one hour, then prunes.</summary>
    protected override async Task ExecuteAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                await Task.Delay(TimeSpan.FromHours(1), ct);

                using var scope   = sp.CreateScope();
                var       manager = scope.ServiceProvider.GetRequiredService<ITokenManager<TToken>>();
                await manager.PruneAsync(DateTime.UtcNow, ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
        }
    }
}
