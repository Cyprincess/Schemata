# Push

The push subsystem is a broadcast fan-out delivery layer. A single `SendAsync` call hands one
`PushContext` to every registered `IPushTransport` concurrently; each transport inspects the target
and its own subscription state to decide whether it delivers or skips. Results stream back in
completion order. Push owns no transport itself — it defines the contracts and the dispatch
mechanics, and transport packages (FCM, SignalR, SMTP, …) plug in. A `SchemataPushSubscription`
addressing table maps an owner to a transport endpoint.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Push.Skeleton` | `IPushService.cs`, `IPushTransport.cs`, `PushContext.cs`, `PushTarget.cs`, `PushOptions.cs`, `PushPriority.cs`, `TransportResult.cs`, `TransportStatus.cs`, `Advisors/IPushSendAdvisor.cs`, `IPushSubscriptionManager.cs`, `Entities/SchemataPushSubscription.cs` |
| `Schemata.Push.Foundation` | `DefaultPushService.cs`, `DefaultPushSubscriptionManager.cs`, `Features/SchemataPushFeature.cs`, `Builders/SchemataPushBuilder.cs`, `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Push.Scheduling` | `Features/SchemataPushSchedulingFeature.cs`, `Internal/PushDispatchJob.cs`, `Internal/SchedulingPushService.cs`, `Extensions/PushSchedulingBuilderExtensions.cs` |

## Startup

`UsePush()` on `SchemataBuilder` activates
`Schemata.Push.Foundation.Features.SchemataPushFeature` (Priority `Orders.Extension + 100_000_000` =
500,000,000) and returns a `SchemataPushBuilder`:

```csharp
builder.UseSchemata(schema => {
    schema.UsePush()
          .AddTransport<SignalRPushTransport>()
          .AddTransport<FcmPushTransport>();
});
```

`SchemataPushFeature.ConfigureServices` registers:

1. `DefaultPushService` as `IPushService` (scoped, `TryAdd`).
2. `DefaultPushSubscriptionManager` as `IPushSubscriptionManager` (scoped, `TryAdd`).
3. `SchemataPushSubscription` as a resource through `SchemataResourceBuilder.Use<…>()`. A resource
   transport activated by the host (`MapHttp()` / `MapGrpc()`) exposes the standard endpoints.

The push service is scoped so per-request DI (including tenant-bound services) flows into transports.

## SchemataPushBuilder

`Schemata.Push.Foundation.Builders.SchemataPushBuilder` contributes transports:

| Member | Effect |
| --- | --- |
| `AddTransport<TTransport>()` | Appends `TTransport` to the `IPushTransport` collection (scoped, `TryAddEnumerable`). |
| `AddFeature<T>()` | Adds a feature to the Schemata configuration. |

Transports are registered as an enumerable collection so `DefaultPushService` can resolve and fan
out to all of them. Each transport identifies itself through `IPushTransport.Name`, which doubles as
the `provider` it stores subscriptions under.

## IPushService

```csharp
public interface IPushService
{
    IAsyncEnumerable<TransportResult> SendAsync(PushContext context, CancellationToken ct = default);

    ValueTask<Operation> ScheduleSendAsync(
        PushContext       context,
        DateTimeOffset?   at = null,
        CancellationToken ct = default);
}
```

`SendAsync` runs the `IPushSendAdvisor` pipeline, then fans out to every transport and yields each
`TransportResult` as its transport completes.

`ScheduleSendAsync` defers delivery to a durable long-running operation. The base implementation
throws `NotSupportedException`; activate it with the Push Scheduling bridge:

```csharp
schema.UsePush(p => p.AddTransport<SignalRPushTransport>()
                     .UseScheduling());
```

`UseScheduling()` on `SchemataPushBuilder` adds `SchemataPushSchedulingFeature` (which auto-activates
the Scheduling feature) and replaces `IPushService` with `SchedulingPushService`. A scheduled send
serializes the `PushContext`, triggers a `PushDispatchJob` through `IScheduler`, and returns the
pending `operations/{operation}` envelope — so the deferred dispatch is managed and observed through
the standard LRO surface (`get` / `list` / `:cancel` / `:wait`) exposed by `Schemata.Scheduling.Http`
or `.Grpc`. A `null` `at` runs as soon as possible; a future `at` defers to a future-dated operation.
Immediate `SendAsync` still delegates to the broadcast fan-out service.

## IPushTransport

```csharp
public interface IPushTransport
{
    string Name { get; }

    ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default);
}
```

A transport reports `TransportStatus.Skipped` for a target it does not handle rather than throwing.
A thrown exception is isolated by the push service and surfaced as `TransportStatus.Failed`. Push
ships no transport implementations; provide one by implementing this contract.

## Targets

`PushTarget` is an abstract record with five built-in shapes. The polymorphic annotation
(`[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]`) lets a target round-trip through JSON
for the durable scheduled path and any wire exposure.

| Target | Carries | Typical responder |
| --- | --- | --- |
| `ChannelTarget(string Channel)` | a channel id | channel-aware transports (group/room) |
| `RecipientTarget(string Subject)` | a recipient canonical name | transports that resolve a subscription |
| `TopicTarget(string Topic)` | a topic id | publish/subscribe transports |
| `BroadcastTarget()` | nothing | connection transports delivering to all |
| `CustomTarget(string Kind, …)` | a kind + parameters | transports matching `Kind` |

## Subscriptions

`SchemataPushSubscription` is the addressing table, modeled after ASP.NET Core Identity
`AspNetUserLogins`. A subscription binds an owner canonical name to a transport endpoint and is unique
by `(owner, provider, providerKey)`. The owner is a free-form canonical name (`users/{x}`,
`groups/{x}`, `tags/{x}`, …), so the same table addresses any principal. `IPushSubscriptionManager`
and `DefaultPushSubscriptionManager` manage the rows. See [Subscriptions](subscriptions.md).

## Feature priority table

| Feature | Priority |
| --- | --- |
| `SchemataPushFeature` | 500,000,000 |
| `SchemataPushSchedulingFeature` | 500,400,000 |

## Extension points

- Implement `IPushTransport` to add a delivery backend, then register it with
  `AddTransport<T>()`.
- Implement `IPushSendAdvisor` (`TryAddEnumerable`) to filter, enrich, rate-limit, audit, or block a
  dispatch before fan-out.
- Provide an `IOwnerResolver<SchemataPushSubscription>` to scope subscriptions by the current
  principal when the repository's ownership advisors are enabled.

## Caveats

- Push ships no transport implementations. `SendAsync` with no registered transport yields nothing.
- `DefaultPushSubscriptionManager` resolves `IRepository<SchemataPushSubscription>`. Configure a
  persistence provider (EF Core or LinqToDB) or the manager cannot read or write rows.

## See also

- [Dispatch](dispatch.md) — fan-out, self-filtering, streaming order, isolation, the advisor pipeline
- [Subscriptions](subscriptions.md) — `SchemataPushSubscription`, the manager, ownership
