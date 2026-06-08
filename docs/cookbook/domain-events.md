# Domain Events

## What you'll build

A repository committed advisor that publishes a domain event after the database commit succeeds. The advisor uses `IRepositoryCommittedAdvisor<TEntity>` so it receives the committed entity snapshot instead of publishing during the mutation pipeline.

For delivery that must survive process crashes, write an outbox record inside the same transaction and drain the outbox from a background worker. Publishing directly after commit is at-most-once.

## Prerequisites

- A working resource setup from [guides/getting-started.md](../guides/getting-started.md) (the `Student` entity and basic CRUD).
- A working event bus from [guides/event-bus.md](../guides/event-bus.md).
- Familiarity with the advisor pipeline from [documents/core/advice-pipeline.md](../documents/core/advice-pipeline.md).

## Step 1: Define the domain event

```csharp
using Schemata.Event.Skeleton;

public sealed class StudentCreated : IEvent
{
    public string StudentName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
```

Every type published over the event bus must implement `IEvent`. The wire name is assigned separately in Step 2; the CLR type name is never used as a routing key.

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

`RegisterEvent<T>(name)` stores the mapping in `IEventTypeRegistry`. `IEventBus.PublishAsync` calls `_registry.RequireName(typeof(TEvent))` before publishing; an unregistered type throws `InvalidOperationException` at call time.

**Assertion:** the application starts without throwing on `IEventTypeRegistry.RequireName`.

## Step 3: Write the committed advisor

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;

public sealed class PublishStudentCreatedAdvisor : IRepositoryCommittedAdvisor<Student>
{
    private readonly IEventBus          _bus;
    private readonly IEventTypeRegistry _registry;

    public PublishStudentCreatedAdvisor(IEventBus bus, IEventTypeRegistry registry)
    {
        _bus      = bus;
        _registry = registry;
    }

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        IRepository<Student>   repository,
        CommitChanges<Student> changes,
        CancellationToken      ct = default)
    {
        _registry.RequireName(typeof(StudentCreated));

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

`IRepositoryCommittedAdvisor<Student>` runs only after a standalone repository commit or unit-of-work commit succeeds. `changes.Added` contains the students added in that commit boundary.

`Order = 0` places this advisor before later committed advisors with higher order values. Cache eviction uses `Orders.Max`, so it runs near the end of the committed pipeline.

**Assertion:** the advisor compiles and `Order` is accessible.

## Step 4: Register the advisor

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

builder.UseSchemata(schema => {
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(
            typeof(IRepositoryCommittedAdvisor<Student>),
            typeof(PublishStudentCreatedAdvisor)));
});
```

Use `TryAddEnumerable` so the advisor is appended to the existing `IEnumerable<IRepositoryCommittedAdvisor<Student>>`. A plain `AddScoped` for the same interface would replace any previously registered advisor.

The advisor is scoped because `IEventBus` is scoped. Registering it as singleton would cause a captive dependency.

**Assertion:** `IEnumerable<IRepositoryCommittedAdvisor<Student>>` resolves from DI and contains `PublishStudentCreatedAdvisor`.

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

**Assertion:** `POST /v1/students` with a valid body logs `"Student 'Alice' created at ..."` after the repository commit succeeds.

## Step 6: Verify the commit boundary

To confirm the event is not published when the create operation is blocked, add a second advisor that blocks the create operation:

```csharp
public sealed class BlockingAdvisor : IResourceCreateAdvisor<Student>
{
    public int Order => -1;   // runs before the repository AddAsync call

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, Student entity, CancellationToken ct = default)
        => Task.FromResult(AdviseResult.Block);
}
```

Register it temporarily and confirm that `POST /v1/students` returns a blocked result and the `StudentCreatedHandler` log line does not appear. Remove the blocking advisor when done.

**Assertion:** with `BlockingAdvisor` registered, no `StudentCreated` event is dispatched.

## Common pitfalls

**Calling `PublishAsync` from a create/update/remove advisor.** Mutation advisors run before the commit boundary. If `CommitAsync` fails, the event has already been dispatched and cannot be recalled. Use a committed advisor for post-commit notifications.

**Using committed advisors for durable delivery.** Committed advisors run after the database commit, outside the database transaction. For at-least-once delivery, write an outbox row inside the transaction and publish from an outbox worker.

**Publishing unregistered event types.** Call `IEventTypeRegistry.RequireName` before publishing so a missing registration fails the committed advisor immediately.

**Scoped advisor captures a scoped bus.** `IEventBus` is scoped. If you register `PublishStudentCreatedAdvisor` as singleton, it captures the first `IEventBus` instance created and reuses it across requests. Register the advisor as scoped.

## See also

- [guides/event-bus.md](../guides/event-bus.md) - `UseEvent`, producers, consumers, handlers
- [guides/unit-of-work.md](../guides/unit-of-work.md) - explicit enlistment and committed advisors
- [cookbook/rabbitmq-event-bus.md](rabbitmq-event-bus.md) - RabbitMQ transport for cross-service events
- [cookbook/custom-advisor.md](custom-advisor.md) - authoring and registering advisors
- [documents/repository/unit-of-work.md](../documents/repository/unit-of-work.md) - committed advisor dispatch
- [documents/event/overview.md](../documents/event/overview.md) - wire-name contract and `IEventTypeRegistry`
