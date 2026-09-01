# Messaging

Request/reply dispatch, independent of the event bus, and the base every command/query-shaped
module builds on.

## Why it is its own domain

A request expects exactly one answer from exactly one handler. An event is broadcast to whoever
subscribed. Those are different shapes, and tying them together forced anything wanting request/reply
— Push, Insight, Resource — to depend on the event domain it did not otherwise need.

`Schemata.Messaging.Skeleton` therefore owns the request contracts, and
`Schemata.Event.Skeleton` depends on *it* rather than the other way round:

```
IMessage                        (Messaging.Skeleton)
├── IRequest<TResponse>         (Messaging.Skeleton)  — one handler, one answer
│   ├── ICommand / ICommand<T>  (Messaging.Skeleton)  — write path
│   └── IQuery<TResult>         (Messaging.Skeleton)  — read path
└── IEvent                      (Event.Skeleton)      — many handlers, no answer
```

`IEvent : IMessage`. `IRequest<TResponse> : IMessage`. A request is **not** an event, and never
was — `IRequest` does not extend `IEvent` and no request type is dual-tagged as both.

## Packages

| Package | Role |
|---|---|
| `Schemata.Messaging.Skeleton` | `IMessage`, `IRequest<TResponse>`, `IRequestHandler<,>`, `IRequestDispatcher`; `ICommand` / `ICommand<T>` / `IQuery<T>` and their handler interfaces; `ICommandDispatcher` / `IQueryDispatcher`; `Advisors.ICommandAdvisor<T>` / `IQueryAdvisor<T>`; `InProcessRequestDispatcher`; `MessageContext`, `IMessageContextPropagator`, `MessageContexts` |
| `Schemata.Messaging.RabbitMq` | the out-of-process dispatcher, over the shared RabbitMQ connection, answering the same three dispatcher interfaces |

The former standalone command/query package and the former standalone plain-dispatch package are
both gone, folded into this one as a base rather than an opt-in layer: the command/query
contracts, the dual advisor chains, and `InProcessRequestDispatcher` all live here now, and every
module that needs request/reply consumes this package directly.

## The dispatcher is the base — no builder activation call required

Earlier revisions of this domain required an explicit builder activation call before dispatch
worked anywhere. That activation step is gone entirely. `InProcessRequestDispatcher` is wired
directly by each business module's own capability extension — `AddSchemataFlow()`,
`AddSchemataScheduling()`, `AddSchemataResources()`, `AddSchemataInsight()` — with the same
four-line block:

```csharp
services.TryAddScoped<InProcessRequestDispatcher>();
services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
```

There is no feature to enable and nothing to forget: activating any of those four modules gets you
a working `IRequestDispatcher`, unconditionally. `Schemata.Messaging.Skeleton` itself ships no
`IServiceCollection` extension at all — wiring the dispatcher into DI is a module capability's
decision, kept in one place per module instead of scattered across callers.

Every entry into a command/query-shaped module's CRUD/verb-command surface goes through this same
dispatcher: the module's own facade (`IFlowRunner`, `IScheduler`, `IInsightService`) resolves
`IRequestDispatcher` and calls `SendAsync` internally, and so do the HTTP/gRPC transports for Flow,
Scheduling, and Insight, and Resource's standard CRUD verbs. There is no second, dispatcher-free
code path for those entries — equivalence between a facade call and a direct
`IRequestDispatcher.SendAsync` call is a consequence of construction, not something a test has to
bridge. See the "Internal command dispatch" note in each module's own overview
([Flow](../flow/overview.md#internal-command-dispatch),
[Scheduling](../scheduling/overview.md#internal-command-dispatch),
[Resource](../resource/overview.md#internal-command-dispatch),
[Insight](../insight/overview.md#internal-command-dispatch)).

**The one sanctioned exception is Resource's AIP-136 custom methods.** They are not in the
command/query vocabulary (M3.3's own ruling) and do not go through `IRequestDispatcher` at all —
`ResourceMethodOperationHandler` is entered directly by `ResourceMethodController`/
`ResourceCustomMethod` and is itself a pipeline root, per the ambient `AdviceContext` rule below.

## Define a request and dispatch it

```csharp
using Schemata.Messaging.Skeleton;

public sealed record PriceQuery(string Product) : IQuery<decimal>;

public sealed class PriceQueryHandler : IRequestHandler<PriceQuery, decimal>
{
    public Task<decimal> HandleAsync(PriceQuery request, CancellationToken ct = default)
        => Task.FromResult(9.99m);
}
```

Register the handler and dispatch:

```csharp
schema.ConfigureServices(services =>
    services.AddScoped<IRequestHandler<PriceQuery, decimal>, PriceQueryHandler>());

var price = await dispatcher.SendAsync<PriceQuery, decimal>(new PriceQuery("widget"), ct);
```

**Exactly one handler per request type.** Zero throws, and so does two — request/reply is one-to-one,
so silently picking a winner would hide a real registration mistake.

## `ICommand` / `IQuery` marks and the typed dispatcher aliases

A request that changes state implements `ICommand` (no result — reuses
`Schemata.Abstractions.Unit`, so a body-less command still flows through the one
`IRequestDispatcher` contract) or `ICommand<TResult>`. A side-effect-free read implements
`IQuery<TResult>` — kept distinct from a plain `IRequest<TResponse>` so the absence of side effects
is expressed in the type, not inferred from the payload.

`ICommandDispatcher` and `IQueryDispatcher` both extend `IRequestDispatcher` and add nothing but a
narrower name: `ICommandDispatcher` additionally exposes a result-less
`SendAsync<TCommand>(TCommand)` overload so a call site sending a command that returns nothing does
not have to name `Unit`; `IQueryDispatcher` carries no member of its own. All three interfaces are
separate DI registrations resolving to the **same** `InProcessRequestDispatcher` instance (or the
same `RabbitMqRequestDispatcher` instance, under the RabbitMQ provider) — the split exists so the
write path or the read path can be replaced independently, not because dispatch itself differs.

`SendAsync<TRequest, TResponse>` picks the advisor chain by inspecting the **runtime type** of
`request`, not by which dispatcher interface the caller went through or how `TRequest` is
statically typed at the call site:

| `request is …` | Chain run |
|---|---|
| `ICommand` or `ICommand<TResponse>` | `Advisor.For<ICommandAdvisor<TRequest>>()` |
| `IQuery<TResponse>` | `Advisor.For<IQueryAdvisor<TRequest>>()` |
| neither (a plain `IRequest<TResponse>`) | none — falls straight through to the handler |

A consumer that wants request/reply and nothing else pays for nothing else.

## Dual-chain advisors

```csharp
public interface ICommandAdvisor<in TCommand> : IAdvisor<TCommand>;
public interface IQueryAdvisor<in TQuery> : IAdvisor<TQuery>;
```

Both take exactly **one** type parameter. This is deliberate, not an oversight: a dispatcher
implements `IRequestDispatcher.SendAsync<TRequest, TResponse>` under the constraint
`TRequest : IRequest<TResponse>`, and that constraint does not carry a second, independent
`TResult` for the query side — a hypothetical `IQueryAdvisor<TQuery, TResult>` could not be
referenced from the code that actually runs it. One type parameter is all `Advisor.For<>()` needs
and all the dispatcher can supply.

Register an advisor with `TryAddEnumerable` and it runs, ordered by `IAdvisor.Order`, before the
handler:

```csharp
public sealed class AuditCreateOrder : ICommandAdvisor<CreateOrder>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, CreateOrder request, CancellationToken ct = default) {
        // observe, reject (throw), or short-circuit (ctx.Set<TResponse>(...) then return Handle)
        return Task.FromResult(AdviseResult.Continue);
    }
}
```

`AdviseResult.Continue` falls through to handler resolution. `AdviseResult.Handle` short-circuits
by reading the advisor's `ctx.Set<TResponse>` value back out — an advisor that returns `Handle`
without setting one throws `InvalidOperationException`, since silently returning `default(TResponse)`
would hide the mistake. Any other result is treated as `Block` and throws without invoking the
handler.

## Ambient `AdviceContext`: root establishes, downstream continues

Every dispatch establishes a fresh `AdviceContext` (`Schemata.Abstractions.Advisors`) and sets it
ambient for the call's duration through `AdviceContext.Establish`, so the advisor chain and the
handler it runs both observe the exact same instance — and any state a command advisor stashed with
`ctx.Set<T>(...)` is visible to whatever the handler runs next, in the same process. The ambient
value is restored the moment `SendAsync` returns, on every path including exceptions, and a nested
dispatch's inner `Establish` restores the outer value on its own disposal — nesting is safe by
construction (`AsyncLocal<AdviceContext?>` under a save/restore scope guard).

**The rule is one sentence: a pipeline root establishes; everything downstream only continues.**
`InProcessRequestDispatcher.SendAsync` is a pipeline root by that rule — it is the only dispatcher
implementation that does this today; `RabbitMqRequestDispatcher`'s client side has no handler to run
advisors around, and its server side, `RabbitMqRequestConsumerHost`, does not establish an ambient
context either (see [The RabbitMQ provider](#the-rabbitmq-provider) below). The other sanctioned
root is the Resource AIP-136 custom-method pipeline (`ResourceMethodOperationHandler`): it does not
go through a dispatcher, so it continues an ambient context when one already exists and establishes
its own otherwise; see [Resource](../resource/overview.md#internal-command-dispatch).

Downstream of a dispatch — a resource pipeline, a nested advisor, a handler resolved mid-dispatch —
only reads `AdviceContext.Current` and must never construct its own `AdviceContext`. A downstream
component with no ambient context to continue must throw rather than silently forking a detached
one: `ResourceAdviceContext.Create` is the canonical example, throwing
`InvalidOperationException` with a message naming the dispatcher when `AdviceContext.Current` is
`null`.

**This ambient mechanism is scoped to the command/query dispatcher and its one sanctioned
continuation point — it does not yet unify every advisor pipeline in the codebase.** Several older,
independent advisor pipelines predate it and still construct their own private, non-ambient
`AdviceContext` for their own advisor interfaces, never reading or writing `AdviceContext.Current`:
the event bus's publish/consume advisors (`IEventPublishAdvisor`, `IEventConsumeAdvisor`), the
scheduler's `JobExecutionDispatcher` (`IJobExecutionAdvisor`), Flow's transition and source advisors
(`IFlowTransitionAdvisor`, `IFlowSourceAdvisor`), and the OAuth/OIDC and Identity request handlers
(`ITokenRequestAdvisor` and friends). Each of those is a deliberately self-contained pipeline for its
own advisor type; nothing here changes their behavior, and a command/query advisor's `ctx.Set<T>`
is not visible to them.

**The ambient value does not cross a Channel or a process boundary.** State that needs to survive a
scope, thread, or process hop travels through `MessageContext.Items` and its propagators instead
(below), never through `AdviceContext`, which holds a live `IServiceProvider` and is a purely
in-process object.

## Crossing a boundary

A dispatch that crosses a DI scope, a thread or a process loses the caller's ambient state. Multi-
tenancy is the concrete case: a scope created in a background task never ran the tenancy middleware,
so its repositories resolve against the wrong tenant provider.

`MessageContext` carries that state explicitly. The sending side flattens it:

```csharp
var context = MessageContexts.Capture(callerProvider);
```

and the receiving side rebuilds it inside the new scope through the registered
`IMessageContextPropagator` implementations.

Propagators are resolved as a **collection**. An empty collection means there is nothing to rebuild,
so no part ever asks whether another part is installed. With no propagator registered, capture
returns an empty context and every restore is a no-op.

`ClaimsPrincipal` does not travel this way — it is already a field on the request records that need
it, so it crosses the boundary inside the payload.

## The RabbitMQ provider

`Schemata.Messaging.RabbitMq.AddRabbitMqRequestDispatcher(...)` replaces the in-process dispatcher
with `RabbitMqRequestDispatcher`, which implements the same three interfaces:

```csharp
services.TryAddScoped<RabbitMqRequestDispatcher>();
services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
```

**Staged registration wins.** Every module's `AddSchemata{Module}()` registers the in-process
dispatcher with `TryAdd`; `AddRabbitMqRequestDispatcher(...)` registers the RabbitMQ dispatcher the
same way. Schemata's staged registrations flush before any feature runs, so calling
`AddRabbitMqRequestDispatcher(...)` lands first and beats the in-process default — no `Replace`,
no probing, no feature ordering to get right. Call it once per application; there is no
`SchemataMessagingBuilder.UseRabbitMq(...)` chaining sugar, on purpose — that would couple this
transport to a builder type and end its ability to be used without the Schemata lifecycle at all.

**The advisor chains and ambient `AdviceContext` are an in-process concern; the RabbitMQ provider
does not run them.** `RabbitMqRequestConsumerHost` — the server-side root that receives a message —
resolves `IRequestHandler<TRequest, TResponse>` directly and invokes it; it does not run
`ICommandAdvisor<TRequest>` / `IQueryAdvisor<TRequest>` and does not call `AdviceContext.Establish`.
A command or query dispatched over the broker therefore skips the advisor chain a same-process
dispatch would run — put cross-cutting concerns that must fire for *every* delivery of a request
(authorization, auditing) in the handler itself, not only in an `ICommandAdvisor`/`IQueryAdvisor`,
if that request can also arrive over RabbitMQ. See
[RabbitMQ Event Bus](../../cookbook/rabbitmq-event-bus.md) for the full request/reply walkthrough.

## Common pitfalls

- **Registering two handlers for one request type.** It throws at dispatch, not at startup. If you
  need fan-out, you want an event, not a request.
- **Assuming `IRequest` is an `IEvent`.** It is not — outbox rows, lifecycle observers and bus
  routing never treat a request as an event.
- **Constructing a fresh `AdviceContext` instead of continuing the ambient one.** Only a pipeline
  root establishes; every downstream component reads `AdviceContext.Current` and, finding none,
  throws rather than forking a detached context that command advisors never populated.
- **Capturing a `MessageContext` on the far side of the boundary.** Capture must run in the
  *caller's* scope, before the message is queued — that is the only place the ambient state exists.
- **Expecting a builder-level messaging activation call to exist.** There is none — activating a
  command/query-shaped module (Flow, Scheduling, Resource, Insight) is what wires the dispatcher.
