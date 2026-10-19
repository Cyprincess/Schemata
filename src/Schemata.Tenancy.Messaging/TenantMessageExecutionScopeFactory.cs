using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Messaging;

public sealed class TenantMessageExecutionScopeFactory<TTenant>(IServiceScopeFactory scopes) : IMessageExecutionScopeFactory
    where TTenant : SchemataTenant
{
    public async ValueTask<MessageExecutionScope> CreateAsync(MessageContext? context, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var identity = TenantMessageContextPropagator.Identity(context?.Items);
        var bootstrap = scopes.CreateAsyncScope();
        AsyncServiceScope? final = null;
        Exception? failure = null;
        try {
            var factory = bootstrap.ServiceProvider.GetRequiredService<ITenantServiceScopeFactory<TTenant>>();
            final = await factory.CreateAsync(identity, ct);
        } catch (Exception error) {
            failure = error;
        }
        try { await bootstrap.DisposeAsync(); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) {
            if (final is { } abandoned) {
                try {
                    using var execution = TenantContext.Enter(identity);
                    await abandoned.DisposeAsync();
                }
                catch (Exception error) { failure = new AggregateException(failure, error); }
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return new(final!.Value, () => TenantContext.Enter(identity));
    }
}
