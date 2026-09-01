# Dispatch

`IPushService.SendAsync` dispatches `SendPushRequest` through `IRequestDispatcher`. Registered `IRequestPipelineAdvisor<SendPushRequest,ImmutableArray<TransportResult>>` wraps run before `SendPushHandler`; the handler fans out to every registered `IPushTransport`.

## Wrap advisor

A push wrap can inspect `request.Context`, call the continuation, return an empty array without calling it, or reshape the result after fan-out.

```csharp
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Push.Skeleton;

public sealed class RateLimitPushAdvisor
    : IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>
{
    public int Order => 0;

    public Task<ImmutableArray<TransportResult>> AdviseAsync(
        AdviceContext ctx,
        SendPushRequest request,
        RequestHandlerContinuation<ImmutableArray<TransportResult>> next,
        CancellationToken ct = default) {
        return next(ct);
    }
}
```

Register the closed advisor with `TryAddEnumerable`. The dispatcher sorts wraps by `Order`; the handler resolves only when the chain reaches its continuation.

## Fan-out

`SendPushHandler` resolves every `IPushTransport`, invokes each transport for the same `PushContext`, and collects results by completion. A transport reports `Skipped` for a target it does not own. A transport exception becomes a `Failed` result while the remaining transports continue.

The `IPushService` facade yields the collected result batch. The individual results preserve completion order within that batch.

## See also

- [Push overview](overview.md)
- [Subscriptions](subscriptions.md)
- [Messaging](../messaging/overview.md)
