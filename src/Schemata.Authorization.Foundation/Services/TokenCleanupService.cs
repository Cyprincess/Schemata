using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Foundation.Services;

public class TokenCleanupService<TToken>(IServiceProvider sp) : BackgroundService
    where TToken : SchemataToken
{
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
