using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

internal sealed class LogicalActorRef(InProcessActorSystem system, ActorId id, Props props) : IActorRef
{
    public ActorId Id { get; } = id;
    public async ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default) where T : IMessage {
        var activation = await system.ResolveForSendAsync(Id, props, ct);
        await activation.TellAsync(message, context, ct);
    }
    public async ValueTask<TResponse> AskAsync<TRequest, TResponse>(TRequest request, MessageContext? context = null,
        TimeSpan? timeout = null, CancellationToken ct = default) where TRequest : IRequest<TResponse> {
        ActorTurnIdentity.RejectSelfAsk(Id);
        if (timeout is null) {
            var activation = await system.ResolveForSendAsync(Id, props, ct);
            return await activation.AskAsync<TRequest, TResponse>(request, context, null, ct);
        }
        using var deadline = new CancellationTokenSource(timeout.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try {
            var activation = await system.ResolveForSendAsync(Id, props, linked.Token);
            return await activation.AskAsync<TRequest, TResponse>(request, context, null, linked.Token);
        } catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested) {
            throw new TimeoutException($"Actor '{Id}' did not reply before the timeout.");
        }
    }
}
