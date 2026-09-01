# Actor Process Serialization

## What you'll build

Two concurrent calls against the same Flow instance — say two `POST ~/v1/processes/{name}:complete`
arriving at the same time — stop racing on `SchemataProcess.Timestamp`. Loading the
`Schemata.Flow.Actor` bridge chains a per-instance mailbox in front of six process-scoped write
commands, every command that reads and writes an already-existing process (Step 3 lists them): the
second call queues behind the first, the first commits, and the second then runs against the state
the first committed instead of losing with an optimistic-concurrency abort.

The bridge is a thin envelope. It does not change the engine, the persistence shape, or how
advisors run; it changes which thread serves a write and when.

## Prerequisites

- A working Flow setup from [flow.md](../guides/flow.md).
- The `Schemata.Flow.Actor` package added to your project.

## Step 1: The conflict it removes

`SchemataProcess` carries an `[ConcurrencyCheck]` `Timestamp` (one of the `IConcurrency` fields).
Two requests racing on the same row both load it at `Timestamp = T`, both stage their update, both
try to commit; one succeeds and the other fails with a database update-concurrency exception that
the EF Core provider translates to `AbortedException`
(`Schemata.Entity.EntityFrameworkCore/EfCoreUnitOfWork.cs:80-81`). The loser has to retry the whole
operation. With a Flow process the retry is rarely free: the side effects of the transition
(catch handlers, source projection, the follow-up subscriptions) re-run.

Within one application instance the bridge replaces that lose-and-retry with first-come,
first-served: the second command waits its turn.

## Step 2: Install the bridge

```csharp
builder.UseSchemata(schema => {
    schema.UseFlow()
          .UseStateMachine()
          .UseActor()
          .MapHttp()
          .Use<OrderProcess>();
});
```

`UseActor()` is a one-call extension on the flow builder that adds `SchemataFlowActorFeature`
(priority `490_600_000`). The feature depends on `SchemataFlowFeature` and `SchemataActorFeature`,
so the actor runtime comes along if it is not already present. No call site changes — every Flow
entry point (HTTP and gRPC transports, `IFlowRunner`, `IRequestDispatcher`, the event and timer
bridges) resolves the same unkeyed handler interface, and the bridge rewires that one resolution.

**Check:** the app starts with no missing-feature errors and an `IActorSystem` resolves from the
container.

## Step 3: What gets serialized

`SchemataFlowActorFeature.ConfigureServices` replaces the unkeyed
`IRequestHandler<TRequest,TResponse>` registration for six process-scoped write commands with
`ActorSerializingHandler<TRequest,TResponse>`
(`Schemata.Flow.Actor/Internal/ActorSerializingHandler.cs`). The keyed default handler stays
registered, untouched, under `FlowConstants.Handlers.Default` — that is what the actor's turn
will resolve.

| Command                | Wraps (facade / transport / bridge)                                                  |
| ---------------------- | ------------------------------------------------------------------------------------ |
| `CompleteActivityRequest`     | `IFlowRunner.CompleteAsync`, `POST ~/v1/processes/{name}:complete`           |
| `CorrelateMessageRequest`     | `IFlowRunner.CorrelateAsync`, `POST ~/v1/processes/{name}:correlate`         |
| `RunEventRequest`             | `FlowTimerJob` (timer fires), `FlowRunner.RunEventAsync`                      |
| `DeliverSignalRequest`        | per-target signal delivery from `ThrowSignalHandler`                          |
| `TerminateProcessRequest`     | `IFlowRunner.TerminateAsync`, `POST ~/v1/processes/{name}:terminate`         |
| `CancelTokenRequest`          | `IFlowRunner.CancelTokenAsync`, tokens `:cancel` endpoint                     |

Two commands stay unwrapped, on purpose:

- **`StartProcessRequest`** — no existing process row exists yet to race on; the engine creates it.
- **`ThrowSignalRequest`** — the fan-out coordinator performs no write of its own. It enumerates
  waiting targets and re-enters the same route per target through the already-wrapped
  `DeliverSignalRequest`, so each target still serializes on its own mailbox.

## Step 4: How a command crosses into the actor

`ActorSerializingHandler<TRequest, TResult>` is closed over two dependencies only: the
`IActorSystem` and the *calling* scope's `IServiceProvider`. It does not inject the keyed inner
handler and never holds a caller-scoped object across the mailbox boundary.

```csharp
// Schemata.Flow.Actor/Internal/ActorSerializingHandler.cs (shape, paraphrased)
public async Task<TResult> HandleAsync(TRequest request, CancellationToken ct = default) {
    var context = MessageContexts.Capture(caller);   // sender side, once, synchronous
    var actor   = await actors.GetAsync(new ActorId("flow", request.ProcessCanonicalName));
    return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
}
```

Three things follow:

- **The actor id is `(flow, {process canonical name})`.** One mailbox per process, shared across
  every command type above — a complete and a correlate on the same instance serialize against
  one another, not independently.
- **`MessageContexts.Capture` runs in the sender's own scope, before the message is queued.**
  That flattened context (tenant, anything else an `IMessageContextPropagator` exported) is the
  only piece of ambient state allowed to cross the mailbox.
- **`GetAsync` is spawn-if-absent.** The first request to the instance creates the actor; the
  actor then lives for the process's lifetime.

`AskAsync` generates a `CorrelationId`, registers a `TaskCompletionSource` in the actor's
pending-reply table, and enqueues the envelope. A bounded channel
(`SchemataActorOptions.MailboxCapacity`, default `1024`, `BoundedChannelFullMode.Wait`) applies
backpressure to the writer when full rather than growing the queue, so a large fan-out slows the
sender down to the mailbox's own processing rate instead of ballooning memory.

## Step 5: How the turn runs

The actor registered for the `flow` route is the shared `RequestDispatchingActor`
(`Schemata.Actor.Foundation/Internal/RequestDispatchingActor.cs`). Its turn is a four-step loop:

1. Build a fresh DI scope from the injected `IActorTurnScopeFactory` — never from
   `IServiceScopeFactory.CreateAsyncScope` directly, since multi-tenancy replaces the factory with
   one that builds the scope from the tenant-isolated provider.
2. Restore ambient state: every registered `IMessageContextPropagator` runs against the new scope
   (the tenancy propagator reinitializes `ITenantContextInitializer<TTenant>` from the captured
   tenant id, so a turn built in a background mailbox still resolves repositories against the
   right tenant).
3. Resolve the *keyed* default handler (`FlowConstants.Handlers.Default`) inside that fresh scope,
   call it, and `ReplyAsync` with the result. `ClaimsPrincipal` already travels on the request
   record itself, so it does not need to round-trip through `MessageContext`.
4. The keyed default handler reloads the process row inside the turn — its `Timestamp` is whatever
   the previous turn committed. The race window between two turns writing the same row is gone.

The drain loop never reads the next envelope until the current turn's scope is fully disposed, so
a fire-and-forget turn cannot defeat the serialization. A turn that throws faults its own `Ask`
with the original exception; `OnFailedAsync` returns `true` (restart) so the stateless actor is
discarded and rebuilt from `Props`, and the mailbox plus pending-reply table survive for whatever
is still queued.

## Step 6: Observe the bridge in effect

Two concurrent completes on the same process now run sequentially against the same actor. The
first commits; the second then observes the state the first committed, so the loser fails with
the engine's own state error (the token is no longer ready at that activity) rather than an
`AbortedException` from a stale `Timestamp`. Either way the second call gets a deterministic
outcome: the path that previously raced on optimistic concurrency now races on application state.

For a cross-instance check (different process rows), nothing about the bridge applies: those go
through different actors, run in parallel, and rely on the optimistic concurrency check exactly
as before.

## Common pitfalls

**The actor only serializes inside one application instance.** Multi-replica deployments, or a
writer that touches the database from outside the app (a SQL script, another service sharing the
database), still race on `IConcurrency`. The bridge removes the in-process race; it does not
remove the optimistic lock, and clients should still be prepared to handle `AbortedException`
with retry at the boundary.

**Entities never cross the mailbox.** Requests carry canonical names and serializable data only;
the keyed default handler reloads the aggregate inside the turn. Stuffing a tracked
`SchemataProcess` into a custom request breaks the contract. For Flow, the corollary is to keep new
command records flat: name + payload, never an entity.

**Ambient state crosses only through `MessageContext`.** Capture happens once, in the sender's
scope, before enqueue (`MessageContexts.Capture(caller)`). Restore happens once, inside the
turn's fresh scope, through the resolved `IMessageContextPropagator` collection. With no
propagator registered, capture returns an empty context and every restore is a no-op. With
`Schemata.Tenancy.Foundation`, `TenantMessageContextPropagator<TTenant>` plus
`TenantActorTurnScopeFactory<TTenant>` rebuilds the turn scope from the tenant-isolated provider —
a mailbox- or scheduler-originated turn still resolves the right tenant's repositories instead of
the wrong or default ones.

**Resolving the keyed default handler yourself reintroduces the race.** Keyed defaults exist for
the turn to resolve. Calling them from application code, or from anywhere that is not the actor's
turn, bypasses the `services.Replace(...)` that installs the wrapper. Inject `IFlowRunner` or
`IRequestDispatcher`, never the keyed `IRequestHandler<,>` registrations directly.

**The mailbox is not durable.** A queued envelope in a stopped or crashed process is gone on
restart — equivalent to a request that failed outright, with no recovery semantics. Authoritative
state lives in the `SchemataProcess` row, reloaded fresh inside each turn, never in an in-flight
envelope. Persist what matters before issuing the write command, and treat the bridge as
ordering, not as durable queueing.

## See also

- [documents/actor/overview.md](../documents/actor/overview.md) — actor system, mailbox, persistence, supervision
- [documents/flow/runtime.md](../documents/flow/runtime.md) — `FlowRunner`, `IFlowRunner`, the unkeyed alias
- [cookbook/flow-with-events.md](flow-with-events.md) — the message bridge that lands on `CorrelateMessageRequest`
- [cookbook/flow-with-timers.md](flow-with-timers.md) — `FlowTimerJob` and the timer path through `RunEventRequest`
- [guides/scheduling.md](../guides/scheduling.md) — the per-job mailbox on `TriggerJobRequest` follows the same template
