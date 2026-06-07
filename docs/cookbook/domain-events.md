# Domain Events

## What you'll build

An advisor that publishes a domain event to the event bus only after the database transaction commits successfully. You'll use `EnqueueAfterCommit` on the repository to guarantee the event is never published for a rolled-back write, and `IEventTypeRegistry.RequireName` to resolve the wire name at publish time.

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

Every type published over the event bus must implement `IEvent`. The wire name is assigned separately in Step 2 — the CLR type name is never used as a routing key.

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

`RegisterEvent<T>(name)` stores the mapping in `IEventTypeRegistry`. `IEventBus.PublishAsync` calls `_registry.RequireName(typeof(TEvent))` before publishing; an unregistered type throws `InvalidOperationException` at call time, not at startup.

**Assertion:** the application starts without throwing on `IEventTypeRegistry.RequireName`.

## Step 3: Write the advisor

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Resource.Foundation.Advisors;

public sealed class PublishStudentCreatedAdvisor : IResourceCreateAdvisor<Student>
{
    private readonly IEventBus          _bus;
    private readonly IEventTypeRegistry _registry;

    public PublishStudentCreatedAdvisor(IEventBus bus, IEventTypeRegistry registry)
    {
        _bus      = bus;
        _registry = registry;
    }

    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        Student       entity,
        CancellationToken ct = default)
    {
        // Resolve the wire name eagerly so a missing registration fails fast,
        // before the after-commit action is enqueued.
        var wireName = _registry.RequireName(typeof(StudentCreated));

        ctx.EnqueueAfterCommit(token => _bus.PublishAsync(
            new StudentCreated {
                StudentName = entity.Name ?? string.Empty,
                CreatedAt   = entity.CreateTime ?? DateTimeOffset.UtcNow,
            }, token));

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

`ctx.EnqueueAfterCommit` adds the publish action to the `AdviceContext`'s after-commit queue. The resource operation handler calls `ctx.DrainAfterCommitAsync()` after `repository.CommitAsync()` succeeds. If the transaction rolls back, the queue is discarded and the event is never published.

`Order = 0` places this advisor early in the create pipeline. It runs after the entity is mapped from the request but before the repository `AddAsync` call. The entity fields are already populated at this point.

**Assertion:** the advisor compiles and `Order` is accessible.

## Step 4: Register the advisor

```csharp
builder.UseSchemata(schema => {
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(
            typeof(IResourceCreateAdvisor<Student>),
            typeof(PublishStudentCreatedAdvisor)));
});
```

Use `TryAddEnumerable` so the advisor is appended to the existing `IEnumerable<IResourceCreateAdvisor<Student>>`. A plain `AddScoped` for the same interface would replace any previously registered advisor.

The advisor is scoped because `IEventBus` is scoped (it may hold a connection or a unit-of-work reference). Registering it as singleton would cause a captive dependency.

**Assertion:** `IEnumerable<IResourceCreateAdvisor<Student>>` resolves from DI and contains `PublishStudentCreatedAdvisor`.

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

**Assertion:** `POST /v1/students` with a valid body logs `"Student 'Alice' created at ..."` after the response is returned.

## Step 6: Verify the after-commit guarantee

To confirm the event is not published on rollback, add a second advisor that blocks the create operation:

```csharp
public sealed class BlockingAdvisor : IResourceCreateAdvisor<Student>
{
    public int Order => -1;   // runs before PublishStudentCreatedAdvisor

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, Student entity, CancellationToken ct = default)
        => Task.FromResult(AdviseResult.Block);
}
```

Register it temporarily and confirm that `POST /v1/students` returns a blocked result and the `StudentCreatedHandler` log line does not appear. Remove the blocking advisor when done.

**Assertion:** with `BlockingAdvisor` registered, no `StudentCreated` event is dispatched.

## Common pitfalls

**Calling `PublishAsync` directly in `AdviseAsync`.** Publishing inside the advisor body (not inside `EnqueueAfterCommit`) runs before the transaction commits. If `CommitAsync` subsequently fails, the event has already been dispatched and cannot be recalled. Always enqueue the publish action.

**`EnqueueAfterCommit` on `ctx` vs. on the repository.** `AdviceContext` (`ctx`) and `IRepository<T>.AdviceContext` are two separate bags. The resource operation handler drains `ctx.DrainAfterCommitAsync()` after commit. The repository drains its own queue after `CommitAsync`. Enqueue on `ctx` when you are inside a resource advisor; enqueue on the repository when you are inside a repository advisor.

**`IEventTypeRegistry.RequireName` throws at enqueue time, not at publish time.** Calling `RequireName` inside the `EnqueueAfterCommit` lambda means the error surfaces during the after-commit drain, after the transaction has already committed. Call `RequireName` before enqueuing so a missing registration fails the request immediately.

**Rollback discards the queue.** If the unit of work rolls back, the after-commit queue on both `AdviceContext` and the repository is discarded. This is the correct behavior for at-most-once delivery. For at-least-once delivery, write the event to an outbox table inside the same transaction and drain the outbox from a background job.

**Scoped advisor captures a scoped bus.** `IEventBus` is scoped. If you register `PublishStudentCreatedAdvisor` as singleton, it captures the first `IEventBus` instance created and reuses it across requests. Register the advisor as scoped.

## See also

- [guides/event-bus.md](../guides/event-bus.md) — `UseEvent`, producers, consumers, handlers
- [guides/unit-of-work.md](../guides/unit-of-work.md) — `BeginWork`, `CommitAsync`, after-commit drain semantics
- [cookbook/rabbitmq-event-bus.md](rabbitmq-event-bus.md) — RabbitMQ transport for cross-service events
- [cookbook/custom-advisor.md](custom-advisor.md) — authoring and registering advisors
- [documents/repository/unit-of-work.md](../documents/repository/unit-of-work.md) — `EnqueueAfterCommit` and the after-commit queue
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and `IEventTypeRegistry`
