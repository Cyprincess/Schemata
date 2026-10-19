using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Push.Foundation;

namespace Schemata.Push.Actor.Handlers;

internal sealed class PushActorRouting<TRequest> : IActorRouting<TRequest>
    where TRequest : ISubscriptionScoped
{
    public ValueTask<ActorId?> ResolveAsync(TRequest request, IServiceProvider services, CancellationToken ct) {
        return ValueTask.FromResult<ActorId?>(new ActorId("push", request.SubscriptionKey));
    }
}
