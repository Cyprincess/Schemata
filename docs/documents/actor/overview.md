# Actor

An in-process actor system: single-threaded, per-instance mailbox processing over ordinary DI
scopes, built to eliminate exactly one class of bug — two concurrent callers racing to write the
same `SchemataProcess` through its optimistic-concurrency token.

## Why it exists

Flow guards a mutable, optimistically-concurrent entity (`SchemataProcess`) behind a
request/response API. Two callers hitting `CompleteActivityRequest` for the same process at the
same time race on `Timestamp` and one of them loses with a concurrency exception. The actor system
removes the race at the entry point instead of asking every caller to retry: route every write
for a given process through one mailbox, and the mailbox's own single-consumer loop serializes
them for free. Scheduling's job-row writers use a different mechanism — the
`SchemataJobWriteGate` semaphore around their fresh-read-and-write section — not actor mailboxes.
See [Flow.Actor](#flowactor-per-instance-serialization) below for the actor mechanism.

## The model

```csharp
namespace Schemata.Actor.Skeleton;

public readonly record struct ActorId(string Type, string Key);

public interface IActor
{
    ValueTask OnStartedAsync(IActorContext ctx);
    ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope);
    ValueTask OnStoppedAsync(IActorContext ctx);
    ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex);
}

public interface IActorRef
{
    ActorId Id { get; }
    ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default) where T : IMessage;
    ValueTask<TResponse> AskAsync<TRequest, TResponse>(TRequest request, MessageContext? context = null, TimeSpan? timeout = null, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}

public interface IActorSystem
{
    Task<IActorRef> SpawnAsync(ActorId id, Props props);
    Task<IActorRef> GetAsync(ActorId id);   // spawn-if-absent
    Task StopAsync(ActorId id);
}
```

`ActorId.Type` is a routing key resolved through `IActorRegistry`, seeded by every
`SchemataActorBuilder.Register<TActor>(actorType, args)` call; `GetAsync` for an unregistered type
throws rather than silently constructing a default actor. `IActorContext.Services` is the current
turn's own scope provider — never a long-lived one — and `IMessage`/`IRequest<TResponse>` come
straight from `Schemata.Messaging.Skeleton`: there is no actor-only message marker, so any existing
command or query is already a valid actor payload.

`Schemata.Actor.Skeleton` is contracts only; `Schemata.Actor.Foundation` supplies the one runtime
implementation, `InProcessActorSystem`.

## Mailbox and backpressure

Each actor gets its own `System.Threading.Channels.Channel<Envelope>` — bounded (capacity from
`SchemataActorOptions.MailboxCapacity`, default 1024), single-reader — and one background task
draining it. A write past capacity blocks the writer (`BoundedChannelFullMode.Wait`) instead of
growing the queue or dropping messages; a large fan-out never balloons memory, it slows the sender
down to the mailbox's own processing rate.

The drain loop never reads the next envelope until the current turn has fully finished, including
releasing its own DI scope — a fire-and-forget turn would defeat the entire point of routing writes
through the actor. Cancellation observed while a message is still queued is handled by an atomic
`Queued → Executing` / `Queued → Canceled` state on the envelope itself, not by pulling it back out
of the channel: `ChannelWriter.WriteAsync`'s own cancellation cannot un-write an already-accepted
item, so the consumer side has to be the one to notice a caller gave up before it starts the turn.
Cancellation observed *while* the turn is already executing does not stop it — the handler still
runs to completion and the loop still releases the scope — it only wakes `IActorContext.Stopping` for
a handler that wants to observe it.

**Two explicit non-goals.** There is no `MailboxKind` or `Props.Mailbox` selection — a priority
mailbox has no implementation description and no use case anywhere in this design, so a single-value
enum plus an unused property would be pure surface area; every mailbox is the one bounded-FIFO shape
above. And the mailbox itself is never persisted: a message sitting in a stopped or crashed process's
channel is gone on restart, with no recovery semantics and no opt-in switch to change that. This is a
deliberate, accepted trade-off, not an oversight — losing a queued message on restart is equivalent
to a plain (non-actor) request failing outright, and authoritative state was never the mailbox's job
to keep: it lives in the domain entity a handler reloads inside its turn (`SchemataProcess`), or,
for an actor that opts into [persistence](#persistence-is-opt-in-and-the-actor-never-holds-authoritative-state),
in `SchemataActor.State` — never in an in-flight envelope.

## Ask, Tell, and supervision

`TellAsync` is fire-and-forget: on a stopped actor's closed channel, the message is dropped. `AskAsync`
generates a `CorrelationId`, registers a `TaskCompletionSource` in the actor's own pending-reply
table, enqueues the envelope, and awaits the completion (bounded by an optional `timeout` and the
caller's own `ct`). Inside the turn, `IActorContext.ReplyAsync`/`ReplyFaultAsync` resolve that same
table entry; a turn triggered by a `Tell` has `CorrelationId == Guid.Empty` and both calls are
no-ops. A turn that throws always faults its own `Ask` with the original exception — a reply recorded
earlier in the same turn is provisional and is discarded once the turn ends abnormally. A turn that
ends without ever calling `ReplyAsync`/`ReplyFaultAsync` on a real `CorrelationId` is faulted anyway
with an "actor did not reply" exception, so a caller can never hang forever on a handler bug.

`RequestDispatchingActor` — the one built-in `IActor` every bridge package reuses instead of writing
its own — implements this shape directly: resolve the turn's scope, restore ambient context, resolve
the keyed default handler, call it, reply with the result or fault with the exception.

**Supervision** is driven by `OnFailedAsync(ctx, ex)`'s return value once a turn throws uncaught:

| Returns | Effect |
|---|---|
| `true` (restart) | The instance is discarded and rebuilt from its `Props`; the mailbox and pending-reply table survive, and the loop keeps draining |
| `false` (stop) | The actor is removed from `IActorSystem`; every remaining queued `Ask` is faulted with an "actor stopped" exception, every queued `Tell` is dropped; the next `GetAsync` for the same `ActorId` spawns a fresh instance |

Either outcome, the turn that actually threw has already faulted its own caller — a restart never
swallows the original exception, it only decides what happens to the *next* message.

## Persistence is opt-in, and the actor never holds authoritative state

```csharp
public interface IPersistentActor : IActor
{
    ValueTask<byte[]?> SaveStateAsync(IActorContext ctx);   // null = no change this turn, skip the write
    ValueTask LoadStateAsync(IActorContext ctx, byte[] state, CancellationToken ct = default);
}
```

`UseActor(a => a.UsePersistence())` turns the mechanism on; an actor participates by implementing
`IPersistentActor`. State loads once, on the first turn after spawn, from `SchemataActor.State`
(opaque `byte[]`, keyed by `ActorId`); it saves after every turn that completes *without* throwing,
strictly before the turn's reply commits — a caller observing a successful reply can rely on the
state that produced it already being durable. Neither read nor write happens for an actor that does
not implement `IPersistentActor`, or when `UsePersistence()` was never called — `RequestDispatchingActor`
itself is stateless and never touches the table. `Actor.Foundation` only *resolves*
`IRepository<SchemataActor>`; the application registers it, the same convention `Flow.Foundation` and
`Scheduling.Foundation` follow for their own entities.

This is deliberately narrow: `SchemataActor` never carries authoritative domain state. The real data
lives in `SchemataProcess`, reloaded fresh inside the turn every time (see
[Flow.Actor](#flowactor-per-instance-serialization) below); a persistent actor's
`byte[]` is bookkeeping the actor keeps about itself, not a second source of truth.

## `Flow.Actor`: per-instance serialization

The bridge follows one template: replace the unkeyed default `IRequestHandler<TRequest,TResponse>`
registration for a set of write-path commands with a wrapper that redirects the call to a per-instance
actor, keeping the keyed default registration intact for the actor's own turn to resolve.

```csharp
// Constructed with only IActorSystem and the caller's IServiceProvider — never the inner handler,
// never anything that outlives this synchronous call.
internal sealed class ActorSerializingHandler<TRequest, TResult>(IActorSystem actors, IServiceProvider caller)
    : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>, IProcessScoped
{
    public async Task<TResult> HandleAsync(TRequest request, CancellationToken ct = default) {
        var context = MessageContexts.Capture(caller);
        var actor   = await actors.GetAsync(new ActorId("flow", request.ProcessCanonicalName));
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}
```

- **Flow.Actor** wraps `Complete` / `Correlate` / `RunEvent` / `DeliverSignal` / `Terminate` /
  `CancelToken` — every command that writes to an already-existing `SchemataProcess`. `Start` is left
  unwrapped (no existing process key to race on yet); the `ThrowSignal` fan-out coordinator performs
  no write of its own and re-enters the same serialization per target through the already-wrapped
  `DeliverSignal`.
- Because every entry point — facade, `IRequestDispatcher`, HTTP/gRPC transports, event and timer
  bridges — resolves the same unkeyed interface, `services.Replace(...)` makes the serialization
  apply everywhere at once, with no call site changing what it resolves.
- The wrapper never injects the keyed inner handler and never holds a caller-scoped object across the
  mailbox boundary: `caller` is read exactly once, synchronously, before the request is enqueued,
  purely to flatten ambient state into a `MessageContext`. `RequestDispatchingActor`'s turn rebuilds a
  fresh scope and resolves the keyed default handler there.

## `Push.Actor`: per-subscription serialization

`Push.Actor` follows the same template as `Flow.Actor` — replace the unkeyed default handler for a
set of write-path commands, keep the keyed default registration for the turn to resolve — keyed by
the subscription identity instead of a process.

- **Push.Actor** wraps `AddPushSubscriptionRequest` and `RemovePushSubscriptionRequest`, the two
  commands that write an existing `SchemataPushSubscription` identity. The `ActorId` is
  `("push", request.SubscriptionKey)`, where `SubscriptionKey` is the `{Owner}|{Provider}|{ProviderKey}`
  triple, so concurrent writers to the same subscription share one mailbox and serialize. The
  check-then-add race that produces a duplicate row under EF read-committed, or a transaction abort
  under LinqToDB, closes because the read and the write now run in one turn.
- `SendPushRequest` stays unwrapped: its fan-out across transports is deliberate parallelism, and a
  per-target mailbox would collapse it into a bottleneck. The read-path queries
  (`GetPushSubscriptionsQuery`, `ExistsPushSubscriptionQuery`) perform no write and need no mailbox.
- The guarantee holds inside one process: `InProcessActorSystem` stores its cells in a process-local
  `ConcurrentDictionary`, so same-triple upserts on one instance never conflict. Across instances the
  database unique index on `(Owner, Provider, ProviderKey)` is the backstop.

## `Report.Actor`: per-report serialization

`Report.Actor` keys on the report name and adds one wrinkle the other bridges do not have: the same
command type serves both a named generation and an inline one, so the wrapper falls back to the keyed
default handler when there is no report identity to key on.

- **Report.Actor** wraps `RunReportRequest` and `GenerateReportRequest`. The `ActorId` is
  `("report", request.ReportKey)`, where `ReportKey` is the report name from `IReportScoped`. Two
  generations of the same report share one mailbox, so a second snapshot cannot be written while the
  first is running, and the retention step — which lists every snapshot for the report and trims the
  excess — cannot race a concurrent generation's list.
- An inline request carries an empty `ReportKey` (an ad-hoc query with no report definition). The
  wrapper resolves the keyed default handler directly on the caller's own scope and runs it there,
  with no mailbox, matching the behavior without the bridge. This is the one place a per-module
  wrapper falls back to the inner handler, because a single command type covers both named and inline
  generation; `Flow.Actor` and `Push.Actor` instead leave a *different* command type unwrapped.
- Every entry point resolves the same unkeyed handler, so the scheduled generation job, the facade,
  and the HTTP/gRPC `:generate` custom method all serialize through the report's mailbox. The value is
  process-local: the snapshot `Uid` is already an idempotent key and the header state machine is
  durable, so this bridge removes redundant work and retention races within one instance, not a
  correctness gap. Across instances, generation still needs external coordination.

All three per-module bridges — `Flow.Actor`, `Push.Actor`, `Report.Actor` — share one wrapper shape:
construct with only `IActorSystem` and the caller's `IServiceProvider`, capture `MessageContext` once
at enqueue, and let `RequestDispatchingActor` resolve the keyed default handler inside the turn.

```csharp
// Push.Actor and Report.Actor use the same wrapper shape as Flow.Actor, keyed by their own identity.
internal sealed class ActorSerializingHandler<TRequest, TResult>(IActorSystem actors, IServiceProvider caller)
    : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>, ISubscriptionScoped   // Report.Actor: IReportScoped
{
    public async Task<TResult> HandleAsync(TRequest request, CancellationToken ct = default) {
        var context = MessageContexts.Capture(caller);
        var actor   = await actors.GetAsync(new ActorId("push", request.SubscriptionKey));
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}
```

None of the three enters any meta-target: a consumer adds `Schemata.Flow.Actor`,
`Schemata.Push.Actor`, or `Schemata.Report.Actor` as an explicit `PackageReference`, the same
convention `Schemata.Flow.Bpmn` follows.

## `Actor.Event` / `Actor.Scheduling`: the other direction

These two bridge *into* the actor system rather than serializing an existing write path:

- **`Actor.Event`** lets an event drive an actor. A consumer registers an explicit
  `IEventActorRoute<TEvent>` (`Resolve(event) -> ActorId?`); `EventActorForwarder<TEvent>` — an
  `IEventHandler<TEvent>` — delivers every matched event to its resolved actor via `TellAsync`.
  Multiple routes for one event type all fire independently; `Resolve` returning `null` skips that
  route without error. There is no convention-based inference from event type to actor type.
- **`Actor.Scheduling`** implements `IActorReminders` — durable, delayed delivery that survives a
  process restart — on top of the scheduler. `ScheduleAsync` translates the delay into a one-time
  `SchemataJob` (`Replay = true`) fired by a single shared job type, `ActorReminderJob`, which
  rehydrates the target `ActorId` and the JSON-serialized payload from `JobContext.Variables` and
  delivers it with `TellAsync`. `IActorContext.ScheduleAsync` throws a clear exception when this
  bridge is not installed, rather than becoming a silent no-op.

Both packages capture `MessageContext` from their own consumption/execution scope — the event
handler's scope, the job's scope — not from whenever the event was originally published or the
reminder was originally scheduled, for the same reason `Flow.Actor` captures at
enqueue time: it is the only scope that still exists by the time the message actually needs to cross
into the actor's turn.

## `MessageContexts.Capture`: the boundary rule

A dispatch that crosses a DI scope, a thread, or a process loses the caller's ambient state — the
actor mailbox is exactly such a boundary. The rule is one sentence: **capture happens once, in the
sender's own scope, before the message is queued; restore happens once, inside the turn's freshly
built scope, before any handler is resolved.**

```csharp
var context = MessageContexts.Capture(callerProvider);   // sender side, synchronous, before enqueue
```

```csharp
// Actor.Foundation's turn dispatcher (Internal/InProcessActorTurnScopeFactory or a tenancy override):
await using var scope = await turnScopeFactory.CreateAsync(envelope.Context, ct);
foreach (var propagator in scope.ServiceProvider.GetServices<IMessageContextPropagator>()) {
    await propagator.RestoreAsync(envelope.Context?.Items ?? Empty, scope.ServiceProvider, ct);
}
// only now does the turn resolve a handler
```

Propagators are resolved as a collection — an empty one means nothing needs rebuilding, so no part
ever probes whether another part is installed. With no propagator registered, `Capture` returns an
empty context and every `RestoreAsync` is a no-op. Multi-tenancy is the concrete, shipped example:
`Schemata.Tenancy.Foundation`'s `TenantMessageContextPropagator<TTenant>` reads the resolved tenant
off `ITenantContextAccessor<TTenant>` on capture and reinitializes `ITenantContextInitializer<TTenant>`
on restore, so a turn built in a background mailbox — which never ran the tenancy middleware — still
resolves repositories against the right tenant provider instead of the wrong or default one.

**`IActorTurnScopeFactory` exists because a DI scope cannot be retargeted to a different provider once
created.** The default implementation (`Actor.Foundation`, registered with `TryAdd`) builds a scope
from the host root and restores propagators into it. Multi-tenancy needs the tenant resolved *before*
the real turn scope exists, because the scope has to be built from the tenant's own isolated provider
— fixing up ambient state after the fact is too late. `Tenancy.Foundation`'s
`TenantActorTurnScopeFactory<TTenant>` therefore runs two phases: a short-lived bootstrap scope off the
host root resolves the tenant and initializes the tenant context; `ITenantServiceScopeFactory<TTenant>`,
resolved from that same bootstrap scope, then builds the real turn scope from the tenant-isolated
provider (and owns acquiring/releasing the `ITenantProviderLease`); every propagator runs a second time
in that final scope, since it descends from a different provider with its own accessor instance.
Disposal releases the final scope first, then the bootstrap scope. It is registered with `Replace`
over the default — `Actor.Foundation` itself depends on no tenancy type at all, it only ever resolves
whatever `IActorTurnScopeFactory` is installed.

`ClaimsPrincipal` does not travel this way: it is already a field on the request records that carry
it (§8 M3.1 of the messaging/actor RFC), so it crosses the mailbox boundary inside the envelope's own
payload, not through `MessageContext`.

## Ambient `AdviceContext`: an actor turn is a new root

Every actor turn establishes its own fresh `AdviceContext` — `ActorInstance` constructs
`new AdviceContext(scope.ServiceProvider)` and calls `AdviceContext.Establish` immediately after the
turn's scope is built, before `OnReceiveAsync` runs and before any state is loaded. This makes an
actor turn one of the sanctioned pipeline roots alongside `InProcessRequestDispatcher.SendAsync`,
`JobExecutionDispatcher`, and event publish/consume — not a continuation of whatever ambient context
happened to exist on the sender's side. The two are related but distinct: `MessageContext` carries
explicit, serializable state (tenant identity and the like) across the mailbox boundary through
propagators; `AdviceContext` is a purely in-process object holding a live `IServiceProvider` that
never crosses a boundary at all, and a fresh one starting on the far side of the mailbox is exactly
what "ambient state does not cross a Channel" (see [Messaging](../messaging/overview.md#ambient-advicecontext-root-establishes-downstream-continues))
means in practice for actors.

## Common pitfalls

- **Injecting the inner keyed handler into an `ActorSerializingHandler`-shaped wrapper.** That runs
  the handler on the caller's own scope, whose lifetime the actor system does not own — the wrapper
  reads the caller's provider exactly once, synchronously, to capture context, and never again.
- **Resolving a handler by its keyed default registration outside a turn.** Keyed defaults exist only
  for a turn to resolve; any other call site bypasses the `Replace` in `Flow.Actor`, `Push.Actor`, or
  `Report.Actor` entirely and reintroduces the race the bridge exists to remove. `Report.Actor`'s own
  inline bypass is the one sanctioned exception: it resolves the keyed default handler directly for a
  request with no report identity.
- **Calling `IServiceScopeFactory.CreateAsyncScope()` directly from turn-dispatch code.** Every turn's
  scope must come from the injected `IActorTurnScopeFactory` — that is the one seam multi-tenancy (or
  any future capability that needs to change which provider a turn descends from) overrides with
  `Replace`.
- **Assuming a restarted actor keeps in-memory state.** `OnFailedAsync` returning `true` discards the
  faulted instance and constructs a fresh one from `Props`; only `IPersistentActor`'s durable
  `byte[]` (if `UsePersistence()` is on) survives a restart, never fields on the old instance.
- **Capturing `MessageContext` on the receiving side of a boundary.** Capture must run in the
  *sender's* scope, before the message is queued — that is the only place the ambient state to
  flatten still exists.
