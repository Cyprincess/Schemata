# Dispatch

`IPushService.SendAsync` dispatches `SendPushRequest` through `IRequestDispatcher`. Registered `IRequestPipelineAdvisor<SendPushRequest,ImmutableArray<TransportResult>>` wraps run before `SendPushHandler`; the handler fans out to every registered `IPushTransport`.

The authenticated management boundary is `PushControlHandler`. HTTP, gRPC, and local management
callers submit the shared `SendPushControlRequest` from `Schemata.Push.Skeleton.Control`. The
handler authorizes `PushPolicies.Send`, validates the JSON message and target, applies delivery
defaults, and dispatches one `SendPushRequest` through the existing pipeline. A missing message
is rejected; explicit JSON null is a payload. Missing target means broadcast; an unknown kind or
an empty selected target field is rejected before any transport runs.

`IPushService` remains the application-internal entry point for background and scheduled sends.
Its caller supplies a `PushContext`; the management policies apply to the control requests.

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

## Control payload and options

The control message stays a JSON value. Nested `data` remains nested, including keys named
`title` or `body`; arrays, numbers, booleans, strings, objects, and null reach transports unchanged.
`PushControlOptions` preserves presence: missing options or missing priority use
`PushOptions.Default.Priority` (`Normal`), while explicit `Low` stays `Low`. `TimeToLive` is a
nullable `TimeSpan`, including negative values and fractional seconds at tick precision.

Sources: `src/Schemata.Push.Skeleton/Control/PushControlRequests.cs`,
`src/Schemata.Push.Foundation/Handlers/PushControlHandler.cs`,
`src/Schemata.Push.Foundation/Handlers/SendPushHandler.cs`.

## See also

- [Push overview](overview.md)
- [Subscriptions](subscriptions.md)
- [Messaging](../messaging/overview.md)
