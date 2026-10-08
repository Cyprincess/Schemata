using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Messaging.Skeleton;

public sealed class MessageExecutionScope(AsyncServiceScope scope, TenantIdentity identity) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;
    public TenantIdentity Identity { get; } = identity;
    public IDisposable Enter() => TenantContext.Enter(Identity);

    public async ValueTask RestoreAsync(MessageContext? context, CancellationToken ct = default) {
        if (context is null) return;
        foreach (var propagator in Services.GetServices<IMessageContextPropagator>()) {
            await propagator.RestoreAsync(context.Items, Services, ct);
        }
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
