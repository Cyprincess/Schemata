using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Messaging.Skeleton;

public sealed class MessageExecutionScope(AsyncServiceScope scope, Func<IDisposable> enter) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;
    /// <summary>The caller enters after awaiting scope creation and owns the returned lease.</summary>
    public IDisposable Enter() => enter();

    public async ValueTask RestoreAsync(MessageContext? context, CancellationToken ct = default) {
        if (context is null) return;
        foreach (var propagator in Services.GetServices<IMessageContextPropagator>()) {
            await propagator.RestoreAsync(context.Items, Services, ct);
        }
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
