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

    public Guid      Uid           { get; set; }
    public string?   Name          { get; set; }
    public string?   CanonicalName { get; set; }
    public DateTime? CreateTime    { get; set; }
    public DateTime? UpdateTime    { get; set; }
    public DateTime? DeleteTime    { get; set; }
    public DateTime? PurgeTime     { get; set; }
}
```

## Enable unit-of-work support

`IUnitOfWork` requires the repository to be configured with `.WithUnitOfWork<TContext>()`. Update the repository registration in `Program.cs`:

```csharp
schema.ConfigureServices(services => {
    services.AddRepository(typeof(EfCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"))
        .WithUnitOfWork<AppDbContext>();

    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
});
```

Repositories are registered as transient. Each resolved `IRepository<Student>` owns a separate data context until it enlists in a unit of work.

## Use a transaction in a service

Inject the repository and the typed unit of work. Enlist every repository that should participate via `Join`, then commit through the unit of work:

```csharp
using Schemata.Entity.Repository;

public sealed class EnrollmentService(
    IRepository<Student>      students,
    IUnitOfWork<AppDbContext> uow)
{
    public async Task EnrollBatchAsync(string[] names, CancellationToken ct = default)
    {
        students.Join(uow);

        foreach (var name in names)
        {
            var student = new Student { FullName = name, Age = 18 };
            await students.AddAsync(student, ct);
        }

        await uow.CommitAsync(ct);
    }
}
```

`IUnitOfWork<AppDbContext>` is resolved from DI (scoped). `Join` enlists the repository; the first enlisted write opens the transaction. After `Join`, calling `students.CommitAsync()` throws `InvalidOperationException` — use `uow.CommitAsync()` for the transaction boundary.

## Rollback on failure

If `CommitAsync` is never called and the unit of work is disposed, the transaction rolls back. You can also roll back explicitly:

```csharp
students.Join(uow);

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

## Multiple repositories

Repositories do not share a context by default. Enlist each repository that should join the same transaction:

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

`IUnitOfWork<TContext>` extends `IUnitOfWork` and targets one concrete context type, such as an EF Core `DbContext` or a LinqToDB `DataConnection`.

## Committed advisors

After a successful standalone repository commit or unit-of-work commit, Schemata invokes registered `IRepositoryCommittedAdvisor<TEntity>` implementations. Each advisor receives a `CommitChanges<TEntity>` snapshot containing added, updated, and removed entities.

Query cache eviction uses this committed pipeline. Updated and removed entities evict reverse-indexed cache entries after the database commit succeeds; added entities are ignored.

## Next steps

- [Object Mapping](object-mapping.md) — split request and response DTOs
- [Query Caching](query-caching.md) — committed advisors evict cached queries here
- [Concurrency and Freshness](concurrency-and-freshness.md) — optimistic concurrency and ETags

## See also

- [Unit of Work](../documents/repository/unit-of-work.md) — explicit enlistment and committed advisors
- [Mutation Pipeline](../documents/repository/mutation-pipeline.md) — advisor execution order around commits
