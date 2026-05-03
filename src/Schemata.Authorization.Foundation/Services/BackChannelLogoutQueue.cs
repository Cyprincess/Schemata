using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Background service that drains a bounded channel of back-channel logout
///     tasks.  Enqueued tasks are fire-and-forget HTTP POSTs that run outside
///     the request scope.  The channel drops oldest entries when full (bounded
///     to 100) to prevent memory exhaustion during high logout churn.
/// </summary>
public sealed class BackChannelLogoutQueue(IServiceProvider sp, ILogger<BackChannelLogoutQueue> logger) : BackgroundService
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(new BoundedChannelOptions(100) {
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    /// <summary>Enqueues an asynchronous logout task for background execution.</summary>
    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> task) { _channel.Writer.TryWrite(task); }

    /// <summary>Drains the channel, executing each task in a new DI scope.  Logs failures without stopping.</summary>
    protected override async Task ExecuteAsync(CancellationToken ct) {
        await foreach (var task in _channel.Reader.ReadAllAsync(ct)) {
            try {
                using var scope = sp.CreateScope();
                await task(scope.ServiceProvider, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "Background logout task failed.");
            }
        }
    }
}
