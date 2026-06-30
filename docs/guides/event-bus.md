# Event Bus

Add an in-process event bus to the Student CRUD app: register an event type, wire a producer and
consumer, and handle the event with a typed handler. This guide builds on
[Getting Started](getting-started.md).

## Add the package

The event bus ships outside the meta target packages, so add it explicitly:

```shell
dotnet add package --prerelease Schemata.Event.Foundation
```

## Enable the event bus

`UseEvent()` takes no delegate and returns an `EventBuilder`. Chain the configuration:

```csharp
schema.UseEvent()
      .RegisterEvent<StudentEnrolled>("students/student-enrolled")
      .UseProducer(p => p.UseInProcess())
      .UseConsumer(c => c.UseInProcess())
      .UseHandler<StudentEnrolled, StudentEnrolledHandler>();
```

Each `EventBuilder` method returns the same builder.

## Define the event

Create `StudentEnrolled.cs`. Event types implement `IEvent`:

```csharp
using Schemata.Event.Skeleton;

public sealed class StudentEnrolled : IEvent
{
    public string? StudentName { get; set; }
    public int     Age         { get; set; }
}
```

## Register the event

`RegisterEvent<TEvent>(string name)` maps the CLR type to a wire name — the routing string that
publishers and consumers share. The wire name lands in `EventContext.EventType` and the
`SchemataEvent.EventType` audit column, the same string everywhere. An unregistered type throws when
you publish it:

```csharp
.RegisterEvent<StudentEnrolled>("students/student-enrolled")
```

## Configure producer and consumer

`UseProducer(p => p.UseInProcess())` registers `InProcessEventBus` as `IEventBus`.
`UseConsumer(c => c.UseInProcess())` registers the subscription store, handler resolver, and dispatch
context for in-process delivery:

```csharp
.UseProducer(p => p.UseInProcess())
.UseConsumer(c => c.UseInProcess())
```

The in-process consumer persists subscriptions through `IRepository<SchemataEventSubscription>`, so a
persistence provider (the EF Core setup from Getting Started) must be configured. For RabbitMQ in
production, see the [RabbitMQ Event Bus](../cookbook/rabbitmq-event-bus.md) recipe.

## Create the handler

Create `StudentEnrolledHandler.cs`. Implement `IEventHandler<TEvent>`:

```csharp
using Schemata.Event.Skeleton;

public sealed class StudentEnrolledHandler : IEventHandler<StudentEnrolled>
{
    public Task HandleAsync(StudentEnrolled @event, CancellationToken ct)
    {
        Console.WriteLine($"Student enrolled: {@event.StudentName}, age {@event.Age}");
        return Task.CompletedTask;
    }
}
```

Register it with `UseHandler<TEvent, THandler>()`:

```csharp
.UseHandler<StudentEnrolled, StudentEnrolledHandler>()
```

The handler is registered as scoped.

## Publish an event

Inject `IEventBus` and call `PublishAsync`:

```csharp
using Schemata.Event.Skeleton;

public sealed class EnrollmentService(IEventBus bus)
{
    public async Task EnrollAsync(string name, int age, CancellationToken ct)
    {
        // ... persist the student ...

        await bus.PublishAsync(new StudentEnrolled {
            StudentName = name,
            Age         = age,
        }, ct);
    }
}
```

`PublishAsync` records the event in a durable outbox and returns immediately — it does not run the
handler inline. A background dispatcher drains the outbox and invokes `StudentEnrolledHandler` a
moment later. The handler may run after `EnrollAsync` has already returned, so write handlers to be
idempotent.

## Verify

```shell
dotnet run
```

```shell
curl -X POST http://localhost:5000/v1/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

If you publish `StudentEnrolled` from a service wired to the create pipeline, the console prints,
shortly after the response, once the outbox dispatcher drains the row:

```text
Student enrolled: Alice, age 20
```

## Next steps

- [Scheduling](scheduling.md) — scheduled jobs publish lifecycle events through this bus
- [Flow](flow.md) — BPMN `Message`/`Signal` catches correlate against the same bus
- [Modular](modular.md) — package the handler in a self-contained module

## See also

- [Event Overview](../documents/event/overview.md) — wire names, the outbox, `IEventTypeRegistry`
- [Domain Events](../cookbook/domain-events.md) — publish an event after a repository commit
