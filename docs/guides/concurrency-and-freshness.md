# Concurrency and Freshness

Add optimistic concurrency control to the `Student` entity and see how ETags flow through the resource pipeline. This guide builds on [Object Mapping](object-mapping.md).

## Two interfaces, two layers

Schemata separates concurrency into two complementary interfaces:

| Interface      | Applied to           | Purpose |
| -------------- | -------------------- | ------- |
| `IConcurrency` | Entity               | Adds `Guid? Timestamp`. The repository mints a new GUID on every create and update. |
| `IFreshness`   | Request/response DTO | Adds `string? EntityTag`. The resource pipeline derives a weak ETag from the entity's `Timestamp` and writes it to the response. On update/delete it reads the ETag from the request and compares it to the stored `Timestamp`. |

`StudentDetail` and `StudentRequest` already implement `IFreshness` from [Object Mapping](object-mapping.md). The only entity change is adding `IConcurrency`.

## Add IConcurrency to Student

In `Student.cs`, add the interface and its property:

```csharp
using Schemata.Abstractions.Entities;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IConcurrency
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    public Guid      Uid          { get; set; }
    public string?   Name         { get; set; }
    public string?   CanonicalName { get; set; }
    public DateTime? CreateTime   { get; set; }
    public DateTime? UpdateTime   { get; set; }
    public DateTime? DeleteTime   { get; set; }
    public DateTime? PurgeTime    { get; set; }

    // IConcurrency
    public Guid? Timestamp { get; set; }
}
```

After adding this, delete `app.db` and let EF Core recreate the schema with the new `Timestamp` column.

The built-in advisors handle everything automatically:

1. `AdviceAddConcurrency` mints `Timestamp` on create.
2. `AdviceUpdateConcurrency` loads the current row, checks the version, then rotates `Timestamp` on update.
3. `AdviceResponseFreshness` encodes `Timestamp` as a weak ETag (`W/"<base64url>"`) and writes it to `IFreshness.EntityTag` on the detail DTO. Its `Order` is `100_000_000`.
4. `AdviceUpdateFreshness` reads the ETag from the request body, `etag` query parameter, or `If-Match` header (in that order) and compares it to the stored `Timestamp`. A mismatch returns HTTP 409. Its `Order` is `300_000_000`.
5. `AdviceDeleteFreshness` performs the same check on delete. Pass `force=true` to skip it.

## Verify

```shell
dotnet run
```

Create a student and observe the non-null `entity_tag`:

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

```json
{
  "full_name": "Alice",
  "age": 20,
  "name": "students/a1b2c3d4e5f6a7b8",
  "entity_tag": "W/\"dGVzdC10aW1lc3RhbXA\"",
  "create_time": "2026-06-04T12:00:00Z",
  "update_time": null
}
```

Conditional update using `If-Match`:

```shell
curl -X PATCH "http://localhost:5000/students/a1b2c3d4e5f6a7b8" \
     -H "Content-Type: application/json" \
     -H 'If-Match: W/"dGVzdC10aW1lc3RhbXA"' \
     -d '{"age":21}'
```

If another request modified the entity first, the server responds with HTTP 409. To skip the check on delete, pass `force=true`:

```shell
curl -X DELETE "http://localhost:5000/students/a1b2c3d4e5f6a7b8?force=true"
```

## See also

- [Object Mapping](object-mapping.md) — `StudentRequest` and `StudentDetail` with `IFreshness`
- [Filtering and Pagination](filtering-and-pagination.md) — query the student list
- [Traits](../documents/entity/traits.md) — complete trait interface reference
- [Update Pipeline](../documents/resource/update-pipeline.md) — advisor execution order for updates
