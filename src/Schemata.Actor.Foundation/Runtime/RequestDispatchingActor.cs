using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     Built-in stateless <see cref="IActor" /> that serializes calls to a keyed default
///     <see cref="IRequestHandler{TRequest,TResponse}" /> through per-instance mailbox delivery.
///     A bridge module registers it once per route (e.g. <c>Register&lt;RequestDispatchingActor&gt;("flow", FlowConstants.Handlers.Default)</c>)
///     and never needs to author its own <see cref="IActor" />.
/// </summary>
/// <param name="handlerKey">
///     The keyed-service key the handler was registered under, carried in through
///     <see cref="Props.Args" /> at the site that registers this actor type.
/// </param>
public sealed class RequestDispatchingActor(object handlerKey) : IActor
{
    private static readonly MethodInfo DispatchMethod =
        typeof(RequestDispatchingActor).GetMethod(nameof(DispatchAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    #region IActor Members

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        var requestType  = envelope.Payload.GetType();
        var responseType = ResolveResponseType(requestType);
        var dispatch     = DispatchMethod.MakeGenericMethod(requestType, responseType);

        // An uncaught exception here is left to the shared turn dispatcher: it faults this turn's
        // Ask with the original exception either way, and restarting this stateless actor (its
        // default OnFailedAsync disposition below) is a harmless no-op that keeps the mailbox and
        // pending-reply table intact for whatever is still queued.
        await (Task)dispatch.Invoke(null, [ctx, handlerKey, envelope.Payload, ctx.Stopping])!;
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    /// <summary>Always restarts: this actor holds no per-instance state, so discarding and rebuilding it is a safe, cheap no-op that keeps the mailbox intact for whatever is still queued.</summary>
    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);

    #endregion

    private static async Task DispatchAsync<TRequest, TResponse>(IActorContext ctx, object key, TRequest request, CancellationToken ct)
        where TRequest : IRequest<TResponse> {
        var handler  = ctx.Services.GetRequiredKeyedService<IRequestHandler<TRequest, TResponse>>(key);
        var response = await handler.HandleAsync(request, ct);
        await ctx.ReplyAsync(response, ct);
    }

    private static Type ResolveResponseType(Type requestType) {
        var requestInterface = requestType.GetInterfaces()
                                           .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

        if (requestInterface is null) {
            throw new InvalidOperationException($"'{requestType}' does not implement {typeof(IRequest<>)}.");
        }

        return requestInterface.GetGenericArguments()[0];
    }
}
