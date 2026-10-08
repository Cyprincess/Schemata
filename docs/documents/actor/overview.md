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

Actor callbacks carry runtime-owned turn identity. An Ask to the same tenant/type/key fails before
mailbox admission; cross-actor requests remain allowed. Callback completion invalidates captured turn
identity. Ordinary message scopes never install it; multi-actor cycle detection is outside this contract.

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

`TellAsync` acknowledges mailbox acceptance; a retirement race that loses admission throws explicitly.
Graceful manual/idle retirement drains accepted messages. Fatal startup/supervision abort remains a
separate failure mode and can discard queued Tell messages. `AskAsync` creates a correlation and awaits
the reply; its timeout includes waiting for a retiring activation, admission, and reply.
The caller's `ct` also cancels the wait. Inside the turn, `IActorContext.ReplyAsync`/`ReplyFaultAsync` resolve the
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

### Activation and retirement states

Each instance moves through one atomic lifecycle state: `Starting` → `Running` →
`GracefulStopping`/`Aborting` → `Retired`. Normal receives run only after initialization
completes successfully. A graceful `StopAsync` that arrives while initialization is still
running does not skip it: initialization completes, the mailbox closes to new writes, and every
already-accepted item still gets a normal receive. A startup failure overrides an earlier
graceful intent — the state escalates to `Aborting`, queued `Ask`s are faulted with an
"actor stopped" exception, queued `Tell`s are dropped, and `OnStoppedAsync` still fires exactly
once. The identity slot stays occupied through the drain and `OnStoppedAsync`, so a
same-identity activation never overlaps the retiring instance's turns. Retained logical references resolve
the current activation at send time and reactivate after retirement, preserving their activation recipe.
An already-retiring self-Tell is rejected instead of waiting for its own callback to finish.

### Host shutdown

`AddSchemataActor()` connects the selected in-process runtime to `IHostApplicationLifetime.ApplicationStopping`. That notification closes new acquisition and delivery admission and starts graceful retirement. Accepted turns finish their state saves and replies before `OnStoppedAsync` and actor disposal. Custom `IActorSystem` implementations retain responsibility for their own host integration.

The hosted service waits using the host-supplied cancellation budget, including `HostOptions.ShutdownTimeout`. When that budget expires, outstanding Ask completions fault and each activation receives an asynchronous cancellation request. The host wait reports cancellation; it does not terminate arbitrary managed callbacks. Running turns, cancellation callbacks and lifecycle cleanup keep their identity slot until they actually finish. Construction already in progress is also retained and receives the current shutdown disposition when it finishes.

Cancellation callback failures are observed before terminal notification, without skipping `OnStoppedAsync` or disposal. A single lifecycle failure retains its exception type; independent failures are aggregated. A later shutdown wait can observe retirement that completed after an earlier budget expired.

Sources: `Runtime/ActorHostedService.cs`, `Runtime/InProcessActorSystem.cs`, `Runtime/ActorInstance.cs` and `Runtime/MailboxItem.cs` in `src/Schemata.Actor.Foundation/`.

### Idle collection

The hosted in-process collector uses `TimeProvider` monotonic timestamps. `IdleTimeout` defaults to
15 minutes and `IdleScanInterval` to one minute. Admission, queued work, active turns, state saves, and
item disposal keep the activation ineligible; the idle timestamp advances after work is released.
Collection uses the same graceful retirement protocol and retains the identity slot through cleanup.
The in-process implementation is the scope of this policy; Orleans integration is separate.

### Activation-local timers

`IActorContext.RegisterTimer(name, callback, dueTime, period)` schedules an in-memory timer through
`TimeProvider`; omit `period` for one delivery. `CancelTimer(name)` is idempotent. Re-registering a name
invalidates the previous generation, including ticks already waiting in the mailbox. The consumer checks
the generation before claiming the callback, which executes with a fresh turn scope and cannot overlap
receive callbacks. A callback already claimed may finish after cancellation.

Periodic ticks coalesce while one tick is pending in the mailbox; they do not build an unbounded backlog.
Timer-thread callbacks only enqueue mailbox work. Retirement and supervision restart dispose all timer
registrations, and stale timer callbacks never reactivate an actor. Durable reminders remain the separate
`Actor.Scheduling` capability.

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
itself is stateless and never touches the table. Each turn resolves the registered
`ActorStateStore` from the turn's final service scope; `UsePersistence()` installs it scoped over
the application's `IRepository<SchemataActor>`, the same convention `Flow.Foundation` and
`Scheduling.Foundation` follow for their own entities. A tenant that needs its own store
dependencies registers `ActorStateStore` explicitly in the tenant container.

`ActorStateStore` finds state by the unique `(ActorType, ActorKey)` pair on `SchemataActor`, matching
`ActorId.Type` and `ActorId.Key`. Resource `Name` is independent of that pair. The application
registers an `IRepositoryAddAdvisor<SchemataActor>` through `TryAddEnumerable` to assign a missing
name before `AdviceAddCanonicalName.DefaultOrder` (120,000,000), preserving explicit names. The
store supplies the actor identity and state on insert; it supplies no resource-name fallback.

Existing databases need a consumer migration for the `ActorType` and `ActorKey` columns and their
unique composite index. Recover the pair from authoritative application identity data or a known,
unambiguous legacy encoding. Blindly splitting an old `Name` at `/` is unsafe when a type or key
contains that delimiter. Preserve resource names independently, and validate identity uniqueness
before applying the index; reads do not fall back to the old concatenated name.

Implementation: `src/Schemata.Actor.Skeleton/Entities/SchemataActor.cs` and
`src/Schemata.Actor.Foundation/Runtime/ActorStateStore.cs`.

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

Reminder slots use `SchemataJob.Key`: `actor-reminder:` plus the JSON array of tenant, actor type,
actor key, and caller-supplied reminder name. Scheduling returns `ActorReminder(Target, Name)`;
cancellation uses that tenant-bound identity. Resource names still come from consumer advisors.
Replacing a slot atomically cancels unclaimed occurrences and commits the new payload and due time.
`ScheduleVersion` prevents old running completions from overwriting a replacement or rearming a
cancelled slot. A delivery already claimed as Running may finish; cancellation suppresses pending work.
Recovery adopts a matching pending occurrence with its original identity and tenant.

Implementation: `src/Schemata.Actor.Scheduling/Runtime/ActorReminders.cs`.

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
// Actor.Foundation's turn dispatcher (default MessageExecutionScopeFactory or a tenancy override):
var scope = await turnScopeFactory.CreateAsync(envelope.Context, ct);
using var identity = scope.Enter();
await using (scope) {
    await scope.RestoreAsync(envelope.Context, ct);
    // Resolve and invoke the handler through scope.Services here.
}
```

`IMessageExecutionScopeFactory.CreateAsync` returns a `MessageExecutionScope` that owns the turn's
DI scope, exposes its `Services`, and runs the registered `IMessageContextPropagator` collection
through `RestoreAsync`. Neither `CreateAsync` nor `RestoreAsync` installs ambient identity; only
`scope.Enter()` calls `TenantContext.Enter(scope.Identity)`. `ActorInstance` calls `Enter` and
retains the lease for the lifetime of the turn so that propagators, cache lookups, and any
outgoing `MessageContexts.Capture` see the right `TenantIdentity`. The actor runtime consumes the
shared identity contract and resolves the installed `IMessageExecutionScopeFactory`.

**Multi-tenancy replaces the factory with `Schemata.Tenancy.Messaging`'s `TenantMessageExecutionScopeFactory<TTenant>`**
(registered by `UseMessaging` on the tenancy builder). The replacement resolves the tenant identity
from `MessageContexts.Identity(context)`, builds a bootstrap scope off the host root, resolves
`ITenantServiceScopeFactory<TTenant>` from that bootstrap, then returns a `MessageExecutionScope`
backed by the tenant-isolated provider (and owns acquiring/releasing the `ITenantProviderLease`).
`MessageExecutionScope.Enter` is called by `ActorInstance` to install the `TenantIdentity` into
`TenantContext` for the duration of the turn. Capture already serialized the tenant into
`MessageContext.Items` through `MessageContexts.Bind(TenantContext.Current, …)`; restore runs every
registered `IMessageContextPropagator` against the tenant scope so downstream services see the
resolved tenant. The execution scope is disposed before its identity frame is restored.

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
  scope must come from the injected `IMessageExecutionScopeFactory` — that is the seam
  `Schemata.Tenancy.Messaging.UseMessaging` overrides with `Replace` so the turn's provider descends
  from the resolved tenant's isolated container instead of the host root.
- **Assuming a restarted actor keeps in-memory state.** `OnFailedAsync` returning `true` discards the
  faulted instance and constructs a fresh one from `Props`; only `IPersistentActor`'s durable
  `byte[]` (if `UsePersistence()` is on) survives a restart, never fields on the old instance.
- **Capturing `MessageContext` on the receiving side of a boundary.** Capture must run in the
  *sender's* scope, before the message is queued — that is the only place the ambient state to
  flatten still exists.
