using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation;

namespace Schemata.Push.Actor.Handlers;

/// <summary>
///     Replaces the unkeyed default handler for a subscription-scoped Push command, redirecting
///     the call to a per-identity actor so concurrent writers to the same subscription triple
///     serialize instead of racing inside the repository.
/// </summary>
/// <remarks>
///     Mirrors Flow.Actor's handler: constructed with only <see cref="IActorSystem" /> and the
///     caller's <see cref="IServiceProvider" /> — it never injects the keyed inner handler. The
///     caller's provider is read exactly once, synchronously, to capture the ambient
///     <see cref="MessageContext" />; only the flattened dictionary plus the request cross the
///     mailbox boundary. The turn dispatcher rebuilds a fresh scope and resolves the keyed default
///     handler there.
/// </remarks>
/// <typeparam name="TRequest">The subscription-scoped command type.</typeparam>
/// <typeparam name="TResult">The command's result type.</typeparam>
internal sealed class ActorSerializingHandler<TRequest, TResult>(
    IActorSystem actors, IServiceProvider caller) : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>, ISubscriptionScoped
{
    public async Task<TResult> HandleAsync(TRequest request, CancellationToken ct = default) {
        var context = MessageContexts.Capture(caller);
        var actor   = await actors.GetAsync(new("push", request.SubscriptionKey));
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}