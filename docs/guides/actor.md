# Actor

Route every write to a given process or other actor-backed domain instance through one mailbox so
concurrent callers serialize instead of racing on an optimistic-concurrency token. This guide
only needs the Student application from [Getting Started](getting-started.md); the request
contracts it reuses come from [Messaging](messaging.md).

## Add the package

`Schemata.Actor.Foundation` is not part of any meta-target package — it is not pulled in
transitively by `Schemata.Application.Complex.Targets` the way [Messaging](messaging.md) is:

```shell
dotnet add package --prerelease Schemata.Actor.Foundation
```

Adding it also brings `Schemata.Actor.Skeleton`, a dependency of `Schemata.Actor.Foundation`: the
actor contracts (`IActor`, `ActorId`, `Props`, `IActorRef`) live there.

## The actor model

`IActor` is the behavior every actor instance implements. The hosting runtime invokes its four
callbacks serially, one turn at a time, for a single instance:

```csharp
using System;
using System.Threading.Tasks;

namespace Schemata.Actor.Skeleton;

public interface IActor
{
    ValueTask OnStartedAsync(IActorContext ctx);
    ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope);
    ValueTask OnStoppedAsync(IActorContext ctx);
    ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex);
}
```

`ActorId(string Type, string Key)` identifies an instance: `Type` is the route key resolved
through `IActorRegistry`, `Key` is unique within that type. `Props(Type ActorType, object[]? Args)`
is the recipe used to construct an instance. `IActorRef` is the handle every caller sends
through — `TellAsync` for fire-and-forget, `AskAsync<TRequest, TResponse>` for a reply.

Each actor gets a bounded mailbox channel — capacity from `SchemataActorOptions.MailboxCapacity`
(default `1024`) — and one background task draining it, one turn at a time. A write past capacity
blocks the writer instead of growing the queue or dropping a message: a burst of callers slows
down to the mailbox's own processing rate rather than piling up in memory. Every lifecycle
callback for one instance runs on that same drain loop, so `OnStarted`, every
`OnReceive`/`OnFailed`, and the final `OnStopped` never overlap.

## Define an actor and dispatch to it

Create `Increment.cs`. It reuses the `ICommand<TResult>` contract from
[Messaging](messaging.md) — any existing command or query is already a valid actor payload, since
`IMessage`/`IRequest<TResponse>` come from `Schemata.Messaging.Skeleton`:

```csharp
using Schemata.Messaging.Skeleton;

public sealed record Increment(int By) : ICommand<int>;
```

Create `CounterActor.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

public sealed class CounterActor : IActor
{
    private int _count;

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
    {
        if (envelope.Payload is Increment increment)
        {
            _count += increment.By;
            await ctx.ReplyAsync(_count);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}
```

Register the type and its route key in `Program.cs`:

```csharp
schema.UseActor(a => a.Register<CounterActor>("counter"));
```

Resolve `IActorSystem` and address an instance by `ActorId`. `GetAsync` spawns the instance on
first use; an unregistered `Type` throws rather than silently constructing something:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

public sealed class CounterService(IActorSystem actors)
{
    public async Task<int> IncrementAsync(string counterName, int by, CancellationToken ct = default)
    {
        var counter = await actors.GetAsync(new ActorId("counter", counterName));
        return await counter.AskAsync<Increment, int>(new Increment(by), ct: ct);
    }
}
```

Every `Increment` for the same `counterName` runs on the same mailbox, one turn at a time — two
concurrent callers never interleave their reads and writes of `_count`.

## Ask semantics

`AskAsync` generates a `CorrelationId`, enqueues the envelope, and awaits a
`TaskCompletionSource` registered in the actor's own pending-reply table, bounded by the optional
`timeout` and the caller's own `ct`. Inside the turn, `IActorContext.ReplyAsync`/`ReplyFaultAsync`
resolve that same table entry. A turn triggered by `TellAsync` has `CorrelationId == Guid.Empty`,
and both calls are no-ops there.

The reply commits only after the turn ends: it is recorded once `OnReceiveAsync` returns without
throwing, so a reply recorded earlier in the same turn is discarded if the turn subsequently
throws — the caller's `Ask` faults with the turn's own exception instead, never the earlier reply.
A turn that never calls `ReplyAsync`/`ReplyFaultAsync` on a real correlation is faulted anyway,
with an "actor did not reply" exception, so a handler bug can never hang a caller forever.

## Supervision

A turn that throws uncaught reaches `OnFailedAsync(ctx, ex)`. Its return value decides what
happens next — the turn that actually threw has already faulted its own `Ask` either way:

| Returns | Effect                                                                                                                                              |
| ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `true`  | The instance is discarded and rebuilt from its `Props`. The mailbox and pending-reply table survive; whatever is still queued keeps draining into the new instance. |
| `false` | The actor is removed from `IActorSystem`. Every remaining queued `Ask` faults with an "actor stopped" exception, every queued `Tell` is dropped, and the next `GetAsync` for the same `ActorId` spawns a fresh instance. |

An `OnFailedAsync` implementation that itself throws is treated as `false`.

## Persistence

*Skippable — durable per-actor state, opt-in per host.*

An actor implementing `IPersistentActor` adds two callbacks:

```csharp
using System.Threading;
using System.Threading.Tasks;

public interface IPersistentActor : IActor
{
    ValueTask<byte[]?> SaveStateAsync(IActorContext ctx);
    ValueTask LoadStateAsync(IActorContext ctx, byte[] state, CancellationToken ct = default);
}
```

`schema.UseActor(a => a.UsePersistence())` turns the mechanism on. State loads once, on the first
turn after spawn, from the persisted `SchemataActor.State` row (keyed by `ActorId`). It saves
after every turn that completes without throwing, before the turn's reply commits — a caller
observing a successful reply can rely on the state that produced it already being durable.
`Actor.Foundation` only resolves `IRepository<SchemataActor>`; the application registers it, the
same convention `Flow.Foundation`/`Scheduling.Foundation` use for their own entities. Enabling
persistence without registering that repository surfaces as a DI resolution failure the first
time a persistent actor's turn runs, not at startup.

Neither callback runs for an actor that does not implement `IPersistentActor`. The mailbox itself
is never persisted: a message sitting in a stopped process's channel is gone on restart, with no
recovery semantics.

## Bridge packages

*Skippable — event and scheduler integration, plus the two business-module bridges.*

`Schemata.Actor.Event`'s `UseEvent()` on the actor builder, then `RouteEvent<TEvent, TRoute>`,
delivers a matched event straight into an actor's mailbox — the event instance is the payload,
since `IEvent` already extends `IMessage`. Assuming [Event Bus](event-bus.md)'s `StudentEnrolled`
event is already registered:

```csharp
using Schemata.Actor.Event;
using Schemata.Actor.Skeleton;

public sealed class StudentEnrolledRoute : IEventActorRoute<StudentEnrolled>
{
    public ActorId? Resolve(StudentEnrolled @event) => new ActorId("counter", @event.StudentName!);
}
```

```csharp
schema.UseActor(a => a
    .Register<CounterActor>("counter")
    .UseEvent()
    .RouteEvent<StudentEnrolled, StudentEnrolledRoute>());
```

`IEventActorRoute<TEvent>.Resolve` returns the target `ActorId?`; returning `null` skips delivery
for that event. Multiple routes for one event type each get their own delivery attempt.

`Schemata.Actor.Scheduling`'s `UseScheduling()` implements `IActorReminders` on top of the
scheduler: `IActorContext.ScheduleAsync(message, delay)` schedules durable, delayed delivery to
the same actor, surviving a process restart. Calling it without `UseScheduling()` installed
throws, naming the missing capability, rather than becoming a silent no-op.

`Schemata.Flow.Actor` bridges the opposite direction: it replaces the Flow module's own write-path
handler registration so concurrent writers to the same `SchemataProcess` serialize through a
per-instance actor instead of racing on its `Timestamp` token.

- **Flow.Actor** wraps `CompleteActivityRequest`, `CorrelateMessageRequest`, `RunEventRequest`,
  `DeliverSignalRequest`, `TerminateProcessRequest`, and `CancelTokenRequest` — every command that
  writes to an already-existing process. `StartProcessRequest` stays unwrapped: there is no
  existing process key to race on yet. `ThrowSignalRequest`, the fan-out coordinator, also stays
  unwrapped: it performs no write of its own, only enumerating candidates and re-entering this
  same serialization per target through the already-wrapped `DeliverSignalRequest`.

The bridge replaces the module's unkeyed default handler registration, so every entry
point — the module's own facade, `IRequestDispatcher`, HTTP/gRPC transports, event and timer
bridges — gets the serialization without changing what it resolves.

`Schemata.Push.Actor` and `Schemata.Report.Actor` follow the same shape for their own modules.
Push.Actor keys on the `(Owner, Provider, ProviderKey)` subscription triple and wraps
`AddPushSubscriptionRequest` and `RemovePushSubscriptionRequest`, closing the subscription
check-then-add race while leaving the `SendPushRequest` fan-out parallel. Report.Actor keys on the
report name and wraps `RunReportRequest` and `GenerateReportRequest`, serializing concurrent
generations of one report so retention cannot race; an inline request with no report name skips the
actor and runs directly. The [Actor overview](../documents/actor/overview.md) covers all three
bridges in detail.

## Boundary rules

An envelope carries only a canonical name plus serializable operation data and `MessageContext` —
never an entity a caller already loaded or tracks. A handler reloads the entity fresh inside its
own turn instead. `MessageContexts.Capture(callerProvider)` flattens the sender's ambient state
before the message is queued; the turn's own scope restores it through the registered
`IMessageContextPropagator` implementations before any handler runs, the same boundary rule
[Messaging](messaging.md#distributed-dispatch) uses for a thread or process hop.

Ambient `AdviceContext` does not cross the mailbox: every turn is its own pipeline root. The
runtime constructs a fresh `AdviceContext` and calls `AdviceContext.Establish` immediately after
the turn's scope is built, before `OnReceiveAsync` runs — an actor turn never continues whatever
`AdviceContext` happened to be ambient on the sender's side.

Multi-tenancy resolves the tenant through the propagator and a two-phase turn scope:
`IActorTurnScopeFactory`'s default builds a scope from the host root, but a tenant-isolated
provider has to be resolved before that scope exists, so `Tenancy.Foundation`'s override runs a
short bootstrap scope first, resolves the tenant, and only then builds the real turn scope from
the tenant's own provider.

The in-process actor system removes the race between two callers in the same process. A
multi-instance deployment still relies on the entity's own `IConcurrency` optimistic-concurrency
token — the actor mailbox is not a distributed lock.

## Common pitfalls

- **Assuming a restarted actor keeps in-memory state.** `OnFailedAsync` returning `true` discards
  the faulted instance and constructs a fresh one from `Props` — only `IPersistentActor`'s durable
  state, with `UsePersistence()` on, survives a restart.
- **Calling `IServiceScopeFactory.CreateAsyncScope()` directly instead of injecting
  `IActorTurnScopeFactory`.** Multi-tenancy, and any capability that needs a turn to descend from
  a different provider, overrides that seam with `Replace`; bypassing it skips the override.
- **Expecting `IActorContext.ScheduleAsync` to become a no-op without `Actor.Scheduling`
  installed.** It throws instead.
- **Resolving a business module's keyed default handler outside a turn.** The keyed registration
  exists only for the turn dispatcher to resolve; any other call site bypasses the actor bridge's
  serialization entirely.

## Next steps

- [Messaging](messaging.md) — the request/command/query contracts every actor payload reuses
- [Flow](flow.md) — `Flow.Actor` serializes process writes through this same mailbox model
- [Scheduling](scheduling.md) — cron, periodic, and one-time jobs with lifecycle events

## See also

- [Actor Overview](../documents/actor/overview.md) — the full mailbox, supervision, and boundary reference
- [Messaging Overview](../documents/messaging/overview.md) — the request contracts actors reuse
