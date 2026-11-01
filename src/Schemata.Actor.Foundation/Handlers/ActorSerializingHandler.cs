using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Handlers;

/// <summary>
///     Serializes a routed request through its per-instance actor. A request without an instance
///     identity resolves the component's keyed default handler directly.
/// </summary>
/// <typeparam name="TRequest">The routed request type.</typeparam>
/// <typeparam name="TResult">The request's result type.</typeparam>
public sealed class ActorSerializingHandler<TRequest, TResult>(
    IActorSystem                actors,
    IServiceProvider            caller,
    IActorRouting<TRequest>     routing,
    string                      defaultHandlerKey
) : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, AdviceContext advice, CancellationToken ct = default) {
        var actorId = await routing.ResolveAsync(request, caller, ct);
        if (actorId is null) {
            return await caller.GetRequiredKeyedService<IRequestHandler<TRequest, TResult>>(defaultHandlerKey)
                               .HandleAsync(request, advice, ct);
        }

        var context = MessageContexts.Capture(caller);
        var actor   = await actors.GetAsync(actorId.Value);
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}
