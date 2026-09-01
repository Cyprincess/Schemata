using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Actor.Internal;

/// <summary>
///     Replaces the unkeyed default handler for a process-scoped Flow command, redirecting the
///     call to the target process's per-instance actor so concurrent writers to the same
///     <see cref="Schemata.Flow.Skeleton.Entities.SchemataProcess" /> serialize instead of racing
///     on its optimistic-concurrency token.
/// </summary>
/// <remarks>
///     Constructed with only <see cref="IActorSystem" /> and the caller's <see cref="IServiceProvider" />
///     — it never injects the keyed inner handler and never holds a caller-scoped object across the
///     mailbox boundary. <paramref name="caller" /> is read exactly once, synchronously, before the
///     request is enqueued, to capture the ambient <see cref="MessageContext" />; everything that
///     crosses into the actor's turn afterward is that flattened dictionary plus the request itself
///     (§5.9). The turn dispatcher rebuilds a fresh scope and resolves the keyed default handler
///     there — this type never touches it.
/// </remarks>
/// <typeparam name="TRequest">The process-scoped command type.</typeparam>
/// <typeparam name="TResult">The command's result type.</typeparam>
/// <param name="actors">The actor system used to resolve the target process's actor.</param>
/// <param name="caller">The calling scope's service provider, used only to capture context.</param>
internal sealed class ActorSerializingHandler<TRequest, TResult>(
    IActorSystem actors, IServiceProvider caller) : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>, IProcessScoped
{
    public async Task<TResult> HandleAsync(TRequest request, CancellationToken ct = default) {
        var context = MessageContexts.Capture(caller);
        var actor   = await actors.GetAsync(new ActorId("flow", request.ProcessCanonicalName));
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}
