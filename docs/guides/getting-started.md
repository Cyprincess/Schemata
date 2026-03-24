# Getting Started

This guide walks through building a minimal student-management API with full CRUD and auto-generated HTTP endpoints. By the end you will have a running application that supports creating, listing, reading, updating, and soft-deleting students.

## Prerequisites

- .NET 8 SDK or later
- Basic familiarity with ASP.NET Core

## Create the project

```shell
dotnet new web -n StudentApp
cd StudentApp
dotnet add package --prerelease Schemata.Application.Complex.Targets
```

`Schemata.Application.Complex.Targets` is a meta-package that pulls in the core framework, Entity Framework Core, Mapster, resource services, and other commonly used packages. See [Packages](../documents/packages.md) for the full package matrix.

## Define the entity

Create `Student.cs`. Implement the trait interfaces that match the capabilities you need:

```csharp
using Schemata.Abstractions.Entities;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    // IIdentifier
    public long Id { get; set; }

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

Each trait enables automatic behavior through built-in advisors:

| Trait            | Behavior                                                                                       |
| ---------------- | ---------------------------------------------------------------------------------------------- |
| `IIdentifier`    | Marks the entity as having a `long` surrogate primary key                                      |
| `ICanonicalName` | Provides `Name` (short identifier) and `CanonicalName` (fully-qualified resource name)         |
| `ITimestamp`     | Automatically sets `CreateTime` on add and `UpdateTime` on update                              |
| `ISoftDelete`    | Sets `DeleteTime` instead of physically deleting the row; queries filter out soft-deleted rows |

The `[CanonicalName("students/{student}")]` attribute defines the resource name pattern. The collection segment (`students`) determines the HTTP route prefix, and the variable segment (`{student}`) is resolved from the entity's `Name` property.

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

## Create the ID advisor

The resource pipeline clears `Name` on create requests to prevent client injection. Since `[CanonicalName]` requires `Name` to be non-null when the canonical name advisor runs, create an advisor that generates `Id` and `Name` before that happens:

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class StudentIdAdvisor : IRepositoryAddAdvisor<Student>
{
    // Runs before AdviceAddCanonicalName (Order 120_000_000)
    public int Order => 115_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<Student> repository,
        Student              entity,
        CancellationToken    ct)
    {
        if (entity.Id <= 0)
            entity.Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (string.IsNullOrWhiteSpace(entity.Name))
            entity.Name = entity.Id.ToString();

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

The `Order` property controls where this advisor runs relative to the built-in advisors. The mutation pipeline runs advisors in ascending order. See [Mutation Pipeline](../documents/repository/mutation-pipeline.md) for the full advisor execution order.

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
        schema.UseMapster();

        schema.ConfigureServices(services => {
            services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentIdAdvisor>());
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student, Student, Student, Student>();
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
- `UseMapster()` enables the object mapper (required by the resource pipeline for DTO conversions)
- `AddRepository(typeof(EntityFrameworkCoreRepository<,>))` registers the EF Core repository as an open generic and all built-in advisors (timestamp, concurrency, soft-delete, canonical name, validation)
- `UseEntityFrameworkCore<AppDbContext>(...)` registers the DbContext with the specified provider
- `UseResource().MapHttp().Use<...>()` exposes the entity as HTTP endpoints

The four type parameters on `Use<TEntity, TRequest, TDetail, TSummary>` are the entity type, the request DTO type, the detail response type, and the list summary type. Using the same type for all four means the entity is used directly for every operation. The next guide ([Object Mapping](object-mapping.md)) shows how to introduce separate request and response types.

## Verify

```shell
dotnet run
```

The following endpoints are now available:

| Method   | Path               | Description           |
| -------- | ------------------ | --------------------- |
| `GET`    | `/students`        | List all students     |
| `POST`   | `/students`        | Create a student      |
| `GET`    | `/students/{name}` | Get a student by name |
| `PATCH`  | `/students/{name}` | Update a student      |
| `DELETE` | `/students/{name}` | Soft-delete a student |

```shell
# Create a student
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'

# List all students
curl http://localhost:5000/students

# Get by name (use the name from the create response)
curl http://localhost:5000/students/1

# Update
curl -X PATCH http://localhost:5000/students/1 \
     -H "Content-Type: application/json" \
     -d '{"age":21}'

# Soft-delete
curl -X DELETE http://localhost:5000/students/1

# Verify: list no longer includes the deleted student
curl http://localhost:5000/students
```

Note that request and response bodies use `snake_case` property names (`full_name`, `create_time`, etc.). This is configured automatically by Schemata's JSON serialization feature -- see [JSON Serialization](../documents/core/json-serialization.md) for details.

Beyond snake_case, the HTTP transport also renames a few resource properties to follow AIP conventions: `CanonicalName` is removed from responses (clients see only the short `name`, since the full path is already in the URL), `EntityTag` is serialized as `etag` rather than the default `entity_tag`, and the list collection uses the pluralized resource name (e.g. `students`) instead of the C# property name `Entities`. The gRPC transport applies equivalent but [different mappings](../documents/resource/grpc-transport.md#comparison-with-http).

## Next steps

- [Object Mapping](object-mapping.md) -- introduce separate request/response DTOs
- [Concurrency and Freshness](concurrency-and-freshness.md) -- add optimistic concurrency and ETags
- [Filtering and Pagination](filtering-and-pagination.md) -- query the student list with filters and pagination
