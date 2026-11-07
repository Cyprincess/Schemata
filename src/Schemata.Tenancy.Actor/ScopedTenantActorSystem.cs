using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Actor;

public sealed class ScopedTenantActorSystem<TActor>(TenantIdentity identity, IServiceProvider invocation, TenantActorSystem<TActor> owner)
    : IActorSystem
    where TActor : SchemataActor, new()
{
    public Task<IActorRef> SpawnAsync(ActorId id, Props props) => owner.SpawnAsync(identity, invocation, id, props);

    public Task<IActorRef> GetAsync(ActorId id) => owner.GetAsync(identity, invocation, id);

    public Task StopAsync(ActorId id) => owner.StopAsync(identity, invocation, id);
}
