# Object Mapping

Split the API surface from the stored entity. Introduce a request DTO and two response DTOs so
clients see only the fields they need, while the `Student` entity stays the database shape. This
guide builds on [Getting Started](getting-started.md).

## What you have

The `Student` from [Getting Started](getting-started.md) uses the entity type for all four resource
type parameters. This guide replaces three of them with dedicated DTOs and maps between them.

## Add the mapping package

`Schemata.Application.Complex.Targets` already pulls in `Schemata.Mapping.Mapster`. To compose
packages by hand:

```shell
dotnet add package --prerelease Schemata.Mapping.Foundation
dotnet add package --prerelease Schemata.Mapping.Mapster
```

## Create the DTOs

`StudentRequest.cs` is the input for `POST` (create) and `PATCH` (update). `IUpdateMask` carries a
field mask for partial updates; `IValidation` enables dry-run requests:

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

`StudentDetail.cs` is the output for single-entity responses:

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

`StudentSummary.cs` is the output for list responses:

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

`UseMapping()` returns a `SchemataMappingBuilder`. Chain `UseMapster()` to select the engine, then
register each pair with `Map<TSource, TDestination>()`. Same-named, same-typed properties map
automatically:

```csharp
schema.UseMapping()
      .UseMapster()
      .Map<Student, StudentDetail>()
      .Map<Student, StudentSummary>()
      .Map<StudentRequest, Student>();
```

For a field whose names differ, pass a configure action and pair `For` with `From`:

```csharp
schema.UseMapping()
      .UseMapster()
      .Map<StudentRequest, Student>(map => {
          map.For(d => d.FullName).From(s => s.FullName);
      });
```

## Update the resource registration

Point `Use<>()` at the new types:

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

A `PATCH` request maps through `ISimpleMapper`. The update handler reads `IUpdateMask.UpdateMask`:

- No mask, or the `*` wildcard, maps with `ISimpleMapper.Map(request, entity)` — a merge that keeps
  the destination value for any null or blank source field.
- A mask resolves its `snake_case` paths to CLR property paths, then maps with
  `ISimpleMapper.Map(request, entity, fields)`. Listed fields are written authoritatively;
  everything else keeps its current value.

```shell
curl -X PATCH http://localhost:5000/students/<name> \
     -H "Content-Type: application/json" \
     -d '{"age":21,"update_mask":"age"}'
```

Only `age` changes; `full_name` keeps its previous value. A masked field is authoritative, so
masking `full_name` with a null body value clears it. The mask is a comma-separated list of
`snake_case` field names.

## Verify

```shell
dotnet run
```

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

The response is a `StudentDetail`:

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

List responses return `StudentSummary` items, without timestamps or ETags.

## Next steps

- [Concurrency and Freshness](concurrency-and-freshness.md) — `StudentRequest`/`StudentDetail` already implement `IFreshness`
- [Validation](validation.md) — `StudentRequest` already implements `IValidation`
- [Filtering and Pagination](filtering-and-pagination.md) — list endpoint returns `StudentSummary`

## See also

- [Mapping overview](../documents/mapping/overview.md) — `ISimpleMapper`, merge and mask semantics
- [Multi-engine mapping](../cookbook/multi-engine-mapping.md) — switching engines and field masks
