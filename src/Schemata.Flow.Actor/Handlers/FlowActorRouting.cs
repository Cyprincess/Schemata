using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Flow.Foundation;

namespace Schemata.Flow.Actor.Handlers;

internal sealed class FlowActorRouting<TRequest> : IActorRouting<TRequest>
    where TRequest : IProcessScoped
{
    public ValueTask<ActorId?> ResolveAsync(TRequest request, IServiceProvider services, CancellationToken ct) {
        return ValueTask.FromResult<ActorId?>(new ActorId("flow", request.ProcessCanonicalName));
    }
}
