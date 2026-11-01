using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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

    public static async ValueTask<MessageContext> CaptureAsync(IServiceProvider source, CancellationToken ct = default,
        IMessageExecutionScopeFactory? scopes = null) {
        ct.ThrowIfCancellationRequested();
        MessageExecutionScope? selected = null;
        IDisposable? execution = null;
        Exception? failure = null;
        try {
            var bootstrap = source.CreateAsyncScope();
            MessageContext initial;
            Exception? preparation = null;
            try {
                initial = Capture(bootstrap.ServiceProvider);
                var factory = scopes ?? bootstrap.ServiceProvider.GetRequiredService<IMessageExecutionScopeFactory>();
                selected = await factory.CreateAsync(initial, ct);
            } catch (Exception error) {
                preparation = error;
                throw;
            } finally {
                try { await bootstrap.DisposeAsync(); }
                catch (Exception cleanup) {
                    Exception abandoned = preparation is null ? cleanup : new AggregateException(preparation, cleanup);
                    if (selected is not null) {
                        try { execution = selected.Enter(); }
                        catch (Exception entryFailure) { abandoned = new AggregateException(abandoned, entryFailure); }
                    }
                    ExceptionDispatchInfo.Capture(abandoned).Throw();
                }
            }
            execution = selected.Enter();
            await selected.RestoreAsync(initial, ct);
            return Capture(selected.Services);
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { if (selected is not null) await selected.DisposeAsync(); }
            catch (Exception error) { cleanup = error; }
            try { execution?.Dispose(); }
            catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                ExceptionDispatchInfo.Capture(failure is null ? cleanup : new AggregateException(failure, cleanup)).Throw();
            }
        }
    }
}
