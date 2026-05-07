# Getting Started

This guide walks through building a student CRUD API with auto-generated HTTP endpoints. By the end you will have a running application that supports creating, listing, reading, updating, and soft-deleting students.

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
using Schemata.Abstractions.Entities;

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

| Trait            | Behavior                                                                               |
| ---------------- | -------------------------------------------------------------------------------------- |
| `IIdentifier`    | Supplies a `Guid` primary key via the `Uid` property                                   |
| `ICanonicalName` | Provides `Name` (short identifier) and `CanonicalName` (fully-qualified resource name) |
| `ITimestamp`     | Sets `CreateTime` on add and `UpdateTime` on update automatically                      |
| `ISoftDelete`    | Sets `DeleteTime` on delete instead of removing the row; queries exclude soft-deleted  |

The `[CanonicalName("students/{student}")]` attribute defines the resource name pattern. `students` is the collection segment (the HTTP route prefix) and `{student}` is a variable segment resolved from the entity's `Name` property.

For a complete reference of all available traits, see [Traits](../documents/entity/traits.md).

## Create the DbContext

Create `AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.UseTableKeyConventions();
    }
}
```

`UseTableKeyConventions()` registers the Schemata `TableKeyConvention` so EF Core respects `[TableKey]` attributes when determining primary keys. Without it, entities that rely on `[TableKey]` for composite or custom key ordering will not be configured correctly.

## Create the Name advisor

The resource pipeline clears `Name` on create requests to prevent client injection. Since `[CanonicalName]` requires `Name` to be non-null when the canonical name advisor runs, create an advisor that populates `Uid` and `Name` before that happens:

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
{
    // Run early
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

`Guid.CreateVersion7()` produces a time-ordered UUID that works well as a primary key for most databases. The advisor runs early (Order `50_000_000`, ahead of the built-in timestamp advisor at `100_000_000`) so that `Uid` and `Name` are set before any downstream advisor reads them.

The `Order` property controls where this advisor runs relative to other advisors in the pipeline. Lower values execute first. See [Mutation Pipeline](../documents/repository/mutation-pipeline.md) for the full advisor execution order.

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
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
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

| Method   | Path                     | Description           |
| -------- | ------------------------ | --------------------- |
| `GET`    | `/students`              | List all students     |
| `POST`   | `/students`              | Create a student      |
| `GET`    | `/{name=students/*}`     | Get a student by name |
| `PATCH`  | `/{name=students/*}`     | Update a student      |
| `DELETE` | `/{name=students/*}`     | Soft-delete a student |

```shell
# Create a student
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
# Response includes "name" (e.g. "students/a1b2c3d4e5f6a7b8")

# List all students
curl http://localhost:5000/students

# Get by name (copy the "name" value directly from the create response)
curl http://localhost:5000/<name-from-response>

# Update
curl -X PATCH http://localhost:5000/<name-from-response> \
     -H "Content-Type: application/json" \
     -d '{"age":21}'

# Soft-delete
curl -X DELETE http://localhost:5000/<name-from-response>

# The deleted student no longer appears in the list
curl http://localhost:5000/students
```

The `name` field in responses is the full qualified resource name (e.g. `"students/a1b2c3d4e5f6a7b8"`). The route pattern `{name=students/*}` captures the full name as a single route parameter, constrained to the `students/` prefix. Use the `name` value from the response directly in the URL — the framework resolves the entity from it.

Request and response bodies use `snake_case` property names (`full_name`, `create_time`, etc.). This is configured automatically by Schemata's JSON serialization feature — see [JSON Serialization](../documents/core/json-serialization.md) for details.

The HTTP transport serializes properties to follow AIP conventions: `Name` contains the full qualified resource name (`"students/a1b2c3d4"`), `EntityTag` serializes as `etag`, and list responses use the pluralized resource name (`"students"`) for the collection field. The gRPC transport applies equivalent but [different mappings](../documents/resource/grpc-transport.md#comparison-with-http).

## Next steps

- [Object Mapping](object-mapping.md) -- introduce separate request/response DTOs
- [Concurrency and Freshness](concurrency-and-freshness.md) -- add optimistic concurrency and ETags
- [Filtering and Pagination](filtering-and-pagination.md) -- query the student list with filters and pagination
