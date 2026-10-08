# Unit of Work

A unit of work coordinates multiple repository mutations within one database transaction.
`IUnitOfWork` provides commit and rollback; repositories enlist through `IRepository.Join`. A
repository that mutates without enlisting opens its own implicit unit of work, so a single
`repository.CommitAsync()` is transactional on its own.

## Where the code lives

| Item                                             | Path                                                                            |
| ------------------------------------------------ | ------------------------------------------------------------------------------- |
| `IUnitOfWork`, `IUnitOfWork<TContext>`           | `src/Schemata.Entity.Repository/IUnitOfWork.cs`                                 |
| `IRepository.Begin` / `Join` / `CommitAsync`     | `src/Schemata.Entity.Repository/IRepository.cs`                                 |
| `MutationResult`                                 | `src/Schemata.Entity.Repository/MutationResult.cs`                              |
| `CommitOrders`                                   | `src/Schemata.Entity.Repository/CommitOrders.cs`                                |
| `IRepositoryCommittedAdvisor<TEntity>`           | `src/Schemata.Entity.Repository/Advisors/IRepositoryCommittedAdvisor.cs`        |
| `IResourceMutationCommittedAdvisor<TEntity>`     | `src/Schemata.Entity.Repository/Advisors/IResourceMutationCommittedAdvisor.cs`  |

## IUnitOfWork interface

```csharp
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    void AddCommitSink(int order, Func<CancellationToken, Task> sink);
    void AddRollbackSink(Action reset);
    void AddSavePreparation(Action preparation);
    Task CommitAsync(CancellationToken ct = default);
}

public interface IUnitOfWork<TContext> : IUnitOfWork
{
    TContext Context { get; }
}
```

| Member                     | Purpose                                                                                                                                              |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AddCommitSink`            | Registers an ordered callback that runs after the unit of work commits its transaction; enlisted repositories use it to dispatch committed advisors. |
| `AddRollbackSink`          | Registers a callback that runs when the unit of work rolls back or is disposed before completion; enlisted repositories use it to reset write state. |
| `AddSavePreparation`       | Registers a scalar value projection on already-enlisted entities, applied when provider-generated write values are final. See [Save preparations](#save-preparations). |
| `CommitAsync`              | Persists the enlisted repositories' changes, commits the transaction, then runs the commit sinks in ascending order.                                  |
| `RollbackAsync`            | Rolls back the transaction and runs each enlisted rollback sink.                                                                                     |
| `Context`                  | The provider context (`DbContext` or `DataConnection`). First access opens the connection; the transaction opens per the provider's execution model. |
| `Dispose` / `DisposeAsync` | Rolls back when never committed, then releases the context.                                                                                          |

Commit sinks are ordered: the unit of work keeps them sorted by `order` (equal orders keep
registration sequence) and runs every sink even when one fails, rethrowing the collected errors
afterwards (a single error directly, several as an `AggregateException`). `CommitOrders` reserves
the segments: `Repository` for type-level committed notifications, `Resource` for resource-mutation
callbacks (pending events, report schedule sync), `Domain` for sinks enlisted directly by domain
owners. The sink registrations are required members of the contract, so every compliant
`IUnitOfWork` implementation carries commit and rollback semantics.

A unit of work is one-shot: after `CommitAsync` or `RollbackAsync`, resolve a fresh `IUnitOfWork`
from DI to start another transaction. `IUnitOfWork<TContext>` binds the type parameter to a concrete
context so multiple provider types coexist in one container.

## Starting and enlisting

Repository transaction entry points preserve commit ownership:

- `IRepository.Begin()` creates a provider unit of work, enlists the calling repository, and
  returns it. The caller owns the returned unit of work: commit or roll it back explicitly, and
  dispose it.
- `IRepository.Join(uow)` enlists the repository in a unit of work resolved separately (typically
  from the DI scope), letting several repositories share one transaction.
- `IResourceMutation<TEntity>` accepts an optional unit of work: with one, the mutation joins and
  only stages; without one, the mutation begins, commits, and disposes its own transaction.

```csharp
public sealed class EnrollmentService(
    IRepository<Student>      students,
    IRepository<Course>       courses,
    IUnitOfWork<AppDbContext> uow)
{
    public async Task EnrollAsync(Student student, Course course, CancellationToken ct)
    {
        students.Join(uow);
        courses.Join(uow);

        await students.AddAsync(student, ct);
        await courses.AddAsync(course, ct);

        await uow.CommitAsync(ct);
    }
}
```

`Join` replaces the repository's owned context with the unit of work's context and registers the
repository's commit and rollback sinks through the required `AddCommitSink` / `AddRollbackSink`
members. While enlisted, `repository.CommitAsync()` throws
`InvalidOperationException` — commit through `uow.CommitAsync()`. `Join` also throws if the
repository already holds uncommitted work or is already enlisted; joining the same transaction twice
is idempotent.

`IUnitOfWork<TContext>` is typically registered scoped (via `.WithUnitOfWork<TContext>()`), so every
injection in one request resolves the same instance.

## Standalone commit

A repository that mutates without enlisting opens an implicit unit of work on the first write and
commits it through `repository.CommitAsync()`:

```csharp
await students.AddAsync(student, ct);
await students.CommitAsync(ct);
```

`CommitAsync` commits that implicit unit of work and reopens the repository, so the same instance
takes further writes — see [Reopening after commit](#reopening-after-commit). A commit with no
staged writes is a no-op and sends no committed notification.

## Committed notifications

Two post-commit extension points exist, with different owners:

- `IRepositoryCommittedAdvisor<TEntity>` is a type-level notification: it runs once per committing
  repository enlistment that staged at least one write, and receives only the repository. The cache
  package's `AdviceCommittedEvictCache<TEntity>` uses it to publish a new entity-type cache
  generation. A commit with no writes never fires it.
- `IResourceMutationCommittedAdvisor<TEntity>` runs on the resource-mutation path: after a mutation
  stages successfully, `ResourceMutation<TEntity>` calls `Prepare(entity, operation)` and enlists
  the returned callback at the `CommitOrders.Resource` segment. `Prepare` must be side-effect free;
  the callback runs after the transaction commits.

The repository registers its committed-notification sink on the first staged write of an
enlistment, so repository-segment sinks always run before resource-segment callbacks. When a commit
sink throws, the unit of work runs the remaining sinks and rethrows afterwards; the database stays
committed and the mutation never replays.

## Reopening after commit

A repository that opened its own unit of work — the implicit one a standalone write enlists —
reopens as soon as that unit of work commits. The same injected `IRepository<T>` therefore serves
several write-then-commit cycles in one scope, and the next write enlists a fresh unit of work:

```csharp
await repository.AddAsync(first);
await repository.CommitAsync();

await repository.AddAsync(second);   // stages into a new unit of work
await repository.CommitAsync();
```

Reads issued between a commit and the next write still resolve against the committed context.

A unit of work supplied through `Begin()` or `Join(uow)` belongs to the caller and never reopens:
once it completes, further work on that repository throws. Resolve a fresh `IRepository<T>` for new
work.

`RepositoryTokenStore` resolves and disposes a fresh transient repository for each independent
operation. Family publication, rotation, invalidation, and redemption own their transaction;
CAS classification reads fresh state, and replay cleanup can issue the next revocation through
the same store instance. `SecurityStore<T>` keeps management writes on `IResourceMutation<T>`;
its publication participant joins the actual write transaction. Revocation and deletion carry
no publication fence.

## Concurrency stamps

EF Core preserves the caller's concurrency stamp while staging an update and uses it as the
original value guarding the write. Immediately before `SaveChangesAsync`, its unit of work rotates
the stamp on each `Modified` `IConcurrency` entry. A failed save restores the caller-visible stamp
before rollback sinks run. LinqToDB executes its guarded update and rotates the stamp during
staging, inside the active transaction. A successful commit followed by a failing commit sink
leaves the committed stamp intact in both providers. The implementations are
`src/Schemata.Entity.EntityFrameworkCore/EfCoreUnitOfWork.cs` and
`src/Schemata.Entity.LinqToDB/LinqToDbRepository.cs`.

## Save preparations

`AddSavePreparation` registers a value projection that synchronizes scalar derived values on
entities already enlisted in the unit of work, at the point where provider-generated write values
(the rotated concurrency stamps) are final. It exists for dependent rows that must record another
entity's final stamp inside the same transaction — the canonical consumer is Flow source binding,
which projects the bound entity's committed stamp onto its binding rows.

Provider timing follows the execution model:

- EF Core queues the preparation and runs it at the commit boundary, after the stamp rotation and
  before `SaveChangesAsync`; the save observes the projected values. A throwing preparation fails
  the commit: stamps restore and the transaction rolls back.
- LinqToDB has already executed the staged write inside the open transaction, so the preparation
  runs synchronously at registration; stage the dependent write that persists the projected value
  afterwards. A throwing preparation surfaces at registration, and disposing the uncommitted unit
  of work rolls the transaction back.

A preparation must only mutate scalar values on entities the caller already holds. It must not
stage or execute writes, resolve services, register further callbacks, or produce external
effects. Registering on a completed or disposed unit of work throws.

## Rollback and disposal

`RollbackAsync` rolls back the transaction and runs each repository's rollback sink, which resets
its staged write state. Disposing a unit of work that never committed rolls back the same way. Both
are safe to call after the unit of work has already completed. A repository enlisted in an
externally supplied unit of work does not dispose that unit of work — the caller owns its lifetime.

## Registration

```csharp
services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlite(connectionString))
        .WithUnitOfWork<AppDbContext>();
```

`AddRepository<TEntity, TImplementation>()` is the only registration overload: call it once per
entity type. It also registers the default `IResourceMutation<>` owner with `TryAddScoped`, so a
closed domain registration wins over the default. `.WithUnitOfWork<TContext>()` registers
`IUnitOfWork<TContext>` as scoped with `TryAddScoped`, so a prior registration wins. Both providers
expose the same method.

## See also

- [overview.md](overview.md) — repository API and suppression scopes
- [mutation-pipeline.md](mutation-pipeline.md) — mutation advisors, write results, and the resource
  mutation owner
- [providers.md](providers.md) — EF Core and LinqToDB unit-of-work implementations
