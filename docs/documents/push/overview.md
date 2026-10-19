# Push

Push fans one `PushContext` out to every registered `IPushTransport`. Each transport decides whether it owns the target and returns a `TransportResult`; `IPushService.SendAsync` yields the collected results.

## Packages

| Package | Role |
| --- | --- |
| `Schemata.Push.Skeleton` | Push contracts, targets, transports, results, subscriptions, and shared control requests/policies |
| `Schemata.Push.Http` | HTTP binding for subscription and send control requests |
| `Schemata.Push.Grpc` | gRPC binding for subscription and send control requests |
| `Schemata.Push.Foundation` | Push service, subscription manager, builder, feature, and shared control handler |
| `Schemata.Push.Scheduling` | Deferred push delivery through Scheduling |
| `Schemata.Push.WebPush` | Web Push delivery transport provider |

## Startup

```csharp
builder.UseSchemata(schema => {
    schema.UsePush()
          .AddTransport<SignalRPushTransport>()
          .AddTransport<FcmPushTransport>();
});
```

The feature registers `IPushService`, `IPushSubscriptionManager`, and the `SchemataPushSubscription` resource. An active Resource HTTP or gRPC transport exposes the subscription resource.
`AddSchemataPushHttp()` and `AddSchemataPushGrpc()` expose the owner-derived subscription and send
control endpoints over HTTP and gRPC; see [Control plane](control-plane.md).

Provider packages install transports through the same builder. `Schemata.Push.WebPush` adds a Web
Push transport (RFC 8030 delivery, RFC 8291 message encryption, RFC 8292 VAPID) with `.AddWebPush()`;
it fans out alongside every transport registered through `AddTransport<T>()`.

Reference `Schemata.Push.WebPush` in the host and configure its provider explicitly:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

builder.UseSchemata(schema => {
    schema.UsePush().AddWebPush(options => {
        options.Subject = builder.Configuration["Push:WebPush:Subject"];
        options.VapidPublicKey = builder.Configuration["Push:WebPush:VapidPublicKey"];
        options.VapidPrivateKey = builder.Configuration["Push:WebPush:VapidPrivateKey"];
    });
});
```

`WebPushOptions`, `WebPushTransport`, and `WebPushConstants` belong to the
`Schemata.Push.WebPush` namespace. Subscription provider identity remains `webpush`; the
provider reads the endpoint from `ProviderKey` and Push API keys from the `p256dh` and `auth`
metadata entries. Other providers implement `IPushTransport` and supply their own addressing
and configuration.

The control handler owns action authorization, subscription owner resolution, required input
validation, target/default options, and secret-free subscription results. Local management callers
dispatch the same requests with a trusted principal. Application-internal `IPushService` and
`IPushSubscriptionManager` remain the entry points for background dispatch and explicit-owner
subscription work.

Control messages retain arbitrary JSON values, including null. Missing priority uses Normal;
explicit Low remains Low. See [Control plane](control-plane.md) for binding and duration contracts.

## Dispatch

`DefaultPushService` creates `SendPushRequest` and sends it through `IRequestDispatcher`. A registered `IRequestPipelineAdvisor<SendPushRequest,ImmutableArray<TransportResult>>` wraps fan-out. A wrap can short-circuit by returning an empty immutable array without calling its continuation. `SendPushHandler` invokes all resolved transports and collects one result per transport.

Implement a closed request-pipeline advisor for request-wide Push gates, rate limiting, auditing, or response shaping. Register it through `TryAddEnumerable`.

## Scheduled delivery

`UseScheduling()` on `SchemataPushBuilder` activates the Push Scheduling bridge. It serializes a context, triggers a Scheduling job, and returns a durable operation envelope. Immediate sends still dispatch through the Push request pipeline.

## See also

- [Dispatch](dispatch.md)
- [Subscriptions](subscriptions.md)
- [Scheduling](../scheduling/overview.md)
