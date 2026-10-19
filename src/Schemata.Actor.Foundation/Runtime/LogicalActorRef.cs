using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

internal sealed class LogicalActorRef<TActor>(InProcessActorSystem<TActor> system, ActorId id, Props props, MessageContext captured) : IActorRef
    where TActor : SchemataActor, new()
{
    public ActorId Id { get; } = id;
    public async ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default) where T : IMessage {
        var activation = await system.ResolveForSendAsync(Id, props, ct, context ?? captured);
        await activation.TellAsync(message, context ?? captured, ct);
    }
    public async ValueTask<TResponse> AskAsync<TRequest, TResponse>(TRequest request, MessageContext? context = null,
        TimeSpan? timeout = null, CancellationToken ct = default) where TRequest : IRequest<TResponse> {
        ActorTurnIdentity.RejectSelfAsk(Id, system);
        if (timeout is null) {
            var activation = await system.ResolveForSendAsync(Id, props, ct, context ?? captured);
            return await activation.AskAsync<TRequest, TResponse>(request, context ?? captured, null, ct);
        }
        using var deadline = new CancellationTokenSource(timeout.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try {
            var activation = await system.ResolveForSendAsync(Id, props, linked.Token, context ?? captured);
            return await activation.AskAsync<TRequest, TResponse>(request, context ?? captured, null, linked.Token);
        } catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested) {
            throw new TimeoutException($"Actor '{Id}' did not reply before the timeout.");
        }
    }
}
