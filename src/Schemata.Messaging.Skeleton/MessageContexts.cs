using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Messaging.Skeleton;

/// <summary>Capture entry point for <see cref="MessageContext" />, called on the sending side.</summary>
public static class MessageContexts
{
    /// <summary>
    ///     Runs every registered <see cref="IMessageContextPropagator" /> against
    ///     <paramref name="source" /> and returns the flattened result.
    /// </summary>
    /// <param name="source">The provider of the scope the message is being sent from.</param>
    public static MessageContext Capture(IServiceProvider source) {
        var items = new Dictionary<string, string?>();

        foreach (var propagator in source.GetServices<IMessageContextPropagator>()) {
            propagator.Capture(items, source);
        }

        return new(items);
    }

    public static async ValueTask<MessageContext> CaptureAsync(IServiceProvider source, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var bootstrap = source.CreateAsyncScope();
        MessageContext initial;
        try {
            initial = Capture(bootstrap.ServiceProvider);
        } catch (Exception error) {
            try {
                await bootstrap.DisposeAsync();
            } catch (Exception cleanup) {
                throw new AggregateException(error, cleanup);
            }
            throw;
        }
        await bootstrap.DisposeAsync();
        var runner = source.GetRequiredService<IMessageExecutionScopeRunner>();
        return await runner.RunAsync(initial, (services, _) => ValueTask.FromResult(Capture(services)), ct);
    }
}
