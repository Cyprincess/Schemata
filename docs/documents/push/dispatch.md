# Dispatch

`DefaultPushService.SendAsync` drives one dispatch through the advisor pipeline and then fans it out
to every registered transport.

## Pipeline order

`SendAsync` is an async iterator (`Schemata.Push.Foundation.DefaultPushService.cs`) that runs two
stages:

1. **Advisor stage.** A fresh `AdviceContext` is built over the service provider, and
   `Advisor.For<IPushSendAdvisor>().RunAsync(context, …)` runs the registered advisors in ascending
   `Order`. The first advisor that returns a result other than `AdviseResult.Continue` short-circuits
   the dispatch: `SendAsync` yields nothing and no transport runs.
2. **Fan-out stage.** Every `IPushTransport` resolved from DI is invoked concurrently. Results stream
   back as each transport completes.

```csharp
var adviceContext = new AdviceContext(_services);
var advice        = await Advisor.For<IPushSendAdvisor>().RunAsync(adviceContext, context, ct);
if (advice is not AdviseResult.Continue) {
    yield break;
}

var pending = _services.GetServices<IPushTransport>()
                       .Select(transport => InvokeAsync(transport, context, ct))
                       .ToList();

while (pending.Count > 0) {
    var finished = await Task.WhenAny(pending);
    pending.Remove(finished);
    yield return await finished;
}
```

## Self-filtering

The push service does not route. It hands the same `PushContext` to every transport, and each
transport decides whether the target is its concern. A transport that does not handle the target
returns `TransportResult.Skipped(Name)`; one that delivers returns `TransportResult.Sent(...)`. The
caller sees one result per transport and reads the `Status` to learn what each did.

Typical responses by target:

| Target | A transport that owns this target | Other transports |
| --- | --- | --- |
| `RecipientTarget` | resolves a `SchemataPushSubscription` (or its own hub state) and sends | `Skipped` |
| `ChannelTarget` | a channel-aware transport sends to the group | `Skipped` |
| `TopicTarget` | a pub/sub transport sends to the topic | `Skipped` |
| `BroadcastTarget` | a connection transport sends to all clients | `Skipped` |
| `CustomTarget` | a transport whose `Kind` matches sends | `Skipped` |

Filtering is transport-defined, so multiple transports can claim the same target (a
`BroadcastTarget` delivered by both SignalR and a websocket gateway, for example).

## Streaming order

`SendAsync` returns `IAsyncEnumerable<TransportResult>` and yields results in **completion order**.
The fan-out loop awaits `Task.WhenAny` over the outstanding transport tasks, so each result reaches
the caller as soon as its transport finishes.

```csharp
await foreach (var result in push.SendAsync(context, ct))
{
    // results arrive in completion order, regardless of registration order
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

| Field | Meaning |
| --- | --- |
| `Transport` | the reporting transport's `Name` |
| `Status` | `Sent`, `Skipped`, `Failed`, or `Unspecified` |
| `Address` | the obfuscated delivery address, when the transport reports one |
| `ProviderRef` | the backend message reference, when the transport reports one |
| `Error` | the failure reason when `Status` is `Failed` |

The `Sent`, `Skipped`, and `Failed` factory methods construct the common shapes.

## Delivery guarantee

`SendAsync` is at-most-once: a transport failure is reported to the caller as a `Failed` result and
is not retried.

## See also

- [Overview](overview.md) — packages, startup, and the builder
- [Subscriptions](subscriptions.md) — how a `RecipientTarget` resolves an endpoint
- [Advice Pipeline](../core/advice-pipeline.md) — how `Advisor.For<T>().RunAsync` resolves and orders
