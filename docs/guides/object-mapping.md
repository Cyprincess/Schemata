# Object Mapping

Introduce separate request and response DTOs so the API exposes only the fields clients need, while the persistent `Student` entity remains unchanged. This guide builds on [Getting Started](getting-started.md).

## What you have

The `Student` entity from [Getting Started](getting-started.md) uses the entity type for all four resource type parameters. This guide splits that into three dedicated DTOs.

## Add the mapping package

`Schemata.Application.Complex.Targets` already includes `Schemata.Mapping.Mapster`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Mapping.Foundation
dotnet add package --prerelease Schemata.Mapping.Mapster
```

## Create the DTOs

Create `StudentRequest.cs` — the input type for `POST` (create) and `PATCH` (update). `IUpdateMask` enables partial updates; `IValidation` enables dry-run validation:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

public record StudentRequest : ICanonicalName, IFreshness, IUpdateMask, IValidation
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? EntityTag     { get; set; }
    public string? UpdateMask    { get; set; }
    public bool    ValidateOnly  { get; set; }
}
```

Create `StudentDetail.cs` — the output type for single-entity responses:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

public record StudentDetail : ICanonicalName, IFreshness, ITimestamp
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    public string?   Name          { get; set; }
    public string?   CanonicalName { get; set; }
    public string?   EntityTag     { get; set; }
    public DateTime? CreateTime    { get; set; }
    public DateTime? UpdateTime    { get; set; }
}
```

Create `StudentSummary.cs` — the output type for list responses:

```csharp
using Schemata.Abstractions.Entities;

public record StudentSummary : ICanonicalName
{
    public string? FullName      { get; set; }
    public int     Age           { get; set; }
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
```

## Configure mappings

`UseMapping()` returns a `SchemataMappingBuilder`. Chain `UseMapster()` to select the Mapster engine, then register each mapping pair with `Map<TSource, TDestination>()`. When source and destination properties share the same name and type, Mapster maps them automatically:

```csharp
schema.UseMapping()
      .UseMapster()
      .Map<Student, StudentDetail>()
      .Map<Student, StudentSummary>()
      .Map<StudentRequest, Student>();
```

`Map<S, D>()` accepts an optional `Action<Map<S, D>>` for custom field mappings.

## Update the resource registration

Change the `Use<>()` call to reference the new types:

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

| Parameter  | Type             | Purpose                                  |
| ---------- | ---------------- | ---------------------------------------- |
| `TEntity`  | `Student`        | Persistent entity stored in the database |
| `TRequest` | `StudentRequest` | Input DTO for `POST` and `PATCH`         |
| `TDetail`  | `StudentDetail`  | Output DTO for single-entity responses   |
| `TSummary` | `StudentSummary` | Output DTO for list responses            |

## How UpdateMask works

When a `PATCH` request includes `update_mask`, the handler calls `ISimpleMapper.Map(request, entity, fields)`. `SimpleMapperHelper.MapWithMask` saves the values of all non-masked writable properties before mapping, then restores them afterward. Only the listed fields are written to the entity:

```shell
curl -X PATCH http://localhost:5000/students/<name> \
     -H "Content-Type: application/json" \
     -d '{"age":21,"update_mask":"age"}'
```

Only `age` is updated. `full_name` retains its previous value. The mask is a comma-separated list of `snake_case` field names.

## Verify

```shell
dotnet run
```

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
  "entity_tag": null,
  "create_time": "2026-06-04T12:00:00Z",
  "update_time": null
}
```

List responses return `StudentSummary` items without timestamps or ETags.

## See also

- [Unit of Work](unit-of-work.md) — previous in the series: transactional batch mutations
- [Concurrency and Freshness](concurrency-and-freshness.md) — next in the series: optimistic concurrency and ETags
- [Validation](validation.md) — add input validation with FluentValidation
- [Mapping Overview](../documents/mapping/overview.md) — `ISimpleMapper`, `SimpleMapperHelper`, mask semantics
