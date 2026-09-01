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
| When | After the unit of work commits. Committed advisors run from the commit sink; rollback runs a separate sink and never reaches this advisor. |
| What | Every entity in `Added`, `Updated` **and** `Removed`. A deleted aggregate can have raised events before it was removed. |
| Order | `Orders.Max - 1_000` — just before `Schemata.Entity.Cache` evicts at `Orders.Max`. |
| Failure | An exception from `IEventBus.PublishAsync` propagates. The commit has already landed, so a failed publish does not roll the data back. |

## Common pitfalls

- **Publishing from a mutation advisor.** `IRepositoryAddAdvisor` and friends run *before* the
  commit. Events raised there escape even when the transaction rolls back. Use this bridge instead.
- **Expecting the commit to roll back when a subscriber throws.** It cannot: the data is already
  committed by the time the advisor runs. Treat delivery as at-most-once unless you pair it with the
  event outbox.
- **Registering the advisor by hand with `AddScoped(typeof(...))`.** That replaces the advisor chain
  rather than joining it, silently disabling every other committed advisor. Call `UseEvent()`.
- **Reusing an entity instance across two commits and expecting the events twice.** The buffer is
  drained on the first commit by design.
