using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Messaging.Skeleton;

public sealed class MessageExecutionScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    public async ValueTask RestoreAsync(MessageContext? context, CancellationToken ct = default) {
        if (context is null) return;
        foreach (var propagator in Services.GetServices<IMessageContextPropagator>()) {
            await propagator.RestoreAsync(context.Items, Services, ct);
        }
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
