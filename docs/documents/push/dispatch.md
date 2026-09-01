# Dispatch

`IPushService.SendAsync` (`DefaultPushService`) dispatches a `SendPushRequest` through
`IRequestDispatcher`; the `SendPushHandler` runs the advisor pipeline and then fans the request out
to every registered transport.

## Pipeline order

`SendPushHandler` (`Schemata.Push.Foundation.Handlers.SendPushHandler.cs`) runs two stages and
returns the collected results as an `ImmutableArray<TransportResult>`. `DefaultPushService.SendAsync`
awaits that whole array and re-yields it to keep the facade's `IAsyncEnumerable` signature:

1. **Advisor stage.** The ambient `AdviceContext` established for the dispatch runs the registered
   `IPushSendAdvisor` chain in ascending `Order`. The first advisor that returns a result other than
   `AdviseResult.Continue` short-circuits the dispatch: the handler returns an empty array and no
   transport runs.
2. **Fan-out stage.** Every `IPushTransport` resolved from DI is invoked concurrently. The handler
   awaits every transport before returning, collecting each result into the array as its transport
   finishes.

```csharp
var ctx    = AdviceContext.Require();
var advice = await Advisor.For<IPushSendAdvisor>().RunAsync(ctx, request.Context, ct);
if (advice is not AdviseResult.Continue) {
    return [];
}

var pending = services.GetServices<IPushTransport>()
                      .Select(transport => InvokeAsync(transport, request.Context, ct))
                      .ToList();
var results = ImmutableArray.CreateBuilder<TransportResult>(pending.Count);
while (pending.Count > 0) {
    var finished = await Task.WhenAny(pending);
    pending.Remove(finished);
    results.Add(await finished);
}

return results.MoveToImmutable();
```

## Self-filtering

The push service does not route. It hands the same `PushContext` to every transport, and each
transport decides whether the target is its concern. A transport that does not handle the target
returns `TransportResult.Skipped(Name)`; one that delivers returns `TransportResult.Sent(...)`. The
caller sees one result per transport and reads the `Status` to learn what each did.

Typical responses by target:

| Target            | A transport that owns this target                                      | Other transports |
| ----------------- | ---------------------------------------------------------------------- | ---------------- |
| `RecipientTarget` | resolves a `SchemataPushSubscription` (or its own hub state) and sends | `Skipped`        |
| `ChannelTarget`   | a channel-aware transport sends to the group                           | `Skipped`        |
| `TopicTarget`     | a pub/sub transport sends to the topic                                 | `Skipped`        |
| `BroadcastTarget` | a connection transport sends to all clients                            | `Skipped`        |
| `CustomTarget`    | a transport whose `Kind` matches sends                                 | `Skipped`        |

Filtering is transport-defined, so multiple transports can claim the same target (a
`BroadcastTarget` delivered by both SignalR and a websocket gateway, for example).

## Result ordering

`SendAsync` returns `IAsyncEnumerable<TransportResult>` and keeps that signature, but the facade
now awaits the whole dispatch before yielding: the caller observes every transport's outcome
together, not one per completion. The results inside the set are ordered by completion — the handler
still uses `Task.WhenAny` to collect them — but they arrive as one batch. The observable difference
from the former per-completion streaming is the time the first result reaches the caller: it is now
after the slowest transport, not the fastest.

```csharp
await foreach (var result in push.SendAsync(context, ct))
{
    // the whole set arrives together; results are ordered by completion within it
}
```

## Isolation

Each transport runs inside `InvokeAsync`, which catches any exception the transport throws and
converts it to a `TransportStatus.Failed` result carrying the exception message:

```csharp
private static async Task<TransportResult> InvokeAsync(
    IPushTransport transport, PushContext context, CancellationToken ct)
{
    try
    {
        return await transport.TrySendAsync(context, ct);
    }
    catch (Exception ex)
    {
        return TransportResult.Failed(transport.Name, ex.Message);
    }
}
```

A transport that throws does not abort the dispatch. The other transports still run, and the caller
receives a `Failed` result for the broken transport alongside the `Sent` / `Skipped` results for the
rest. `TransportResult.Error` carries the exception message as a plain string; richer error metadata
is the transport's responsibility.

## IPushSendAdvisor

```csharp
public interface IPushSendAdvisor : IAdvisor<PushContext>;
```

The advisor receives the `PushContext` before fan-out. Returning `AdviseResult.Block` aborts the
dispatch; `AdviseResult.Continue` proceeds. Register advisors with `TryAddEnumerable` so they
accumulate, and order them with the `Order` property:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IPushSendAdvisor, RateLimitPushAdvisor>());
```

Use the advisor for cross-cutting concerns that gate or shape every dispatch: routing filters,
payload enrichment through `PushContext.Metadata`, rate limiting, and auditing. Per-transport
delivery decisions belong in the transport, not the advisor.

## TransportResult

```csharp
public sealed record TransportResult(
    string          Transport,
    TransportStatus Status,
    string?         Address     = null,
    string?         ProviderRef = null,
    string?         Error       = null);
```

| Field         | Meaning                                                         |
| ------------- | --------------------------------------------------------------- |
| `Transport`   | the reporting transport's `Name`                                |
| `Status`      | `Sent`, `Skipped`, `Failed`, or `Unspecified`                   |
| `Address`     | the obfuscated delivery address, when the transport reports one |
| `ProviderRef` | the backend message reference, when the transport reports one   |
| `Error`       | the failure reason when `Status` is `Failed`                    |

The `Sent`, `Skipped`, and `Failed` factory methods construct the common shapes.

## Delivery guarantee

`SendAsync` is at-most-once: a transport failure is reported to the caller as a `Failed` result and
is not retried.

## See also

- [Overview](overview.md) — packages, startup, and the builder
- [Subscriptions](subscriptions.md) — how a `RecipientTarget` resolves an endpoint
- [Advice Pipeline](../core/advice-pipeline.md) — how `Advisor.For<T>().RunAsync` resolves and orders
