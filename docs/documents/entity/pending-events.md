# Pending Events

`Schemata.Entity.Event` publishes the events an entity collected during a transaction, once that
transaction has committed.

## The problem it solves

An entity that publishes an event the moment it changes state announces something that has not
happened yet. If the surrounding unit of work later rolls back, subscribers have already acted on a
fact the database never recorded.

The fix is to buffer: the entity records what it did, and something else publishes once the commit
is real.

## The contract

`IHasPendingEvents` lives in `Schemata.Event.Skeleton`:

```csharp
public interface IHasPendingEvents
{
    IReadOnlyList<IEvent> DequeuePendingEvents();
}
```

Two deliberate choices:

- **It is not in the Domain package**, so an ordinary entity can use the flush mechanism without
  adopting DDD vocabulary.
- **The element type is `IEvent`, not `IDomainEvent`**, for the same reason.

`Dequeue` both returns and clears. Draining is the caller's signal that it has taken ownership, so a
second commit of the same instance republishes nothing.

## Wiring

```csharp
services.AddRepository(typeof(Repository<>))
        .UseEntityFrameworkCore<MyDbContext>()
        .UseEvent();
```

**The container must already hold an `IEventBus` implementation.** `Schemata.Event.Foundation`
provides one, but this package depends on the contract, not that implementation — a consumer with
its own bus works too. Because the package is usable without the Schemata feature pipeline, there is
no startup-time check; the advisor takes `IEventBus` as a constructor dependency, so a missing
registration fails DI resolution on the first commit instead of quietly dropping events.

## Writing an entity that participates

Any entity may implement the interface directly:

```csharp
public sealed class Invoice : IHasPendingEvents
{
    private readonly List<IEvent> _pending = [];

    public void Settle() {
        State = InvoiceState.Settled;
        _pending.Add(new InvoiceSettled(CanonicalName));
    }

    public IReadOnlyList<IEvent> DequeuePendingEvents() {
        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }
}
```

If you are modelling aggregates, `Schemata.Domain.Skeleton.AggregateBase` already carries that
buffer plus the identity and concurrency traits:

```csharp
public sealed class Invoice : AggregateBase
{
    public void Settle() {
        State = InvoiceState.Settled;
        Raise(new InvoiceSettled(CanonicalName));
    }
}
```

Deriving from `AggregateBase` is optional and changes nothing about publication — the bridge reacts
to `IHasPendingEvents`, which both shapes satisfy.

## Semantics

| Aspect | Behaviour |
|---|---|
| When | After the mutation's unit of work commits. The advisor prepares a callback at staging time; the unit of work runs it after the transaction commits. Rollback never reaches the callback. |
| What | Every entity staged through `IResourceMutation<TEntity>` whose mutation applied — creates, updates, **and** deletes. A deleted aggregate can have raised events before it was removed. Writes that bypass `IResourceMutation<TEntity>` (direct repository writes) do not flush pending events. |
| Order | The resource segment (`CommitOrders.Resource`) — after type-level repository notifications such as cache eviction, before domain-owner sinks. Within the segment the advisor orders itself at `Orders.Max - 1_000`. |
| Failure | An exception from `IEventBus.PublishAsync` propagates. The commit has already landed, so a failed publish does not roll the data back. |

`Prepare` is side-effect free: it captures the entity and returns the publishing callback, so a
rolled-back transaction publishes nothing. Draining happens in the callback, after the commit is
durable; when several staged mutations capture the same entity, only the first callback observes
the buffered events.

## Common pitfalls

- **Publishing from a mutation advisor.** `IRepositoryAddAdvisor` and friends run *before* the
  commit. Events raised there escape even when the transaction rolls back. Use this bridge instead.
- **Expecting direct repository writes to flush events.** The bridge listens on the resource
  mutation path. Route writes through `IResourceMutation<TEntity>` (the resource layer does this
  for you) when the entity buffers events.
- **Expecting the commit to roll back when a subscriber throws.** The data is already committed
  when the callback runs. In-process publishing awaits handlers; RabbitMQ publishing awaits broker
  confirmation. The event bus provides neither a transactional outbox nor automatic publish retry,
  so applications must handle the gap between the business commit and successful publication.
- **Registering the advisor by hand with `AddScoped(typeof(...))`.** That replaces the advisor
  chain rather than joining it, silently disabling every other committed advisor. Call
  `UseEvent()`.
- **Reusing an entity instance across two commits and expecting the events twice.** The buffer is
  drained on the first commit by design.
