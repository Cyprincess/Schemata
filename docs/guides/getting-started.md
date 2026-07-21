# Getting Started

This guide builds a Student CRUD API with auto-generated HTTP endpoints. By the end you have a
running application that creates, lists, reads, updates, and soft-deletes students.

## Prerequisites

- .NET 8 SDK or later
- Familiarity with ASP.NET Core

## Create the project

```shell
dotnet new web -n StudentApp
cd StudentApp
dotnet add package --prerelease Schemata.Application.Complex.Targets
dotnet add package --prerelease Schemata.Entity.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

`Schemata.Application.Complex.Targets` bundles the core framework, the repository abstraction,
Mapster, resource services, identity, and validation. The persistence provider is a separate
choice: `Schemata.Entity.EntityFrameworkCore` adapts the repository to EF Core, and the SQLite
package supplies the database driver used in this guide.

## Define the entity

Create `Student.cs`. Implement the trait interfaces for the capabilities you want:

```csharp
using Schemata.Abstractions.Entities;

[PrimaryKey(nameof(Uid))]
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    // IIdentifier
    public Guid Uid { get; set; }

    // ICanonicalName
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    // ITimestamp
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    // ISoftDelete
    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }
}
```

Each trait enables behavior through built-in repository advisors:

| Trait            | Behavior                                                                                   |
| ---------------- | ------------------------------------------------------------------------------------------ |
| `IIdentifier`    | Supplies a `Guid` primary key via the `Uid` property                                       |
| `ICanonicalName` | Provides `Name` (short identifier) and `CanonicalName` (fully-qualified resource name)     |
| `ITimestamp`     | Sets `CreateTime` on add and `UpdateTime` on update                                        |
| `ISoftDelete`    | Sets `DeleteTime` on delete instead of removing the row; queries exclude soft-deleted rows |

`[CanonicalName("students/{student}")]` defines the resource-name pattern: `students` is the
collection segment (the HTTP route prefix) and `{student}` is a variable resolved from the entity's
`Name` property.

## Create the DbContext

Create `AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
}
```

The class-level `[PrimaryKey(nameof(Uid))]` declares `Uid` as the primary key.

## Create the name advisor

The resource pipeline clears `Name` on create requests so a client cannot inject it. Because
`[CanonicalName]` needs a non-null `Name` when the canonical-name advisor runs, add an advisor that
populates `Uid` and `Name` first:

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class AdviceAddStudentName : IRepositoryAddAdvisor<Student>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<Student> repository,
        Student              entity,
        CancellationToken    ct = default)
    {
        if (entity.Uid == Guid.Empty)
            entity.Uid = Guid.CreateVersion7();

        if (string.IsNullOrWhiteSpace(entity.Name))
            entity.Name = entity.Uid.ToString("N");

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

`Guid.CreateVersion7()` produces a time-ordered UUID that sorts well as a primary key. `Order = 0`
runs this advisor ahead of every built-in repository advisor, the lowest of which is at
100,000,000.

## Configure the application

Replace `Program.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        schema.ConfigureServices(services => {
            services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student>();
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.EnsureCreatedAsync();
}

app.Run();
```

The key pieces:

- `UseSchemata` registers Schemata services and middleware on the `WebApplicationBuilder`.
- `UseJsonSerializer()` configures `System.Text.Json` with snake_case names and long-as-string
  output.
- `AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()` registers the closed-generic
  EF Core repository for `Student` and adds the built-in advisors (timestamp, concurrency,
  canonical name, validation, uniqueness, soft-delete). Each entity type takes its own
  registration call.
- `UseEntityFrameworkCore<AppDbContext>(...)` registers an `IDbContextFactory<AppDbContext>` for the
  chosen provider.
- `UseResource().MapHttp().Use<Student>()` exposes the entity as HTTP endpoints.

`Use<Student>()` is shorthand for `Use<Student, Student, Student, Student>()` — the entity type
fills all four type parameters (entity, request, detail, summary).

## Verify

```shell
dotnet run
```

These endpoints are now available:

| Method   | Path                     | AIP     | Description           |
| -------- | ------------------------ | ------- | --------------------- |
| `GET`    | `/v1/students`           | AIP-132 | List students         |
| `POST`   | `/v1/students`           | AIP-133 | Create a student      |
| `GET`    | `/v1/students/{student}` | AIP-131 | Get a student by name |
| `PATCH`  | `/v1/students/{student}` | AIP-134 | Update a student      |
| `DELETE` | `/v1/students/{student}` | AIP-135 | Soft-delete a student |

```shell
# Create a student
curl -X POST http://localhost:5000/v1/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
# Response includes "name" (e.g. "students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6")

# List students
curl http://localhost:5000/v1/students

# Get by name (append the "name" value from the create response to /v1/)
curl http://localhost:5000/v1/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6

# Update
curl -X PATCH http://localhost:5000/v1/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6 \
     -H "Content-Type: application/json" \
     -d '{"age":21}'

# Soft-delete
curl -X DELETE http://localhost:5000/v1/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6

# The deleted student no longer appears in the list
curl http://localhost:5000/v1/students
```

The `name` field is the full resource name (`"students/a1b2c3d4..."`), so a resource URL is
`/v1/` followed by the `name` value. Request and response bodies use `snake_case` property names
(`full_name`, `create_time`).

## Next steps

- [Unit of Work](unit-of-work.md) — wrap batch mutations in a transaction
- [Object Mapping](object-mapping.md) — split request and response DTOs
- [Concurrency and Freshness](concurrency-and-freshness.md) — optimistic concurrency and ETags
- [Filtering and Pagination](filtering-and-pagination.md) — filter and page the student list

## See also

- [Traits](../documents/entity/traits.md) — the complete trait interface reference
- [Mutation Pipeline](../documents/repository/mutation-pipeline.md) — advisor execution order
- [Resource Overview](../documents/resource/overview.md) — the four type parameters and handler stages
