# Unit of Work

This guide adds explicit transaction control to the Student CRUD app. Building on [Getting Started](getting-started.md), you will learn how to batch multiple repository mutations in a single database transaction using `IUnitOfWork<TContext>`.

## How it works

By default, each call to `repository.CommitAsync()` corresponds to one database transaction. When you need multiple `AddAsync` / `UpdateAsync` / `RemoveAsync` calls to succeed or fail together, `IUnitOfWork<TContext>` provides explicit begin/commit/rollback control.

`IUnitOfWork<TContext>` is enabled by chaining `.WithUnitOfWork<TContext>()` on the repository builder. Once registered, all repositories sharing the same `DbContext` can participate in the same transaction.

## Add WithUnitOfWork

In `Program.cs`, chain `.WithUnitOfWork<AppDbContext>()` after `UseEntityFrameworkCore`:

```csharp
schema.ConfigureServices(services => {
    services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"))
        .WithUnitOfWork<AppDbContext>();

    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentIdAdvisor>());
});
```

This registers `IUnitOfWork<AppDbContext>` as a scoped service backed by `EfCoreUnitOfWork<AppDbContext>`.

## Create an enrollment service

Create `Services/EnrollmentService.cs` to demonstrate transactional batching. For this guide we'll simulate adding multiple students in one transaction:

```csharp
using Schemata.Entity.Repository;

public class EnrollmentService(IRepository<Student> students)
{
    public async Task EnrollAsync(string[] names, CancellationToken ct = default)
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

`BeginWork()` starts a new database transaction. `CommitAsync()` flushes all pending changes and commits. If any mutation fails (e.g., validation exception), the `using` block calls `Dispose()` which automatically rolls back.

Register the service in `Program.cs`:

```csharp
schema.ConfigureServices(services => {
    // ... existing registrations ...
    services.AddScoped<EnrollmentService>();
});
```

## How repositories interact with Unit of Work

When `IUnitOfWork.IsActive` is `true`, calling `CommitAsync` on a repository **throws** `InvalidOperationException` — the unit of work owns the transaction. Commit via `uow.CommitAsync()`:

```csharp
using var uow = students.BeginWork();
await students.AddAsync(student1);
await courses.AddAsync(course);
await uow.CommitAsync();  // commits both — never call repo.CommitAsync() inside UoW
```

Repositories sharing the same scoped `DbContext` automatically participate in the same transaction, even without explicitly passing the UoW.

## Verify

```shell
dotnet run
```

```shell
# Enroll two students atomically (via a custom endpoint or test)
# If the second student fails validation, neither is persisted
```

`BeginWork()` requires `.WithUnitOfWork()` to be configured — calling it without registration throws `InvalidOperationException`, making transactional intent explicit.

## When to use Unit of Work

| Scenario                                    | Approach                        |
| ------------------------------------------- | ------------------------------- |
| Single mutation per request                 | `AddAsync` + `CommitAsync`      |
| Multiple mutations, independent             | Each with its own `CommitAsync` |
| Multiple mutations, must succeed together   | `BeginWork()` + `CommitAsync()` |

For most CRUD endpoints, individual `CommitAsync` is sufficient. Use `BeginWork()` for multi-step operations where partial failure would leave inconsistent state.

## Next steps

- [Object Mapping](object-mapping.md) — introduce separate request/response DTOs
- [Unit of Work](../documents/repository/unit-of-work.md) — technical reference

## Further reading

- [Repository Overview](../documents/repository/overview.md)
- [Mutation Pipeline](../documents/repository/mutation-pipeline.md)
