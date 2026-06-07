# Delete Pipeline

`ResourceOperationHandler.DeleteAsync` accepts a canonical name string, an optional ETag, a force flag, and a `ClaimsPrincipal?`. It loads the existing entity, runs it through a fixed sequence of advisor stages, removes it from the repository, and returns a `bool` indicating success. Authorization is checked before the entity is loaded, per AIP-211.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` (lines 534-594) |
| `Schemata.Resource.Foundation` | `Advisors/AdviceDeleteFreshness.cs` |
| `Schemata.Resource.Foundation` | `Advisors/IResourceDeleteRequestAdvisor.cs` |
| `Schemata.Resource.Foundation` | `Advisors/IResourceDeleteAdvisor.cs` |
| `Schemata.Abstractions` | `Entities/ISoftDelete.cs` |

## Pipeline walkthrough

### Stage 1: Gate check

```
IResourceRequestAdvisor<TEntity>
```

Receives the principal and the operation token `nameof(Operations.Delete)`. A `Block` returns `false`. A `Handle` returns `true` (the advisor handled the deletion itself).

### Stage 2: Name parsing

`ApplyIdentifierPredicates` parses the canonical name and applies leaf and parent predicates to the `ResourceRequestContainer<TEntity>`.

### Stage 3: Delete request advisors

```
IResourceDeleteRequestAdvisor<TEntity>
```

Receives a `DeleteRequest { Name, Etag, Force }`, the container, and the principal. Authorization advisors run here when `WithAuthorization()` is configured.

| Order | Advisor | What it does |
|---|---|---|
| `AdviceDeleteRequestAnonymous.DefaultOrder` (80M) | `AdviceDeleteRequestAnonymous` | Honors `[Anonymous(Operations.Delete)]` |
| `AdviceDeleteRequestAuthorize.DefaultOrder` (100M) | `AdviceDeleteRequestAuthorize` | Calls `IAccessProvider`; throws `AuthorizationException` on denial |

### Stage 4: Entity load

```csharp
var entity = await _repository.Once()
                              .SuppressQuerySoftDelete()
                              .SingleOrDefaultAsync(q => container.Query(q), ct)
          ?? throw ResourceNotFound(name);
```

Soft-delete is suppressed so already-tombstoned entities can be hard-deleted. If no entity matches, `NotFoundException` is thrown.

### Stage 5: Delete entity advisors

```
IResourceDeleteAdvisor<TEntity>
```

| Order | Advisor | What it does |
|---|---|---|
| `Orders.Base` (100M) | `AdviceDeleteFreshness` | Compares the request ETag against the entity's `IConcurrency.Timestamp`; throws `ConcurrencyException` on mismatch |

`AdviceDeleteFreshness` fires only when the `DeleteRequest.Etag` starts with `W/` and `DeleteRequest.Force` is `false`. Passing `force = true` bypasses the freshness check.

### Soft-delete interception

If the entity implements `ISoftDelete`, the repository-layer advisor `AdviceRemoveSoftDelete<TEntity>` intercepts `RemoveAsync` and converts it to an update that sets `DeleteTime = DateTimeOffset.UtcNow`. It returns `AdviseResult.Handle` to prevent the physical delete. The entity is not removed from the database; it is tombstoned.

To physically delete a soft-deleted entity, call `repository.SuppressRemoveSoftDelete()` before `RemoveAsync`, or use a separate administrative endpoint.

### Stage 6: Persistence

```csharp
await _repository.RemoveAsync(entity, ct)
await _repository.CommitAsync(ct)
```

If `AdviceRemoveSoftDelete` returns `Handle`, the repository skips the physical delete and commits the tombstone update instead.

## Soft-delete vs. physical delete

| Scenario | Behavior |
|---|---|
| Entity implements `ISoftDelete`, no suppression | `RemoveAsync` sets `DeleteTime`; entity remains in DB |
| Entity implements `ISoftDelete`, `SuppressRemoveSoftDelete()` called | Physical delete |
| Entity does not implement `ISoftDelete` | Physical delete |
| `force = true` on `DeleteRequest` | Bypasses ETag check; soft-delete behavior unchanged |

## Extension points

- Implement `IResourceDeleteRequestAdvisor<TEntity>` to add pre-load logic (e.g., dependency checks).
- Implement `IResourceDeleteAdvisor<TEntity>` to add entity-level logic after load (e.g., cascade soft-delete of children).
- The delete pipeline does not have a response advisor stage. Post-delete side effects should use `EnqueueAfterCommit` on `AdviceContext` or the repository.

## Design motivation

The delete pipeline returns `bool`. A successful delete has no body to return; the HTTP transport maps `true` to `204 No Content`, `false` (blocked) to an empty result; the gRPC transport throws `NoContentException` on `false`.

## Caveats

- `AdviceDeleteFreshness` requires the ETag to start with `W/`. A plain ETag without the `W/` prefix is treated as opt-out, not as a mismatch.
- The `force` flag bypasses only the freshness check. It does not bypass authorization or other delete advisors.
- After a soft-delete, `GetAsync` still returns the entity (it suppresses soft-delete filtering). The caller can inspect `DeleteTime` to determine whether the resource is tombstoned.

## See also

- [Resource Overview](overview.md)
- [Update Pipeline](update-pipeline.md)
- [Advice Pipeline](../core/advice-pipeline.md)
- [Entity Traits](../entity/traits.md)
- [Repository Mutation Pipeline](../repository/mutation-pipeline.md)
