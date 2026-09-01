# Messaging

Dispatch a typed request to exactly one handler, mark a request as a write or a read with
`ICommand`/`IQuery`, and hook cross-cutting behavior onto dispatch with advisors. This guide
extends the Student application from [Getting Started](getting-started.md): the query it
dispatches reads through the same `Student` repository.

## Add the package

`Schemata.Application.Complex.Targets` references `Schemata.Resource.Foundation`, which
references `Schemata.Messaging.Skeleton` directly. The package installed in Getting Started
already carries the request contracts and the in-process dispatcher this guide uses.

## Requests, commands, and queries

Every dispatched payload implements `IRequest<TResponse>` from `Schemata.Messaging.Skeleton`: one
type, answered by exactly one handler. `ICommand`/`ICommand<TResult>` mark a request that changes
state; `IQuery<TResult>` marks a side-effect-free read:

```csharp
using Schemata.Abstractions;

namespace Schemata.Messaging.Skeleton;

public interface IRequest<TResponse> : IMessage;

public interface ICommand : IRequest<Unit>;
public interface ICommand<TResult> : IRequest<TResult>;

public interface IQuery<TResult> : IRequest<TResult>;
```

`ICommand` reuses `Schemata.Abstractions.Unit` as its response, so a command with no result still
flows through the same `IRequestDispatcher` contract instead of needing a parallel dispatch path.
A plain `IRequest<TResponse>` that is neither a command nor a query dispatches the same way, only
without an advisor chain — see [Advise a command or query](#advise-a-command-or-query).

## Define a query and dispatch it

Create `GetStudentAgeQuery.cs`:

```csharp
using Schemata.Messaging.Skeleton;

public sealed record GetStudentAgeQuery(string StudentName) : IQuery<int?>;
```

Create `GetStudentAgeHandler.cs`. Implement `IQueryHandler<TQuery, TResult>` — a bare
`IRequestHandler<GetStudentAgeQuery, int?>` works identically, since `IQueryHandler` adds no
members of its own:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;

public sealed class GetStudentAgeHandler(IRepository<Student> repository)
    : IQueryHandler<GetStudentAgeQuery, int?>
{
    public async Task<int?> HandleAsync(GetStudentAgeQuery request, CancellationToken ct = default)
    {
        return await repository.FirstOrDefaultAsync(
            q => q.Where(s => s.Name == request.StudentName).Select(s => (int?)s.Age), ct);
    }
}
```

Register the handler against `IRequestHandler<,>` — dispatch resolves by that interface, never by
`IQueryHandler<,>`:

```csharp
schema.ConfigureServices(services =>
    services.AddScoped<IRequestHandler<GetStudentAgeQuery, int?>, GetStudentAgeHandler>());
```

Inject `IQueryDispatcher` (or the plain `IRequestDispatcher`) and dispatch:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

public sealed class StudentAgeService(IQueryDispatcher dispatcher)
{
    public Task<int?> GetAgeAsync(string studentName, CancellationToken ct = default)
        => dispatcher.SendAsync<GetStudentAgeQuery, int?>(new GetStudentAgeQuery(studentName), ct);
}
```

Exactly one handler may be registered per request type. Zero handlers throws
`InvalidOperationException` at dispatch time, and so does two — request/reply is one-to-one, so a
second handler is a registration mistake rather than a fan-out.

## The dispatcher trio

Three interfaces reach the same dispatch:

- `IRequestDispatcher.SendAsync<TRequest, TResponse>` dispatches any request.
- `ICommandDispatcher` extends it and adds a result-less `SendAsync<TCommand>(TCommand)` overload
  constrained to `ICommand`, so a call site sending a command that returns nothing does not have
  to name `Unit`.
- `IQueryDispatcher` extends `IRequestDispatcher` and adds nothing of its own — it exists so the
  read path is a separate DI registration from the write path, replaceable on its own.

All three resolve to the same `InProcessRequestDispatcher` instance.
`Schemata.Messaging.Skeleton` ships no `IServiceCollection` extension of its own; wiring the
dispatcher is each business module's job. Activating any of Flow, Scheduling, Resource, or
Insight — `UseResource()` in this guide's `Program.cs` — registers the same four lines:

```csharp
services.TryAddScoped<InProcessRequestDispatcher>();
services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
```

`TryAdd` makes every module's registration idempotent: activating a second module never conflicts
with the first. An application that dispatches requests without Flow, Scheduling, Resource, or
Insight registers `InProcessRequestDispatcher` (from `Schemata.Messaging.Skeleton.Internal`)
itself, with the same four lines.

## Advise a command or query

`SendAsync` inspects the request instance's runtime type — not the dispatcher interface the
caller went through — to pick the advisor chain:

| `request is …`                      | Chain run                             |
| ------------------------------------ | -------------------------------------- |
| `ICommand` or `ICommand<TResponse>`  | `ICommandAdvisor<TRequest>`            |
| `IQuery<TResponse>`                  | `IQueryAdvisor<TRequest>`              |
| neither                              | none — falls straight to the handler   |

Both advisor interfaces take one type parameter — `ICommandAdvisor<in TCommand>` and
`IQueryAdvisor<in TQuery>` — and register with `TryAddEnumerable`, ordered by ascending
`IAdvisor.Order`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

public sealed class LoggingCommandAdvisor<TCommand>(ILogger<LoggingCommandAdvisor<TCommand>> logger)
    : ICommandAdvisor<TCommand>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, TCommand request, CancellationToken ct = default)
    {
        logger.LogInformation("Dispatching {Command}", typeof(TCommand).Name);
        return Task.FromResult(AdviseResult.Continue);
    }
}
```

Register it open-generic to run for every command, not one:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped(typeof(ICommandAdvisor<>), typeof(LoggingCommandAdvisor<>)));
```

`AdviseResult.Continue` falls through to the handler. `AdviseResult.Handle` short-circuits: the
advisor calls `ctx.Set<TResponse>(...)` and returns `Handle`; the dispatcher reads the value back
with `ctx.TryGet<TResponse>` and returns it without resolving a handler at all. An advisor that
returns `Handle` without setting a value throws `InvalidOperationException`, since silently
returning `default(TResponse)` would hide the mistake. Any other result is treated as `Block` and
throws without invoking the handler. An empty advisor chain costs only the `GetServices` call that
finds it empty.

## The built-in command surface

Each business module's own request types already implement `ICommand`/`IQuery`. A
command/query-shaped module dispatches its entire write surface through `IRequestDispatcher`, so
an advisor written against one of its request types intercepts every entry point that reaches it —
the module's own facade, HTTP/gRPC transports, and event or timer bridges alike:

| Module     | Count | A few of them                                                          |
| ---------- | ----- | ------------------------------------------------------------------------ |
| Flow       | 8     | `StartProcessRequest`, `CompleteActivityRequest`, `ThrowSignalRequest`   |
| Scheduling | 5     | `ScheduleJobRequest`, `TriggerJobRequest`, `RescheduleJobRequest`, `StageJobExecutionResultRequest` |
| Resource   | 5     | `CreateResourceRequest<,,>`, `UpdateResourceRequest<,,>`                |
| Insight    | 1     | `QueryInsightRequest`                                                    |

Hook an advisor directly onto one of them, the same way as a custom command:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Foundation.Commands;
using Schemata.Messaging.Skeleton.Advisors;

public sealed class AuditCompleteActivity : ICommandAdvisor<CompleteActivityRequest>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, CompleteActivityRequest request, CancellationToken ct = default)
    {
        // audit, then continue
        return Task.FromResult(AdviseResult.Continue);
    }
}
```

registered the same `TryAddEnumerable` way as any other `ICommandAdvisor<TCommand>`.

## Ambient AdviceContext

`SendAsync` builds a fresh `AdviceContext` and makes it ambient for the call's duration through
`AdviceContext.Establish` before it runs the advisor chain, so the chain and the handler it
invokes observe the same instance — a command advisor's `ctx.Set<T>(...)` is visible to the
handler through `AdviceContext.Current`. The ambient value is restored the moment `SendAsync`
returns, on every path including exceptions.

`InProcessRequestDispatcher.SendAsync` is a **pipeline root**: it establishes. Everything it calls
downstream — the handler, a nested advisor — only reads `AdviceContext.Current` and must never
construct its own `AdviceContext`. A custom pipeline root (a background job runner, a message
consumer that is not `InProcessRequestDispatcher`) follows the same rule explicitly:

```csharp
var ctx = new AdviceContext(services);
using var _ = AdviceContext.Establish(ctx);
```

A nested `Establish` inside that scope restores the outer value on its own disposal, so nesting
composes safely.

## Distributed dispatch

*Skippable — replaces the in-process dispatcher with a RabbitMQ transport.*

`Schemata.Messaging.RabbitMq.AddRabbitMqRequestDispatcher(configure)` registers
`RabbitMqRequestDispatcher` against the same three interfaces:

```csharp
services.TryAddScoped<RabbitMqRequestDispatcher>();
services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
```

Schemata's staged registrations flush before any feature runs, so `AddRabbitMqRequestDispatcher`
lands first and beats a module's own in-process `TryAdd` regardless of call order. That
registration owns only the *outbound* side: `RabbitMqRequestDispatcher` becomes the
`IRequestDispatcher`/`ICommandDispatcher`/`IQueryDispatcher` a sender resolves. On the consuming
side, `RabbitMqRequestConsumerHost` opens a scope per delivery, restores ambient state through the
registered `IMessageContextPropagator` implementations, then resolves `InProcessRequestDispatcher`
by its concrete type — never through those three interfaces, which would just republish the
delivery back onto the broker — and calls its `SendAsync`. That establishes a fresh
`AdviceContext` for the delivery and runs the same advisor chain a local dispatch would, before
invoking the handler.

`Scheduling`'s fifth command, `StageJobExecutionResultRequest` (`Schemata.Scheduling.Foundation.Commands`), needs the same explicit wire name as any other request once the RabbitMQ dispatcher is active: it carries a finished execution's job-row result to the scheduling writer (see [Scheduling](scheduling.md)), and the application registers it beside its own bindings:

```csharp
using Schemata.Abstractions;
using Schemata.Scheduling.Foundation.Commands;

options.Register<StageJobExecutionResultRequest, Unit>("scheduling.stage-job-execution-result");
```

The wire name is the application's choice. The framework registers no wire name on its own; with the RabbitMQ dispatcher active, a staging send whose type is unregistered fails at dispatch with `InvalidOperationException`.

Crossing a DI scope, thread, or process drops ambient state. `MessageContexts.Capture(callerProvider)`
flattens it into a `MessageContext` on the sending side; the registered
`IMessageContextPropagator` implementations rebuild it on the far side. With none registered,
capture returns an empty context and every restore is a no-op.

## Common pitfalls

- **Registering an advisor for a request that is not a command or query.**
  `ICommandAdvisor<SomeNonCommandRequest>` never runs — the dispatcher only looks up that chain
  for a request that actually implements `ICommand`/`ICommand<T>`.
- **Returning `Handle` without setting a result.** Call `ctx.Set<TResponse>(...)` first, then
  return `Handle`. Otherwise the dispatcher throws `InvalidOperationException` rather than
  guessing a default value.
- **Registering two handlers for one request type.** The mismatch surfaces at dispatch, not at
  startup. Fan-out belongs to an event, not a request.
- **Constructing a fresh `AdviceContext` outside a pipeline root.** A component that only
  continues an existing dispatch reads `AdviceContext.Current`; only the dispatcher, or your own
  explicit root, calls `Establish`.

## Next steps

- [Actor](actor.md) — serialize concurrent writers to the same process instance
- [Flow](flow.md) — the process engine's own command surface
- [Scheduling](scheduling.md) — the job engine's own command surface

## See also

- [Messaging Overview](../documents/messaging/overview.md) — the full request/command/query reference
- [Advice Runtime](../documents/advice/runtime.md) — the generic advisor pipeline `AdviceContext` drives
