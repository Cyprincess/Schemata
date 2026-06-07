# Event Bus

Add an in-process event bus to the Student CRUD app. This guide shows how to register an event type, wire up a producer and consumer, and handle events with a typed handler. This guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Event.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Event.Foundation
```

## Enable the event bus

`UseEvent()` takes no delegate and returns an `EventBuilder` for chaining. `SchemataEventFeature` runs at `Order = Priority = 440_000_000`:

```csharp
schema.UseEvent()
      .RegisterEvent<StudentEnrolled>("students/student-enrolled")
      .UseProducer(p => p.UseInProcess())
      .UseConsumer(c => c.UseInProcess())
      .UseHandler<StudentEnrolled, StudentEnrolledHandler>();
```

Each method on `EventBuilder` returns the same builder for chaining.

## Define the event

Create `StudentEnrolled.cs`. Event types must implement `IEvent`:

```csharp
using Schemata.Event.Skeleton;

public sealed class StudentEnrolled : IEvent
{
    public string? StudentName { get; set; }
    public int     Age         { get; set; }
}
```

## Register the event

`RegisterEvent<TEvent>(string name)` maps the CLR type to a wire name. The wire name is a distributed contract — publishers and consumers use it to route payloads. Unregistered types throw on publish:

```csharp
.RegisterEvent<StudentEnrolled>("students/student-enrolled")
```

The wire name is stored in `EventContext.EventType` and in the `SchemataEvent.EventType` audit column — the same string everywhere.

## Configure producer and consumer

`UseProducer(p => p.UseInProcess())` registers `InProcessEventBus` as `IEventBus` (scoped). `UseConsumer(c => c.UseInProcess())` registers `InMemoryEventSubscriptionStore` and `HandlerResolver` for in-process dispatch:

```csharp
.UseProducer(p => p.UseInProcess())
.UseConsumer(c => c.UseInProcess())
```

For RabbitMQ in production, see the [RabbitMQ Event Bus](../cookbook/rabbitmq-event-bus.md) cookbook recipe.

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

`UseHandler` registers `StudentEnrolledHandler` as `IEventHandler<StudentEnrolled>` (scoped). `IRequestHandler<TRequest, TResponse>` is also supported for request/reply patterns — note that only a single handler per request type is supported; multiple registrations will fail.

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

## Verify

```shell
dotnet run
```

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

If you publish from an advisor or service wired to the create pipeline, the console should print:

```text
Student enrolled: Alice, age 20
```

## See also

- [Flow](flow.md) — previous in the series: BPMN catch events that bridge to this bus
- [Scheduling](scheduling.md) — next in the series: scheduled jobs publish lifecycle events here
- [Event Overview](../documents/event/overview.md) — wire names, `IEventTypeRegistry`, dispatch pipeline
- [Event Providers](../documents/event/providers.md) — InProcess and RabbitMQ details
- [RabbitMQ Event Bus](../cookbook/rabbitmq-event-bus.md) — production-ready RabbitMQ setup
