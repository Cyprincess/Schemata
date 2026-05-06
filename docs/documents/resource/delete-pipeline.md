# Delete Pipeline

The delete operation is handled by `ResourceOperationHandler.DeleteAsync`. It takes an existing `TEntity`, an optional ETag, and a force flag, runs the [advisor pipeline](../core/advice-pipeline.md), and returns a boolean indicating success.

## Method Signature

```csharp
public async Task<bool> DeleteAsync(
    string name, string? etag, bool force,
    ClaimsPrincipal? principal, CancellationToken? ct)
```

The return value is `true` when the entity is deleted (or the operation is handled by an advisor), and `false` when the operation is blocked.

## Pipeline Steps

### 1. General Request Advisor

```
IResourceRequestAdvisor<TEntity> -- Operations.Delete
```

The first gate. Returns `Continue` to proceed, `Handle` to indicate success without deleting, or `Block` to silently deny (returns `false`).

### 2. Delete Request Advisor

```
IResourceDeleteRequestAdvisor<TEntity>
```

Receives a `DeleteRequest` containing:

| Property | Type      | Description                            |
| -------- | --------- | -------------------------------------- |
| `Name`   | `string?` | The entity's name.                     |
| `Etag`   | `string?` | The ETag for concurrency checking.     |
| `Force`  | `bool`    | Whether to bypass the freshness check. |

When `WithAuthorization()` is enabled, `AdviceDeleteRequestAuthorize` runs at order 110,000,000:

1. Checks if the entity type has `[Anonymous(Operations.Delete)]` -- if so, skips authorization.
2. Calls `IAccessProvider<TEntity, TRequest>.HasAccessAsync` with the current `ClaimsPrincipal`.
3. Throws `AuthorizationException` if access is denied.

### 3. Delete Entity Advisor

```
IResourceDeleteAdvisor<TEntity>
```

This advisor has access to both the entity and the `DeleteRequest`. The built-in advisor:

| Order       | Advisor                 | Behavior                                             |
| ----------- | ----------------------- | ---------------------------------------------------- |
| 100,000,000 | `AdviceDeleteFreshness` | Enforces optimistic concurrency via ETag comparison. |

#### Freshness Validation

`AdviceDeleteFreshness` enforces concurrency checking with these rules:

1. If `DeleteRequest.Force` is `true`, the freshness check is skipped entirely.
2. If a `SuppressFreshness` marker is present in the `AdviceContext`, the check is skipped.
3. If the entity does not implement `IConcurrency` or has no `Timestamp`, the check is skipped.
4. If `DeleteRequest.Etag` is null, empty, or does not start with `W/`, the check is skipped.
5. Otherwise, the advisor computes the expected ETag from the entity's `IConcurrency.Timestamp` and compares it with the request's `Etag`. A mismatch throws `ConcurrencyException`.

### 4. Persistence

The entity is removed from the repository and committed:

```csharp
await _repository.RemoveAsync(entity, ct);
await _repository.CommitAsync(ct);
```

Whether this performs a physical delete or a soft delete depends on the repository implementation and whether the entity implements `ISoftDelete`. The `RemoveAsync` method on the repository handles this distinction -- entities that implement `ISoftDelete` get their `DeleteTime` set and are marked as deleted rather than physically removed.

### 5. Result

Returns `true` on success. In the [HTTP transport](./http-transport.md), this becomes a 204 No Content response.

## Soft Delete Integration

The resource system integrates with the `ISoftDelete` interface from `Schemata.Abstractions.Entities`:

```csharp
public interface ISoftDelete
{
    DateTime? DeleteTime { get; set; }
    DateTime? PurgeTime { get; set; }
}
```

When an entity implements `ISoftDelete`:

- **Delete**: The repository's `RemoveAsync` sets `DeleteTime` rather than physically deleting the row.
- **List**: By default, soft-deleted entities are excluded from query results. Set `showDeleted=true` on the `ListRequest` to include them.
- **Get/Find**: The handler calls `SuppressQuerySoftDelete()` when resolving entities by name, so soft-deleted entities can be found for get, update, and delete operations.

The `Force` flag on `DeleteRequest` bypasses the freshness check but does not change the soft-delete behavior. Whether the delete is soft or physical is determined solely by the entity's implementation of `ISoftDelete` and the repository's handling.

## Implementing a Custom Delete Advisor

To add custom logic before deletion:

```csharp
public class ProtectActiveOrders<TEntity> : IResourceDeleteAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => 50_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, TEntity entity, DeleteRequest request,
        ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        // Custom business logic to prevent deletion
        if (entity is Order { Status: "active" })
        {
            throw new FailedPreconditionException("Cannot delete active orders.");
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

## No Response Advisors

Unlike Create, Update, and Get, the delete pipeline does not run `IResourceResponseAdvisor<TEntity, TDetail>`. Delete operations return a simple boolean and do not produce a detail DTO.
