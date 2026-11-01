using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Tenancy.Actor;

/// <summary>
///     Creates the <see cref="TenantActorSystem{TActor}" /> owner through the actor system factory
///     seam, so tenancy contributes the system the host hosts without replacing the host's own
///     registration.
/// </summary>
/// <typeparam name="TActor">The persisted actor-state entity type every per-tenant runtime is closed on.</typeparam>
public sealed class TenantActorSystemFactory<TActor> : IActorSystemFactory
    where TActor : SchemataActor, new()
{
    public IActorSystem Create(IServiceProvider services) {
        return new TenantActorSystem<TActor>(
            services,
            services.GetRequiredService<IActorRegistry>(),
            services.GetRequiredService<IMessageExecutionScopeFactory>(),
            services.GetRequiredService<IOptions<SchemataActorOptions>>(),
            services.GetService<TimeProvider>());
    }
}
