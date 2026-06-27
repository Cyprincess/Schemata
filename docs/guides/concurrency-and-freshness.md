# Concurrency and Freshness

Add optimistic concurrency control to the `Student` entity and see how ETags flow through the resource pipeline. This guide builds on [Object Mapping](object-mapping.md).

## Two interfaces, two layers

Schemata separates concurrency into two complementary interfaces:

| Interface      | Applied to           | Purpose |
| -------------- | -------------------- | ------- |
| `IConcurrency` | Entity               | Adds a non-nullable `Guid Timestamp`. `AdviceAddConcurrency` mints a new GUID on create; the database guards the update when `Timestamp` carries `[ConcurrencyCheck]`. |
| `IFreshness`   | Request/response DTO | Adds `string? EntityTag`. The resource pipeline derives a weak ETag from the entity's `Timestamp` and writes it to the response. On update it reads the ETag from the request and compares it to the stored `Timestamp`. |

`StudentDetail` and `StudentRequest` already implement `IFreshness` from [Object Mapping](object-mapping.md). The only entity change is adding `IConcurrency`.

## Add IConcurrency to Student

In `Student.cs`, add the interface and annotate the property with `[ConcurrencyCheck]` so the database enforces the version on update:

```csharp
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IConcurrency
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    public Guid      Uid           { get; set; }
    public string?   Name          { get; set; }
    public string?   CanonicalName { get; set; }
    public DateTime? CreateTime    { get; set; }
    public DateTime? UpdateTime    { get; set; }
    public DateTime? DeleteTime    { get; set; }
    public DateTime? PurgeTime     { get; set; }

    // IConcurrency
    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }
}
```

After adding this, delete `app.db` and let EF Core recreate the schema with the new `Timestamp` column.

The plumbing is split between the repository pipeline and the database:

1. `AdviceAddConcurrency` (add pipeline) mints `Timestamp` on create.
2. On update, EF Core reads `[ConcurrencyCheck]` and issues `UPDATE ... WHERE Timestamp = @original` while bumping `Timestamp` to a fresh GUID. A stale token matches zero rows, which surfaces as `AbortedException` (mapped to HTTP 409). There is no update-side concurrency advisor.
3. The resource freshness advisors bridge `Timestamp` to the HTTP ETag: the response advisor encodes `Timestamp` as a weak ETag (`W/"<base64url>"`) on the detail DTO, and the update advisor reads the request's ETag and compares it to the stored `Timestamp` before the write.

Without `[ConcurrencyCheck]`, the add stamp still mints a token but the update writes unconditionally, so concurrent writers can lose updates.

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
  "update_time": "2026-06-04T12:00:00Z"
}
```

`create_time` and `update_time` are equal on create — `AdviceAddTimestamp` reads the clock once and assigns both.

Conditional update using `If-Match`:

```shell
curl -X PATCH "http://localhost:5000/students/a1b2c3d4e5f6a7b8" \
     -H "Content-Type: application/json" \
     -H 'If-Match: W/"dGVzdC10aW1lc3RhbXA"' \
     -d '{"age":21}'
```

If another request modified the entity first, the server responds with HTTP 409. Sending the current ETag (from the last read) lets the conditional update succeed.

## Next steps

- [Filtering and Pagination](filtering-and-pagination.md) — query the student list with AIP-160 filter
- [Query Caching](query-caching.md) — committed-pipeline eviction reuses the same advisor family
- [Validation](validation.md) — `StudentRequest` already implements `IValidation`

## See also

- [Traits](../documents/entity/traits.md) — complete trait interface reference
- [Update Pipeline](../documents/resource/update-pipeline.md) — advisor execution order for updates
