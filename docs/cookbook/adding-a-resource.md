# Adding a Resource

## What you'll build

A `Course` resource exposed over both HTTP REST and gRPC, using separate request, detail, and summary DTOs. You'll see how the four type parameters collapse rightward, how discovery works via `[Resource]`, and how `[HttpResource]` and `[GrpcResource]` control which transports are active.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Resource.Foundation`, `Schemata.Resource.Http`, `Schemata.Resource.Grpc`.

## Step 1: Define the entity and DTOs

All four types must be `class` and implement `ICanonicalName`. Missing slots collapse rightward: `[Resource<TEntity>]` is equivalent to `[Resource<TEntity, TEntity, TEntity, TEntity>]`.

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

// Entity — persisted to the database
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete
{
    public string?         Name         { get; set; }
    public string?         CanonicalName { get; set; }
    public Guid            Uid          { get; set; }
    public DateTimeOffset? CreateTime   { get; set; }
    public DateTimeOffset? UpdateTime   { get; set; }
    public DateTimeOffset? DeleteTime   { get; set; }
    public string?         Title        { get; set; }
    public int             Credits      { get; set; }
}

// Request DTO — used for create and update
public class CourseRequest : ICanonicalName
{
    public string? Name         { get; set; }
    public string? CanonicalName { get; set; }
    public string? Title        { get; set; }
    public int     Credits      { get; set; }
}

// Detail DTO — returned for single-resource reads
public class CourseDetail : ICanonicalName
{
    public string?         Name         { get; set; }
    public string?         CanonicalName { get; set; }
    public string?         Title        { get; set; }
    public int             Credits      { get; set; }
    public DateTimeOffset? CreateTime   { get; set; }
    public DateTimeOffset? UpdateTime   { get; set; }
}

// Summary DTO — returned for each item in list responses
public class CourseSummary : ICanonicalName
{
    public string? Name         { get; set; }
    public string? CanonicalName { get; set; }
    public string? Title        { get; set; }
}
```

**Assertion:** All four types compile. `ResourceAttribute<Course>` is equivalent to `ResourceAttribute<Course, Course, Course, Course>` — use the four-parameter form when the DTOs differ.

## Step 2: Attach the resource attribute

Place `[Resource<...>]` on the entity class. The attribute drives both imperative registration and assembly-scan discovery.

```csharp
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete
{
    // ... fields as above
}
```

**Assertion:** `typeof(Course).GetCustomAttribute<ResourceAttribute>()` returns a non-null instance with `EntityType == typeof(Course)`, `RequestType == typeof(CourseRequest)`, `DetailType == typeof(CourseDetail)`, and `SummaryType == typeof(CourseSummary)`.

## Step 3: Expose over HTTP

Add `[HttpResource]` to the entity class and call `MapHttp()` in startup.

```csharp
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[HttpResource]
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete { ... }
```

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Course, CourseRequest, CourseDetail, CourseSummary>();
```

`[HttpResource]` is a `ResourceEndpointAttributeBase` with `Name = "HTTP"`. `SchemataHttpResourceFeature` reads it during `ConfigureApplication` and synthesizes a `ResourceController<Course, CourseRequest, CourseDetail, CourseSummary>` via `MakeGenericType`. The route is `~/courses`.

**Assertion:** `GET /courses` returns `200 OK` with a JSON list. `POST /courses` with a valid body returns `201 Created` with a `canonical_name` field.

## Step 4: Expose over gRPC

Add `[GrpcResource]` and call `MapGrpc()` in startup.

```csharp
[Resource<Course, CourseRequest, CourseDetail, CourseSummary>]
[HttpResource]
[GrpcResource]
[CanonicalName("courses/{course}")]
public class Course : ICanonicalName, IIdentifier, ITimestamp, ISoftDelete { ... }
```

```csharp
schema.UseResource()
      .MapHttp()
      .MapGrpc()
      .Use<Course, CourseRequest, CourseDetail, CourseSummary>();
```

`[GrpcResource]` has `Name = "gRPC"`. `SchemataGrpcResourceFeature` synthesizes a gRPC service for the resource and maps it to the endpoint router.

**Assertion:** A gRPC client calling `CourseService.GetCourse` with a valid name receives a `CourseDetail` response.

## Step 5: Use discovery instead of imperative registration

If you prefer not to call `.Use<Course, ...>()` in startup, the resource feature scans loaded assemblies for types carrying `[ResourceAttribute]` and registers them automatically. Remove the `.Use<>()` call and ensure the assembly containing `Course` is loaded before `ConfigureServices` runs.

```csharp
schema.UseResource()
      .MapHttp()
      .MapGrpc();
// Course is discovered automatically from [Resource<...>] + [HttpResource] + [GrpcResource]
```

**Assertion:** The app starts and `GET /courses` works without an explicit `.Use<Course, ...>()` call, provided the assembly is referenced and loaded.

## Common pitfalls

- **Missing `ICanonicalName` on a DTO.** All four type parameters must implement `ICanonicalName`. The handler maps `Name` and `CanonicalName` between them. A DTO without the interface compiles but produces null identity fields in responses.
- **`[Resource<TEntity>]` collapses all four slots to `TEntity`.** This is convenient for prototyping but means the same type is used for persistence, requests, and responses. Add separate DTOs before exposing the API publicly to avoid leaking internal fields.
- **`MapHttp()` and `MapGrpc()` must be called before `Use<>()`** in the fluent chain. Calling `Use<>()` first and then `MapHttp()` registers the transport after the resource, which may miss the endpoint synthesis step.
- **`SchemataControllersFeature` strips Schemata assembly parts.** Framework controllers are opt-in. `ResourceControllerFeatureProvider` adds the synthesized controller as an `ApplicationPart` automatically, but any hand-written controller in a `Schemata.*` assembly needs a `SchemataExtensionPart<T>` registration.
- **Discovery scans `AppDomain.CurrentDomain.GetAssemblies()` at `ConfigureServices` time.** Assemblies loaded after that point are not discovered. If your entity lives in a plugin assembly, ensure it is loaded before `UseSchemata` runs.

## See also

- [Resource overview](../documents/resource/overview.md)
- [HTTP transport](../documents/resource/http-transport.md)
- [gRPC transport](../documents/resource/grpc-transport.md)
- [Canonical Name Routing](canonical-name-routing.md)
- [Getting Started guide](../guides/getting-started.md)
