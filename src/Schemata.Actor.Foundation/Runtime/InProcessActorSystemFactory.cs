using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>Creates the in-process actor system closed on <typeparamref name="TActor" />.</summary>
internal sealed class InProcessActorSystemFactory<TActor> : IActorSystemFactory
    where TActor : SchemataActor, new()
{
    public IActorSystem Create(IServiceProvider services) {
        return new InProcessActorSystem<TActor>(
            services,
            services.GetRequiredService<IActorRegistry>(),
            services.GetRequiredService<IMessageExecutionScopeFactory>(),
            services.GetRequiredService<IOptions<SchemataActorOptions>>(),
            services.GetService<TimeProvider>());
    }
}
