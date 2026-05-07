# Concurrency and Freshness

This guide builds on [Object Mapping](object-mapping.md). You will add optimistic concurrency control to the `Student` entity and see how ETags flow through the pipeline.

## How it works

Schemata separates concurrency into two complementary interfaces:

| Interface      | Applied to           | Purpose                                                                                                                                                                                                                                                         |
| -------------- | -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IConcurrency` | Entity               | Adds a `Guid? Timestamp` property. The repository layer generates a new GUID on every create and update.                                                                                                                                                        |
| `IFreshness`   | Request/Response DTO | Adds a `string? EntityTag` property. The resource pipeline derives a weak ETag from the entity's `Timestamp` and writes it to the response DTO. On update/delete, it reads the ETag from the request and compares it to the entity's current `Timestamp` value. |

`StudentDetail` and `StudentRequest` already implement `IFreshness` from the previous guide. The only code change is adding `IConcurrency` to the entity.

## Add IConcurrency to Student

In `Student.cs`, add the `IConcurrency` interface and its property:

```csharp
using Schemata.Abstractions.Entities;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IConcurrency
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

    // IConcurrency
    public Guid? Timestamp { get; set; }
}
```

After adding this, delete your existing `app.db` (or run `EnsureDeletedAsync` followed by `EnsureCreatedAsync`) so EF Core recreates the schema with the new `Timestamp` column.

The built-in advisors handle everything:

1. **On create/update**: `AdviceAddConcurrency` / `AdviceUpdateConcurrency` sets `Timestamp` to a new `Guid`.
2. **On response**: `AdviceResponseFreshness` reads `IConcurrency.Timestamp` from the entity, encodes it as a weak ETag (`W/"<base64url>"`), and assigns it to `IFreshness.EntityTag` on the detail DTO.
3. **On update request**: `AdviceUpdateFreshness` reads the ETag from `IFreshness.EntityTag` on the request DTO (or from the `If-Match` header / `etag` query parameter) and compares it with the entity's current `Timestamp`. A mismatch throws a `ConcurrencyException` (HTTP 409).
4. **On delete request**: `AdviceDeleteFreshness` performs the same check. Passing `force=true` skips the check.

## Verify

```shell
dotnet run
```

### Create and observe the ETag

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

The response includes a non-null `etag`:

```json
{
  "full_name": "Alice",
  "age": 20,
  "name": "a1b2c3d4e5f6a7b8",
  "etag": "W/\"dGVzdC10aW1lc3RhbXA\"",
  "create_time": "2026-03-26T12:00:00Z",
  "update_time": null
}
```

### Partial update with update_mask

The `update_mask` field on `StudentRequest` (from `IUpdateMask`) tells the pipeline which fields to write. When present, only the listed fields are mapped from the request to the entity; all other fields are left unchanged.

```shell
curl -X PATCH http://localhost:5000/students/<name> \
     -H "Content-Type: application/json" \
     -d '{"age":21,"update_mask":"age"}'
```

Only `age` is updated. `full_name` retains its previous value.

The `update_mask` value is a comma-separated list of field names in `snake_case`. For example, `"full_name,age"` would update both fields.

### Conditional update with If-Match

Pass the ETag from a previous response to ensure the update targets the expected version:

```shell
curl -X PATCH http://localhost:5000/<name-from-response> \
     -H "Content-Type: application/json" \
     -H 'If-Match: W/"dGVzdC10aW1lc3RhbXA"' \
     -d '{"age":22}'
```

If the ETag matches, the update succeeds with a new `etag` in the response. If another request modified the entity first, the server responds with HTTP 409:

```json
{
  "error": {
    "code": 409,
    "status": "ABORTED",
    "message": "A concurrency conflict occurred while processing this request.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.ErrorInfo",
        "reason": "CONCURRENCY_MISMATCH"
      }
    ]
  }
}
```

### Alternative: ETag via query parameter

Pass the ETag as the `etag` query parameter instead of the `If-Match` header:

```shell
curl -X PATCH "http://localhost:5000/<name-from-response>?etag=W/\"dGVzdC10aW1lc3RhbXA\"" \
     -H "Content-Type: application/json" \
     -d '{"age":22}'
```

Or include it in the request body via the `etag` field (since `StudentRequest` implements `IFreshness`):

```shell
curl -X PATCH http://localhost:5000/<name-from-response> \
     -H "Content-Type: application/json" \
     -d '{"age":22,"etag":"W/\"dGVzdC10aW1lc3RhbXA\""}'
```

The controller checks all three locations, in order: request body, `etag` query parameter, `If-Match` header.

### Conditional delete

```shell
curl -X DELETE "http://localhost:5000/<name-from-response>?etag=W/\"dGVzdC10aW1lc3RhbXA\""
```

To skip the concurrency check on delete, pass `force=true`:

```shell
curl -X DELETE "http://localhost:5000/<name-from-response>?force=true"
```

Or include it in the request body via the `etag` field (since `StudentRequest` implements `IFreshness`):

```shell
curl -X PATCH http://localhost:5000/students/<name> \
     -H "Content-Type: application/json" \
     -d '{"age":22,"etag":"W/\"dGVzdC10aW1lc3RhbXA\""}'
```

The controller checks all three locations, in order: request body, `etag` query parameter, `If-Match` header.

### Conditional delete

```shell
curl -X DELETE "http://localhost:5000/students/<name>?etag=W/\"dGVzdC10aW1lc3RhbXA\""
```

To skip the concurrency check on delete, pass `force=true`:

```shell
curl -X DELETE "http://localhost:5000/students/<name>?force=true"
```

## Next steps

- [Filtering and Pagination](filtering-and-pagination.md) -- query the student list with filters and pagination
- [Validation](validation.md) -- add input validation with FluentValidation

## Further reading

- [Traits](../documents/entity/traits.md)
- [Update Pipeline](../documents/resource/update-pipeline.md)
