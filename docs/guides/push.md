# Push

Add push notifications to the Student CRUD app: register a transport, record a subscription, and
broadcast a message to a recipient. This guide builds on [Getting Started](getting-started.md).

Push is a broadcast fan-out layer. One `SendAsync` call hands the same message to every registered
transport; each transport decides whether the target is its concern. Push ships no transport of its
own, so this guide implements a small console transport to see delivery end to end.

## Add the package

Push is not bundled in the meta-targets, so add `Schemata.Push.Foundation` directly:

```shell
dotnet add package --prerelease Schemata.Push.Foundation
```

## Enable push

`UsePush()` returns a `SchemataPushBuilder`. `SchemataPushFeature` runs at Priority 500,000,000.
Register transports on the returned builder:

```csharp
schema.UsePush().AddTransport<ConsolePushTransport>();
```

`AddTransport<T>()` appends the transport to the `IPushTransport` collection that the push service
fans out to. `UsePush` also registers `DefaultPushService` as `IPushService`,
`DefaultPushSubscriptionManager` as `IPushSubscriptionManager`, and `SchemataPushSubscription` as a
resource. The subscription table persists through `IRepository<SchemataPushSubscription>`, so the EF
Core setup from Getting Started must be configured.

## Implement a transport

Create `ConsolePushTransport.cs`. A transport implements `IPushTransport`:

```csharp
using Schemata.Push.Skeleton;

public sealed class ConsolePushTransport : IPushTransport
{
    private readonly IPushSubscriptionManager _subscriptions;

    public ConsolePushTransport(IPushSubscriptionManager subscriptions) { _subscriptions = subscriptions; }

    public string Name => "console";

    public async ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default)
    {
        if (context.Target is not RecipientTarget recipient)
        {
            return TransportResult.Skipped(Name);
        }

        var delivered = false;
        await foreach (var subscription in _subscriptions.GetForOwnerAsync(recipient.Subject, Name, ct))
        {
            Console.WriteLine($"[console:{subscription.ProviderKey}] {context.Message}");
            delivered = true;
        }

        return delivered ? TransportResult.Sent(Name) : TransportResult.Skipped(Name);
    }
}
```

`Name` identifies the transport and is the `provider` it stores subscriptions under. A transport
returns `TransportResult.Skipped(Name)` for a target it does not handle — it never throws to mean
"not mine". The push service catches any thrown exception and reports it as `Failed`, isolating the
failure from the other transports.

## Record a subscription

A `RecipientTarget` resolves to a delivery endpoint through the `SchemataPushSubscription` table.
Inject `IPushSubscriptionManager` and add a row:

```csharp
using Schemata.Push.Skeleton;

public sealed class SubscriptionService(IPushSubscriptionManager subscriptions)
{
    public ValueTask<SchemataPushSubscription> SubscribeAsync(string userId, string device, CancellationToken ct)
    {
        return subscriptions.AddAsync(
            owner:       $"users/{userId}",
            provider:    "console",
            providerKey: device,
            ct:          ct);
    }
}
```

`AddAsync` is idempotent on `(owner, provider, providerKey)`: calling it twice with the same triple
returns the existing row. The owner is a free-form canonical name, so `groups/{id}` and `tags/{id}`
address groups and tags through the same table.

## Send a notification

Inject `IPushService` and call `SendAsync`:

```csharp
using Schemata.Push.Skeleton;

public sealed class NotificationService(IPushService push)
{
    public async Task NotifyAsync(string userId, string body, CancellationToken ct)
    {
        var context = new PushContext(body, new RecipientTarget($"users/{userId}"));

        await foreach (var result in push.SendAsync(context, ct))
        {
            Console.WriteLine($"{result.Transport}: {result.Status}");
        }
    }
}
```

`SendAsync` runs the advisor pipeline, fans out to every transport concurrently, and yields one
`TransportResult` per transport in completion order. The console transport delivers to the subscribed
device and reports `Sent`; a transport with no matching subscription reports `Skipped`.

## Targets

`PushTarget` has five built-in shapes. Each transport decides which it handles:

| Target | Addresses |
| --- | --- |
| `RecipientTarget(subject)` | one recipient by canonical name |
| `ChannelTarget(channel)` | a named channel or group |
| `TopicTarget(topic)` | a publish/subscribe topic |
| `BroadcastTarget()` | every connection a transport holds |
| `CustomTarget(kind, params)` | transports matching `kind` |

## Verify

```shell
dotnet run
```

Wire `SubscriptionService.SubscribeAsync` and `NotificationService.NotifyAsync` into an endpoint or
the create pipeline, then subscribe and notify:

```text
[console:desk-1] Welcome, Alice
console: Sent
```

## Next steps

- [Modular](modular.md) — package the transport in its own module
- [Event Bus](event-bus.md) — publish a domain event that triggers a push send

## See also

- [Push Overview](../documents/push/overview.md) — `IPushService`, `IPushTransport`, targets
- [Push Notifications](../cookbook/push-notifications.md) — a transport, an advisor, and scheduled delivery
