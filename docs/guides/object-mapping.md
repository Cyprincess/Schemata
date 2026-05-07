# Object Mapping

This guide builds on [Getting Started](getting-started.md). You will introduce separate request and response types so the API exposes only the fields clients need, while the persistent `Student` entity remains unchanged.

## Create the request DTO

`StudentRequest` is used for both `POST` (create) and `PATCH` (update) bodies. It implements `ICanonicalName` because the resource pipeline requires it on all four type parameters. `IFreshness` enables ETag-based conditional updates. `IUpdateMask` enables partial updates. `IValidation` enables dry-run validation (covered in a later guide).

Create `StudentRequest.cs`:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

public record StudentRequest : ICanonicalName, IFreshness, IUpdateMask, IValidation
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    // ICanonicalName
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    // IFreshness
    public string? EntityTag { get; set; }

    // IUpdateMask
    public string? UpdateMask { get; set; }

    // IValidation
    public bool ValidateOnly { get; set; }
}
```

## Create the detail DTO

`StudentDetail` is returned by `GET`, `POST`, and `PATCH` endpoints. It exposes timestamps and the ETag so clients can use them in subsequent conditional requests.

Create `StudentDetail.cs`:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

public record StudentDetail : ICanonicalName, IFreshness, ITimestamp
{
    public string? FullName  { get; set; }
    public int     Age       { get; set; }

    // ICanonicalName
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    // IFreshness
    public string? EntityTag { get; set; }

    // ITimestamp
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
}
```

## Create the summary DTO

`StudentSummary` is used in list responses. It contains only the fields needed for a collection view.

Create `StudentSummary.cs`:

```csharp
using Schemata.Abstractions.Entities;

public record StudentSummary : ICanonicalName
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    // ICanonicalName
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
```

## Configure Mapster mappings

`UseMapster()` on `SchemataBuilder` returns a `SchemataMappingBuilder`. Call `Map<TSource, TDestination>()` to register each mapping pair. When source and destination properties share the same name and type, Mapster maps them automatically.

In `Program.cs`, replace `schema.UseMapster()`:

```csharp
schema.UseMapster()
      .Map<Student, StudentDetail>()
      .Map<Student, StudentSummary>()
      .Map<StudentRequest, Student>();
```

`Map<S, D>()` accepts an optional `Action<Map<S, D>>` for custom field mappings, but the default convention-based mapping works here.

## Update the resource registration

In `Program.cs`, change the `Use<>()` call to reference the new types:

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

The four type parameters are:

| Parameter  | Type             | Purpose                                  |
| ---------- | ---------------- | ---------------------------------------- |
| `TEntity`  | `Student`        | Persistent entity stored in the database |
| `TRequest` | `StudentRequest` | Input DTO for `POST` and `PATCH`         |
| `TDetail`  | `StudentDetail`  | Output DTO for single-entity responses   |
| `TSummary` | `StudentSummary` | Output DTO for list responses            |

## Full Program.cs

After all changes, `Program.cs` looks like this:

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

        schema.UseMapster()
              .Map<Student, StudentDetail>()
              .Map<Student, StudentSummary>()
              .Map<StudentRequest, Student>();

        schema.ConfigureServices(services => {
            services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider
               .GetRequiredService<AppDbContext>()
               .Database.EnsureCreatedAsync();

app.Run();
```

## Verify

```shell
dotnet run
```

Create a student and observe the detail response shape:

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

The response is now a `StudentDetail`:

```json
{
  "full_name": "Alice",
  "age": 20,
  "name": "students/a1b2c3d4e5f6a7b8",
  "etag": null,
  "create_time": "2026-03-26T12:00:00Z",
  "update_time": null
}
```

List students and observe the summary response shape:

```shell
curl http://localhost:5000/students
```

Each item in the `students` array is now a `StudentSummary`:

```json
{
  "students": [
    {
      "full_name": "Alice",
      "age": 20,
      "name": "students/a1b2c3d4e5f6a7b8"
    }
  ],
  "total_size": 1,
  "next_page_token": null
}
```

The `etag` is `null` because `Student` does not implement `IConcurrency` yet. The next guide adds that.

## Next steps

- [Concurrency and Freshness](concurrency-and-freshness.md) -- add optimistic concurrency and ETags
- [Filtering and Pagination](filtering-and-pagination.md) -- query the student list with filters and pagination
- [Validation](validation.md) -- add input validation with FluentValidation

## Further reading

- [Mapping](../documents/mapping.md)
