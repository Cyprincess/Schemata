# Domain Events

## What you'll build

A repository committed advisor that publishes a domain event after the database commit succeeds. The
advisor implements `IRepositoryCommittedAdvisor<Student>`, so it receives the committed entity
snapshot once the commit boundary has closed rather than firing during the mutation pipeline.

The event bus records every publish in a durable outbox and drains it from a background dispatcher,
so delivery is at-least-once even though the advisor calls `PublishAsync` after the commit.

## Prerequisites

- The `Student` entity and CRUD setup from [guides/getting-started.md](../guides/getting-started.md).
- A configured event bus from [guides/event-bus.md](../guides/event-bus.md).
- Familiarity with the advisor pipeline from [documents/core/advice-pipeline.md](../documents/core/advice-pipeline.md).

## Step 1: Define the domain event

```csharp
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

## Step 3: Write the committed advisor

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

## Step 4: Register the advisor

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

## Step 5: Implement the handler

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

**Scoped advisor captured as singleton.** `IEventBus` is scoped. Registering the advisor as a
singleton captures the first bus instance and reuses it across requests. Register the advisor as
scoped.

## See also

- [guides/event-bus.md](../guides/event-bus.md) — `UseEvent`, producers, consumers, handlers
- [cookbook/rabbitmq-event-bus.md](rabbitmq-event-bus.md) — RabbitMQ transport for cross-service events
- [documents/event/dispatch-pipeline.md](../documents/event/dispatch-pipeline.md) — the outbox and dispatcher
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and `IEventTypeRegistry`
