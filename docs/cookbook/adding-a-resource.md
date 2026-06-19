# Adding a Resource

## What you'll build

A `Course` resource exposed over both HTTP REST and gRPC with separate request, detail, and summary DTOs. You'll
see how the four type parameters collapse rightward, how `[Resource]` drives discovery, and how `[HttpResource]`
and `[GrpcResource]` select transports.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Resource.Foundation`, `Schemata.Resource.Http`, `Schemata.Resource.Grpc`.

## Step 1: Define the entity and DTOs

All four types are `class` and implement `ICanonicalName`. Missing slots collapse rightward:
`[Resource<TEntity>]` equals `[Resource<TEntity, TEntity, TEntity, TEntity>]`.

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete
{
    public string?         Name          { get; set; }
    public string?         CanonicalName { get; set; }
    public Guid            Uid           { get; set; }
    public DateTime?       CreateTime    { get; set; }
    public DateTime?       UpdateTime    { get; set; }
    public DateTime?       DeleteTime    { get; set; }
    public DateTime?       PurgeTime     { get; set; }
    public string?         Title         { get; set; }
    public int             Credits       { get; set; }
}

public class CourseRequest : ICanonicalName        // create and update
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? Title         { get; set; }
    public int     Credits       { get; set; }
}

public class CourseDetail : ICanonicalName          // single-resource reads
{
    public string?   Name          { get; set; }
    public string?   CanonicalName { get; set; }
    public string?   Title         { get; set; }
    public int       Credits       { get; set; }
    public DateTime? CreateTime    { get; set; }
    public DateTime? UpdateTime    { get; set; }
}

public class CourseSummary : ICanonicalName         // list items
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? Title         { get; set; }
}
```

**Assertion:** all four compile. `ResourceAttribute<Course>` equals
`ResourceAttribute<Course, Course, Course, Course>`; use the four-parameter form when the DTOs differ.

## Step 2: Attach the resource attribute

`[Resource<...>]` on the entity drives both imperative registration and assembly-scan discovery.

```csharp
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete { /* ... */ }
```

**Assertion:** `typeof(Course).GetCustomAttribute<ResourceAttribute>()` is non-null with `Entity == typeof(Course)`,
`Request == typeof(CourseRequest)`, `Detail == typeof(CourseDetail)`, and `Summary == typeof(CourseSummary)`.

## Step 3: Populate the name on create

The create pipeline sanitizes server-managed fields, clearing `Name` and `CanonicalName` from the request so a
client cannot inject them. Because `[CanonicalName]` needs a non-null `Name`, add a repository add advisor that
assigns one (the same pattern Getting Started uses for `Student`):

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class CourseNameAdvisor : IRepositoryAddAdvisor<Course>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext       ctx,
        IRepository<Course> repository,
        Course              entity,
        CancellationToken   ct = default) {
        if (entity.Uid == Guid.Empty)         entity.Uid  = Guid.CreateVersion7();
        if (string.IsNullOrWhiteSpace(entity.Name)) entity.Name = entity.Uid.ToString("N");
        return Task.FromResult(AdviseResult.Continue);
    }
}
```

## Step 4: Expose over HTTP

Add `[HttpResource]` and call `MapHttp()`:

```csharp
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[HttpResource]
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete { /* ... */ }
```

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Course, CourseRequest, CourseDetail, CourseSummary>();
```

`SchemataHttpResourceFeature` synthesizes a `ResourceController<Course, CourseRequest, CourseDetail, CourseSummary>`.
`ResourceControllerConvention` sets the route from `CollectionPath`, giving `/v1/courses` and `/v1/courses/{name}`.

**Assertion:** `GET /v1/courses` returns `200` with a JSON list. `POST /v1/courses` with a valid body returns
`201 Created` with a `name` field.

## Step 5: Expose over gRPC

Add `[GrpcResource]` and call `MapGrpc()`:

```csharp
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[HttpResource]
[GrpcResource]
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete { /* ... */ }
```

```csharp
schema.UseResource()
      .MapHttp()
      .MapGrpc()
      .Use<Course, CourseRequest, CourseDetail, CourseSummary>();
```

`SchemataGrpcResourceFeature` synthesizes a `CourseService` with RPCs `ListCourses`, `GetCourse`, `CreateCourse`,
`UpdateCourse`, and `DeleteCourse`.

**Assertion:** a gRPC client calling `CourseService/GetCourse` with a valid name receives a `CourseDetail`.

## Step 6: Use discovery instead of imperative registration

`SchemataResourceFeature` scans loaded assemblies for `[Resource]`-decorated types during `ConfigureServices`.
Drop the `.Use<>()` call and ensure the assembly holding `Course` is loaded first:

```csharp
schema.UseResource()
      .MapHttp()
      .MapGrpc();
// Course is discovered from [Resource<...>] + [HttpResource] + [GrpcResource]
```

**Assertion:** the app starts and `GET /v1/courses` works without an explicit `.Use<Course, ...>()`, provided the
assembly is referenced and loaded.

## Common pitfalls

- **A DTO missing `ICanonicalName`.** All four type parameters must implement it; the handler maps `Name` and
  `CanonicalName` between them.
- **`[Resource<TEntity>]` collapses all four slots to `TEntity`.** Convenient for prototyping, but the same type
  then serves persistence, requests, and responses. Add separate DTOs before exposing the API publicly.
- **Call `MapHttp()`/`MapGrpc()` before `Use<>()`.** The `Use<>()` overloads tag the resource with the
  transport's endpoint name; calling them on the base `SchemataResourceBuilder` skips that tag.
- **Discovery reads `AppDomain.CurrentDomain.GetAssemblies()` at `ConfigureServices` time.** A type in an
  assembly loaded later is not discovered.

## See also

- [Resource Overview](../documents/resource/overview.md)
- [HTTP Transport](../documents/resource/http-transport.md)
- [gRPC Transport](../documents/resource/grpc-transport.md)
- [Canonical Name Routing](canonical-name-routing.md)
