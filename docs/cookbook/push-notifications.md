# Push Notifications

## What you'll build

A working push setup with a custom transport. You will implement an `IPushTransport` that delivers to
endpoints stored in the `SchemataPushSubscription` table, register it with `UsePush`, record a
subscription through `IPushSubscriptionManager`, and broadcast a notification to a recipient. Then you
will add a send advisor that blocks dispatches and switch to durable scheduled delivery.

Push owns no transport, so the transport is yours to write. The push service fans a single
`PushContext` out to every registered transport; each transport decides whether the target is its
concern.

## Prerequisites

- The `Student` entity and CRUD setup from [guides/getting-started.md](../guides/getting-started.md).
- A configured persistence provider for the `SchemataPushSubscription` table.
- Familiarity with the advisor pipeline from [documents/core/advice-pipeline.md](../documents/core/advice-pipeline.md).

## Step 1: Implement a transport

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Push.Skeleton;

public sealed class ConsolePushTransport : IPushTransport
{
    private readonly IPushSubscriptionManager _subscriptions;

    public ConsolePushTransport(IPushSubscriptionManager subscriptions) { _subscriptions = subscriptions; }

    public string Name => "console";

    public async ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default)
    {
        // This transport only handles recipient targets it has a subscription for.
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

A transport returns `Skipped` for a target it does not own rather than throwing. It identifies itself
through `Name`, which is also the `provider` it stores subscriptions under.

**Assertion:** `ConsolePushTransport` compiles and implements `IPushTransport`.

## Step 2: Register push and the transport

```csharp
builder.UseSchemata(schema => {
    schema.UsePush().AddTransport<ConsolePushTransport>();
});
```

`AddTransport<T>()` appends the transport to the `IPushTransport` collection. `UsePush` registers
`DefaultPushService` as `IPushService`, `DefaultPushSubscriptionManager` as
`IPushSubscriptionManager`, and `SchemataPushSubscription` as a resource.

**Assertion:** the application starts and `IEnumerable<IPushTransport>` resolves with one entry whose
`Name` is `"console"`.

## Step 3: Record a subscription

```csharp
public sealed class EnrollmentNotifier(IPushSubscriptionManager subscriptions, IPushService push)
{
    public async Task SubscribeAsync(string userId, string deviceKey, CancellationToken ct)
    {
        await subscriptions.AddAsync(
            owner:       $"users/{userId}",
            provider:    "console",
            providerKey: deviceKey,
            metadata:    null,
            ct:          ct);
    }
}
```

`AddAsync` is idempotent on `(owner, provider, providerKey)`: a second call with the same triple
returns the existing row without inserting a duplicate. The owner is a free-form canonical name, so
`groups/{id}` or `tags/{id}` work the same way.

**Assertion:** calling `SubscribeAsync("chino", "desk-1", ct)` twice leaves exactly one row in
`SchemataPushSubscriptions`.

## Step 4: Send a notification

```csharp
public async Task NotifyAsync(string userId, string body, CancellationToken ct)
{
    var context = new PushContext(body, new RecipientTarget($"users/{userId}"));

    await foreach (var result in push.SendAsync(context, ct))
    {
        Console.WriteLine($"{result.Transport}: {result.Status}");
    }
}
```

`SendAsync` runs the advisor pipeline, fans out to every transport, and yields one
`TransportResult` per transport in completion order. The console transport sends to the subscribed
device and reports `Sent`; a transport with no matching subscription reports `Skipped`.

**Assertion:** `NotifyAsync("chino", "Welcome", ct)` prints the delivery line and a
`console: Sent` result.

## Step 5: Block a dispatch with an advisor

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Advisors;

public sealed class QuietHoursPushAdvisor : IPushSendAdvisor
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, PushContext context, CancellationToken ct = default)
    {
        var hour = DateTime.UtcNow.Hour;
        return Task.FromResult(hour is >= 22 or < 7 ? AdviseResult.Block : AdviseResult.Continue);
    }
}
```

Register it with `TryAddEnumerable`:

```csharp
schema.ConfigureServices(services => {
    services.TryAddEnumerable(ServiceDescriptor.Scoped<IPushSendAdvisor, QuietHoursPushAdvisor>());
});
```

`AdviseResult.Block` aborts the dispatch before any transport runs, so `SendAsync` yields nothing
during quiet hours.

**Assertion:** during 22:00–07:00 UTC, `SendAsync` produces no results and the console prints nothing.

## Common pitfalls

**Throwing from a transport.** A transport must return `TransportResult.Skipped(Name)` for a target it
does not handle. A thrown exception is caught and reported as `Failed` with the message, but returning
`Skipped` is the intended signal for "not mine".

**Expecting routing from the push service.** `SendAsync` does not route; it hands the same context to
every transport. If no transport claims the target, every result is `Skipped` and nothing is
delivered. Routing lives in each transport's `TrySendAsync`.

**Registering a transport as a singleton that depends on a scoped service.** `IPushService` and
`IPushSubscriptionManager` are scoped. A transport that injects the manager must be scoped too;
`AddTransport<T>()` registers it scoped for that reason.

**Passing a future `at`.** The durable backend dispatches immediately; a future `at` throws
`NotSupportedException`. Pass `null` for immediate durable dispatch.

## See also

- [guides/push.md](../guides/push.md) — the step-by-step introduction
- [documents/push/dispatch.md](../documents/push/dispatch.md) — fan-out, streaming, isolation
- [documents/push/subscriptions.md](../documents/push/subscriptions.md) — the addressing table and manager
- [cookbook/ownership-and-row-acl.md](ownership-and-row-acl.md) — scoping subscriptions by owner
