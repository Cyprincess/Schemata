# Domain Events

## What you'll build

A repository committed advisor that publishes a domain event after the database commit succeeds. The
advisor implements `IRepositoryCommittedAdvisor<Student>`, so it receives the committed entity
snapshot once the commit boundary has closed rather than firing during the mutation pipeline.

The event bus records every publish in a durable outbox and drains it from a background dispatcher,
so delivery is at-least-once even though the advisor calls `PublishAsync` after the commit.

Production code rarely writes this advisor by hand: the `Schemata.Entity.Event` bridge ships it as
`UseEvent()` on the repository builder (Step 3). This recipe still walks the hand-rolled advisor so
the mechanism underneath is visible.

## Prerequisites

- The `Student` entity and CRUD setup from [guides/getting-started.md](../guides/getting-started.md).
- A configured event bus from [guides/event-bus.md](../guides/event-bus.md).
- Familiarity with the advisor pipeline from [documents/core/advice-pipeline.md](../documents/core/advice-pipeline.md).

## Step 1: Define the domain event

```csharp
using System;
using Schemata.Event.Skeleton;

public sealed class StudentCreated : IEvent
{
    public string         StudentName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt   { get; init; }
}
```

Every type published over the bus implements `IEvent`. The wire name is assigned in Step 2; the CLR
type name is never used as a routing key.

**Assertion:** `StudentCreated` compiles and implements `IEvent`.

## Step 2: Register the event wire name

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<StudentCreated>("students/student-created")
          .UseProducer(p => p.UseInProcess())
          .UseConsumer(c => c.UseInProcess())
          .UseHandler<StudentCreated, StudentCreatedHandler>();
});
```

`RegisterEvent<T>(name)` stores the mapping in `IEventTypeRegistry`. `PublishAsync` resolves the name
via `RequireName(type)` before recording the outbox row; an unregistered type throws
`InvalidOperationException` at the call.

**Assertion:** the application starts without throwing on `IEventTypeRegistry.RequireName`.

## Step 3: Prefer the built-in `UseEvent()` bridge

`Schemata.Entity.Event` ships the committed advisor already written. The aggregate buffers events
on itself through `IHasPendingEvents` (`Schemata.Event.Skeleton`); the bridge's
`AdviceCommittedPendingEvents<TEntity>` drains the buffer onto `IEventBus` after the commit
succeeds, at `Order = Orders.Max - 1_000`.

```csharp
using System;
using Schemata.Domain.Skeleton;

public sealed class StudentCreated : IDomainEvent   // narrows IEvent by intent only
{
    public string         StudentName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt   { get; init; }
}

public sealed class Student : AggregateBase
{
    public string? Name { get; set; }

    public void Enroll(string name)
    {
        Name = name;
        Raise(new StudentCreated { StudentName = name, CreatedAt = DateTimeOffset.UtcNow });
    }
}
```

`AggregateBase` (`Schemata.Domain.Skeleton`) implements `IAggregateRoot` and `IHasPendingEvents`:
`Raise(IEvent)` buffers the event, and `DequeuePendingEvents()` hands the buffer to the drain and
clears it. Buffering instead of publishing is the point — an event raised by a transaction that
later rolls back never reaches a subscriber. `IDomainEvent` narrows `IEvent` by intent only; the
flush collects `IEvent`, so any entity implementing `IHasPendingEvents` directly participates the
same way, with no aggregate vocabulary required.

Register the bridge on the repository builder:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;

services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseEvent();
```

`UseEvent()` appends `AdviceCommittedPendingEvents<>` through `TryAddEnumerable` as an open-generic
scoped `IRepositoryCommittedAdvisor<>`, so it joins the committed-advisor chain for every entity
type. The advisor takes `IEventBus` as a hard constructor dependency, so a missing bus registration
fails on the first commit instead of silently dropping events. It walks all three commit
collections — `Added`, `Updated`, and `Removed`: a removed aggregate can still carry events it
raised before the delete, and draining only `Added` would drop them. The hand-rolled advisor below
reads `Added` alone only because its scenario is create-only.

**Assertion:** with no advisor written by hand, committing a `Student` whose `Enroll` ran publishes
`StudentCreated` through the bus.

## Step 4: Write the committed advisor

Hand-rolling the same advisor shows what `UseEvent()` does for you:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Event.Skeleton;

public sealed class PublishStudentCreatedAdvisor : IRepositoryCommittedAdvisor<Student>
{
    private readonly IEventBus _bus;

    public PublishStudentCreatedAdvisor(IEventBus bus) { _bus = bus; }

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        IRepository<Student>   repository,
        CommitChanges<Student> changes,
        CancellationToken      ct = default)
    {
        foreach (var entity in changes.Added)
        {
            await _bus.PublishAsync(
                new StudentCreated {
                    StudentName = entity.Name ?? string.Empty,
                    CreatedAt   = entity.CreateTime ?? DateTimeOffset.UtcNow,
                },
                ct);
        }

        return AdviseResult.Continue;
    }
}
```

`IRepositoryCommittedAdvisor<Student>` runs after a standalone repository commit or a unit-of-work
commit succeeds. `CommitChanges<Student>` exposes `Added`, `Updated`, and `Removed` for that commit
boundary.

**Assertion:** the advisor compiles and `Order` is accessible.

## Step 5: Register the advisor

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

builder.UseSchemata(schema => {
    schema.ConfigureServices(services => {
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(IRepositoryCommittedAdvisor<Student>),
                typeof(PublishStudentCreatedAdvisor)));
    });
});
```

Use `TryAddEnumerable` so the advisor is appended to the existing committed-advisor set; a plain
`AddScoped` for the same interface replaces any previously registered advisor.

Register the advisor as scoped because `IEventBus` is scoped. A singleton advisor would capture a
scoped bus as a captive dependency.

**Assertion:** `IEnumerable<IRepositoryCommittedAdvisor<Student>>` resolves from DI and contains
`PublishStudentCreatedAdvisor`.

## Step 6: Implement the handler

```csharp
public sealed class StudentCreatedHandler : IEventHandler<StudentCreated>
{
    private readonly ILogger<StudentCreatedHandler> _logger;

    public StudentCreatedHandler(ILogger<StudentCreatedHandler> logger) { _logger = logger; }

    public Task HandleAsync(StudentCreated @event, CancellationToken ct)
    {
        _logger.LogInformation(
            "Student '{Name}' created at {At}", @event.StudentName, @event.CreatedAt);
        return Task.CompletedTask;
    }
}
```

The handler runs when the outbox dispatcher drains the published row, which may be after the HTTP
response has returned.

**Assertion:** `POST /v1/students` with a valid body logs `"Student 'Alice' created at ..."` shortly
after the repository commit succeeds.

## Step 7: Name the event audit row

`Schemata.Event.Foundation` routes each publish that reaches lifecycle observers through
`SchemataEventAuditObserver`. The observer sets `EventType` from the wire name, while the `Name`
and `CanonicalName` fields are resolved by the repository add
pipeline against the entity's `[CanonicalName("events/{event}")]` pattern. The built-in
canonical-name advisor at order `120_000_000` throws `ValidationException` when `Name` is empty,
so every host that wants audit persistence must register a Name advisor that runs before that
chain. The feature's audit observer constructs `EventType`; the application's advisor fills
`Name`; the repository pipeline resolves `CanonicalName`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Event.Skeleton.Entities;

public sealed class EventAuditNameAdvisor : IRepositoryAddAdvisor<SchemataEvent>
{
    public int Order => 50_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext              context,
        IRepository<SchemataEvent> repository,
        SchemataEvent              entity,
        CancellationToken          ct)
    {
        entity.Name = entity.EventType;
        return Task.FromResult(AdviseResult.Continue);
    }
}

services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataEvent>, EventAuditNameAdvisor>());
```

Trace, line by line:

- `EventAuditNameAdvisor` implements `IRepositoryAddAdvisor<SchemataEvent>`; the audit observer's
  `AddAsync` walks every `IRepositoryAddAdvisor<SchemataEvent>` registered with the host.
- `Order => 50_000_000` runs before `AdviceAddIdentifier` at `90_000_000`,
  `AdviceAddTimestamp` at `100_000_000`, `AdviceAddConcurrency` at `110_000_000`, and
  `AdviceAddCanonicalName` at `120_000_000`.
- `entity.EventType` holds the wire name the audit observer set when constructing the record.
  The wire name (e.g. `students/student-created`) copies into `entity.Name`, providing the value
  for the `events/{event}` placeholder.
- The advisor returns `AdviseResult.Continue` so the chain proceeds. `AdviceAddCanonicalName`
  then resolves `CanonicalName` to `events/{wireName}`.


**Assertion:** publishing `StudentCreated` after `EventAuditNameAdvisor` is registered writes a
`SchemataEvent` row with `CanonicalName = "events/students/student-created"`.

## Common pitfalls

**Calling `PublishAsync` from a create/update/remove advisor.** Mutation advisors run before the
commit boundary. The outbox row is recorded immediately, so if `CommitAsync` later fails the event is
already queued and the dispatcher will deliver it. Publish from a committed advisor so the row is
recorded only after the commit succeeds.

**Expecting the handler to run before `PublishAsync` returns.** `PublishAsync` records the outbox row
and returns; the handler runs later from the dispatcher. Side effects are observable asynchronously,
so handlers must be idempotent.

**Publishing unregistered event types.** Register every published type with `RegisterEvent<T>(name)`
during startup; a missing registration throws `InvalidOperationException` from the committed advisor
at publish time.

**Missing event audit Name advisor.** When `EventAuditNameAdvisor` is absent,
`InProcessEventBus.NotifyPublishedAsync` logs
`IEventLifecycleObserver.OnPublishedAsync threw for event '{EventType}'.` at `Warning`, the
`SchemataEvent` row does not persist, and `PublishAsync` returns successfully. Treat that warning
literal as an audit-misconfiguration signal.

**Scoped advisor captured as singleton.** `IEventBus` is scoped. Registering the advisor as a
singleton captures the first bus instance and reuses it across requests. Register the advisor as
scoped.

## See also

- [guides/event-bus.md](../guides/event-bus.md) — `UseEvent`, producers, consumers, handlers
- [cookbook/rabbitmq-event-bus.md](rabbitmq-event-bus.md) — RabbitMQ transport for cross-service events
- [documents/event/dispatch-pipeline.md](../documents/event/dispatch-pipeline.md) — the outbox and dispatcher
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and `IEventTypeRegistry`
