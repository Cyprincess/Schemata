# Getting Started

This guide walks through building a Student CRUD API with auto-generated HTTP endpoints. By the end you'll have a running application that supports creating, listing, reading, updating, and soft-deleting students.

## Prerequisites

- .NET 8 SDK or later
- Familiarity with ASP.NET Core

## Create the project

```shell
dotnet new web -n StudentApp
cd StudentApp
dotnet add package --prerelease Schemata.Application.Complex.Targets
```

`Schemata.Application.Complex.Targets` bundles the core framework, Entity Framework Core, Mapster, resource services, and commonly used packages. See [Packages](../documents/packages.md) for the full package matrix.

## Define the entity

Create `Student.cs`. Implement the trait interfaces that match the capabilities you need:

```csharp
using Microsoft.EntityFrameworkCore;
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

Each trait enables behavior through built-in advisors:

| Trait | Behavior |
| --- | --- |
| `IIdentifier` | Supplies a `Guid` primary key via the `Uid` property |
| `ICanonicalName` | Provides `Name` (short identifier) and `CanonicalName` (fully-qualified resource name) |
| `ITimestamp` | Sets `CreateTime` on add and `UpdateTime` on update automatically |
| `ISoftDelete` | Sets `DeleteTime` on delete instead of removing the row; queries exclude soft-deleted rows |

The `[CanonicalName("students/{student}")]` attribute defines the resource name pattern. `students` is the collection segment (the HTTP route prefix) and `{student}` is a variable segment resolved from the entity's `Name` property.

For a complete reference of all available traits, see [Traits](../documents/entity/traits.md).

## Create the DbContext

Create `AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
}
```

The class-level `[PrimaryKey(nameof(Uid))]` attribute declares `Uid` as the primary key.

## Create the Name advisor

The resource pipeline clears `Name` on create requests to prevent client injection. Since `[CanonicalName]` requires `Name` to be non-null when the canonical name advisor runs, create an advisor that populates `Uid` and `Name` before that happens:

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<Student> repository,
        Student              entity,
        CancellationToken    ct)
    {
        if (entity.Uid == Guid.Empty)
            entity.Uid = Guid.CreateVersion7();

        if (string.IsNullOrWhiteSpace(entity.Name))
            entity.Name = entity.Uid.ToString("N");

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

`Guid.CreateVersion7()` produces a time-ordered UUID that works well as a primary key. `Order = 0` puts this advisor at the front of the pipeline.

The `Order` property controls where each advisor runs in the pipeline; lower values execute first. See [Mutation Pipeline](../documents/repository/mutation-pipeline.md) for the full advisor execution order.

## Configure the application

Replace the contents of `Program.cs`:

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
            services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student>();
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider
               .GetRequiredService<AppDbContext>()
               .Database.EnsureCreatedAsync();

app.Run();
```

The key pieces:

- `UseSchemata` registers all Schemata services and middleware on the `WebApplicationBuilder`
- `UseJsonSerializer()` configures System.Text.Json with snake_case naming and 53-bit integer handling
- `AddRepository(typeof(EntityFrameworkCoreRepository<,>))` registers the EF Core repository as an open generic and all built-in advisors (timestamp, concurrency, soft-delete, canonical name, validation)
- `UseEntityFrameworkCore<AppDbContext>(...)` registers the DbContext with the specified provider
- `UseResource().MapHttp().Use<Student>()` exposes the entity as HTTP endpoints

`Use<Student>()` is shorthand for `Use<Student, Student, Student, Student>()` — the entity type is used for all four type parameters (entity, request, detail, summary). The [Object Mapping](object-mapping.md) guide shows how to introduce separate request and response types.

## Verify

```shell
dotnet run
```

The following endpoints are now available:

| Method | Path | AIP | Description |
| --- | --- | --- | --- |
| `GET` | `/students` | AIP-132 | List all students |
| `POST` | `/students` | AIP-133 | Create a student |
| `GET` | `/{name=students/*}` | AIP-131 | Get a student by name |
| `PATCH` | `/{name=students/*}` | AIP-134 | Update a student |
| `DELETE` | `/{name=students/*}` | AIP-135 | Soft-delete a student |

```shell
# Create a student
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
# Response includes "name" (e.g. "students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6")

# List all students
curl http://localhost:5000/students

# Get by name (copy the "name" value from the create response)
curl http://localhost:5000/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6

# Update
curl -X PATCH http://localhost:5000/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6 \
     -H "Content-Type: application/json" \
     -d '{"age":21}'

# Soft-delete
curl -X DELETE http://localhost:5000/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6

# The deleted student no longer appears in the list
curl http://localhost:5000/students
```

The `name` field in responses is the full qualified resource name (e.g. `"students/a1b2c3d4..."`). The route pattern `{name=students/*}` captures the full name as a single route parameter, constrained to the `students/` prefix.

Request and response bodies use `snake_case` property names (`full_name`, `create_time`, etc.). This is configured automatically by `UseJsonSerializer()` — see [JSON Serialization](../documents/core/json-serialization.md) for details.

## Next steps

- [Unit of Work](unit-of-work.md) — wrap batch mutations in a transaction
- [Object Mapping](object-mapping.md) — introduce separate request/response DTOs
- [Concurrency and Freshness](concurrency-and-freshness.md) — add optimistic concurrency and ETags
- [Filtering and Pagination](filtering-and-pagination.md) — query the student list with filters and pagination

## See also

- [Traits](../documents/entity/traits.md) — complete trait interface reference
- [Mutation Pipeline](../documents/repository/mutation-pipeline.md) — advisor execution order
- [Resource Overview](../documents/resource/overview.md) — four type parameters and handler stages
- [JSON Serialization](../documents/core/json-serialization.md) — snake_case and wire format details
- [Packages](../documents/packages.md) — full meta-package matrix
