using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Messaging;

public sealed class TenantMessageExecutionScopeFactory<TTenant>(IServiceScopeFactory scopes) : IMessageExecutionScopeFactory
    where TTenant : SchemataTenant
{
    public async ValueTask<MessageExecutionScope> CreateAsync(MessageContext? context, CancellationToken ct = default) {
        var identity = MessageContexts.Identity(context);
        await using var bootstrap = scopes.CreateAsyncScope();
        var factory = bootstrap.ServiceProvider.GetRequiredService<ITenantServiceScopeFactory<TTenant>>();
        var final = await factory.CreateAsync(identity, ct);
        return new(final, identity);
    }
}
