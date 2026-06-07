# Unit of Work

Wrap multiple repository operations in a single database transaction using `IUnitOfWork`. This guide extends the `Student` application from [Getting Started](getting-started.md) to show how to commit or roll back a batch of mutations atomically.

## What you have

The `Student` entity from [Getting Started](getting-started.md):

```csharp
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    public Guid      Uid          { get; set; }
    public string?   Name         { get; set; }
    public string?   CanonicalName { get; set; }
    public DateTime? CreateTime   { get; set; }
    public DateTime? UpdateTime   { get; set; }
    public DateTime? DeleteTime   { get; set; }
    public DateTime? PurgeTime    { get; set; }
}
```

## Enable unit-of-work support

`IUnitOfWork` requires the repository to be configured with `.WithUnitOfWork<TContext>()`. Update the repository registration in `Program.cs`:

```csharp
schema.ConfigureServices(services => {
    services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"))
        .WithUnitOfWork<AppDbContext>();

    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
});
```

Without `.WithUnitOfWork<TContext>()`, calling `repository.BeginWork()` throws `InvalidOperationException`.

## Use a transaction in a service

Inject `IRepository<Student>` and call `BeginWork()` to open a transaction. The returned `IUnitOfWork` is `IDisposable` — use it in a `using` block so the transaction rolls back automatically if you don't commit:

```csharp
using Schemata.Entity.Repository;

public sealed class EnrollmentService(IRepository<Student> students)
{
    public async Task EnrollBatchAsync(string[] names, CancellationToken ct = default)
    {
        using var uow = students.BeginWork();

        foreach (var name in names)
        {
            var student = new Student { FullName = name, Age = 18 };
            await students.AddAsync(student, ct);
        }

        await uow.CommitAsync(ct);
    }
}
```

`BeginWork()` calls `IUnitOfWork.Begin()` on the underlying unit of work and returns it. All repositories resolved from the same DI scope share the same `IUnitOfWork<TContext>` instance, so they participate in the same transaction automatically.

## Rollback on failure

If `CommitAsync` is never called and the `using` block exits (normally or via exception), `Dispose()` rolls back the transaction. You can also roll back explicitly:

```csharp
using var uow = students.BeginWork();
try
{
    await students.AddAsync(student, ct);
    await uow.CommitAsync(ct);
}
catch
{
    await uow.RollbackAsync(ct);
    throw;
}
```

`RollbackAsync` and `Dispose` are both safe to call after a rollback has already occurred.

## After-commit actions

Advisors and services can enqueue work that must only run after a successful commit — cache eviction, outbox writes, notifications. Use `EnqueueAfterCommit` on either the repository or the unit of work:

```csharp
students.EnqueueAfterCommit(async ct => {
    // runs only after CommitAsync succeeds
    await cache.RemoveAsync("students:list", ct);
});

await uow.CommitAsync(ct);
// cache.RemoveAsync runs here, after the transaction commits
```

When a `IUnitOfWork` is active, `repository.EnqueueAfterCommit(action)` routes the action to the unit of work's queue. The queue drains once at `uow.CommitAsync`. On rollback or `Dispose`, the queue is discarded — the action never runs.

If no unit of work is active, the repository drains its own queue immediately after `CommitAsync`.

## Typed unit of work

When multiple database providers coexist in the same DI container, use `IUnitOfWork<TContext>` to target a specific context:

```csharp
public sealed class EnrollmentService(
    IRepository<Student>     students,
    IUnitOfWork<AppDbContext> uow)
{
    public async Task EnrollAsync(Student student, CancellationToken ct)
    {
        uow.Begin();
        await students.AddAsync(student, ct);
        await uow.CommitAsync(ct);
    }
}
```

`IUnitOfWork<TContext>` extends `IUnitOfWork` — all the same methods apply.

## See also

- [Getting Started](getting-started.md) — the `Student` entity and startup configuration
- [Object Mapping](object-mapping.md) — next in the series: separate request/response DTOs
- [Query Caching](query-caching.md) — after-commit cache eviction in practice
- [Unit of Work](../documents/repository/unit-of-work.md) — `BeginWork` semantics and `EnqueueAfterCommit` design
- [Mutation Pipeline](../documents/repository/mutation-pipeline.md) — advisor execution order around commits
