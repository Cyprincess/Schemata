# Push

Push fans one `PushContext` out to every registered `IPushTransport`. Each transport decides whether it owns the target and returns a `TransportResult`; `IPushService.SendAsync` yields the collected results.

## Packages

| Package | Role |
| --- | --- |
| `Schemata.Push.Skeleton` | Push contracts, targets, transports, results, subscriptions, and request envelopes |
| `Schemata.Push.Foundation` | Push service, subscription manager, builder, feature, and request handler |
| `Schemata.Push.Scheduling` | Deferred push delivery through Scheduling |

## Startup

```csharp
builder.UseSchemata(schema => {
    schema.UsePush()
          .AddTransport<SignalRPushTransport>()
          .AddTransport<FcmPushTransport>();
});
```

The feature registers `IPushService`, `IPushSubscriptionManager`, and the `SchemataPushSubscription` resource. An active Resource HTTP or gRPC transport exposes the subscription resource.

## Dispatch

`DefaultPushService` creates `SendPushRequest` and sends it through `IRequestDispatcher`. A registered `IRequestPipelineAdvisor<SendPushRequest,ImmutableArray<TransportResult>>` wraps fan-out. A wrap can short-circuit by returning an empty immutable array without calling its continuation. `SendPushHandler` invokes all resolved transports and collects one result per transport.

Implement a closed request-pipeline advisor for request-wide Push gates, rate limiting, auditing, or response shaping. Register it through `TryAddEnumerable`.

## Scheduled delivery

`UseScheduling()` on `SchemataPushBuilder` activates the Push Scheduling bridge. It serializes a context, triggers a Scheduling job, and returns a durable operation envelope. Immediate sends still dispatch through the Push request pipeline.

## See also

- [Dispatch](dispatch.md)
- [Subscriptions](subscriptions.md)
- [Scheduling](../scheduling/overview.md)
