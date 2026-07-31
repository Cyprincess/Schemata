# Unit of Work

A unit of work coordinates multiple repository mutations within one database transaction.
`IUnitOfWork` provides commit and rollback; repositories enlist through `IRepository.Join`. A
repository that mutates without enlisting opens its own implicit unit of work, so a single
`repository.CommitAsync()` is transactional on its own.

## Where the code lives

| Item                                         | Path                                                                     |
| -------------------------------------------- | ------------------------------------------------------------------------ |
| `IUnitOfWork`, `IUnitOfWork<TContext>`       | `src/Schemata.Entity.Repository/IUnitOfWork.cs`                          |
| `IRepository.Begin` / `Join` / `CommitAsync` | `src/Schemata.Entity.Repository/IRepository.cs`                          |
| `CommitChanges<TEntity>`                     | `src/Schemata.Entity.Repository/CommitChanges.cs`                        |
| `IRepositoryCommittedAdvisor<TEntity>`       | `src/Schemata.Entity.Repository/Advisors/IRepositoryCommittedAdvisor.cs` |

## IUnitOfWork interface

```csharp
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    void AddCommitSink(Func<CancellationToken, Task> sink);
    void AddRollbackSink(Action reset);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface IUnitOfWork<TContext> : IUnitOfWork
{
    TContext Context { get; }
}
```

| Member                     | Purpose                                                                                                                                              |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AddCommitSink`            | Registers a callback that runs after the unit of work commits its transaction; enlisted repositories use it to dispatch their committed advisors.    |
| `AddRollbackSink`          | Registers a callback that runs when the unit of work rolls back or is disposed before completion; enlisted repositories use it to reset tracking.    |
| `CommitAsync`              | Persists the enlisted repositories' changes, commits the transaction, then dispatches each repository's committed advisors.                          |
| `RollbackAsync`            | Rolls back the transaction and resets each enlisted repository's tracking lists.                                                                     |
| `Context`                  | The provider context (`DbContext` or `DataConnection`). First access opens the connection; the transaction opens per the provider's execution model. |
| `Dispose` / `DisposeAsync` | Rolls back when never committed, then releases the context.                                                                                          |

The sink registrations are required members of the contract, so every compliant `IUnitOfWork`
implementation carries commit and rollback semantics — there is no optional side interface to miss.

A unit of work is one-shot: after `CommitAsync` or `RollbackAsync`, resolve a fresh `IUnitOfWork` from
DI to start another transaction. `IUnitOfWork<TContext>` binds the type parameter to a concrete context
so multiple provider types coexist in one container.

## Starting and enlisting

Two entry points open a unit of work:

- `IRepository.Begin()` creates a provider unit of work, enlists the calling repository, and returns it.
- `IRepository.Join(uow)` enlists the repository in a unit of work resolved separately (typically from
  the DI scope), letting several repositories share one transaction.

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
`InvalidOperationException` — commit through `uow.CommitAsync()`. `Join` also throws if the repository
already holds uncommitted work or is already enlisted.

`IUnitOfWork<TContext>` is typically registered scoped (via `.WithUnitOfWork<TContext>()`), so every
injection in one request resolves the same instance.

## Standalone commit

A repository that mutates without enlisting opens an implicit unit of work on the first write
(`EnsureWriteUnitOfWork`) and commits it through `repository.CommitAsync()`:

```csharp
await students.AddAsync(student, ct);
await students.CommitAsync(ct);
```

`CommitAsync` commits that implicit unit of work and reopens the repository, so the same instance takes
further writes — see [Reopening after commit](#reopening-after-commit). A repository owned but never
mutated still dispatches an empty committed snapshot on commit, so committed advisors observe the no-op
on the same footing as the enlisted path.

## Committed advisor pipeline

`IRepositoryCommittedAdvisor<TEntity>` runs after a successful commit boundary. It receives a typed
snapshot:

```csharp
public sealed class CommitChanges<TEntity> where TEntity : class
{
    public IReadOnlyList<TEntity> Added   { get; init; }
    public IReadOnlyList<TEntity> Updated { get; init; }
    public IReadOnlyList<TEntity> Removed { get; init; }
}
```

A standalone `repository.CommitAsync()` snapshots the repository's tracked adds, updates, and removes,
then dispatches committed advisors after persistence. `uow.CommitAsync()` invokes the committed sink
each enlisted repository registered, in turn dispatching that repository's committed advisors. When a
committed sink throws, the unit of work collects the errors and rethrows them (single error directly,
several as an `AggregateException`) after running the remaining sinks.

Query-cache eviction uses this pipeline: updated and removed entities evict reverse-indexed cache
entries after commit; added entities do not.

## Reopening after commit

A repository that opened its own unit of work — the implicit one a standalone write enlists — reopens
as soon as that unit of work commits. The same injected `IRepository<T>` therefore serves several
write-then-commit cycles in one scope, and the next write enlists a fresh unit of work:

```csharp
await repository.AddAsync(first);
await repository.CommitAsync();

await repository.AddAsync(second);   // stages into a new unit of work
await repository.CommitAsync();
```

Reads issued between a commit and the next write still resolve against the committed context.

A unit of work supplied through `Begin()` or `Join(uow)` belongs to the caller and never reopens: once
it completes, further work on that repository throws. Call `Begin()` again, or resolve a fresh
`IRepository<T>`, to start the next one — `Begin()` always creates a new unit of work rather than
handing out the repository's implicit one.

## Rollback and disposal

`RollbackAsync` rolls back the transaction and runs each repository's rollback sink, which clears its
tracking lists. Disposing a unit of work that never committed rolls back the same way. Both are safe to
call after the unit of work has already completed. A repository enlisted in an externally supplied unit
of work does not dispose that unit of work — the caller owns its lifetime.

## Registration

```csharp
services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlite(connectionString))
        .WithUnitOfWork<AppDbContext>();
```

`AddRepository<TEntity, TImplementation>()` is the only registration overload: call it once per
entity type. `.WithUnitOfWork<TContext>()` registers `IUnitOfWork<TContext>` as scoped with `TryAddScoped`, so a
prior registration wins. Both providers expose the same method.

## See also

- [overview.md](overview.md) — repository API and suppression scopes
- [mutation-pipeline.md](mutation-pipeline.md) — mutation advisors and the committed pipeline
- [providers.md](providers.md) — EF Core and LinqToDB unit-of-work implementations
