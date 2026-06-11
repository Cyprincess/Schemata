# Delete Pipeline

`ResourceOperationHandler.DeleteAsync` accepts a canonical name string, an optional ETag, and a `ClaimsPrincipal?`. It loads the existing entity, runs it through a fixed sequence of advisor stages, removes it from the repository, and returns a `DeleteResultBase<TDetail>`: a soft delete carries the updated resource in `Detail` per AIP-164, a hard delete carries nothing. Authorization is checked before the entity is loaded, per AIP-211.

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

Receives the principal and the operation token `nameof(Operations.Delete)`. A `Block` throws `NotFoundException`. A `Handle` returns early (the advisor handled the deletion itself, optionally stashing a `DeleteResultBase<TDetail>` in the `AdviceContext`).

### Stage 2: Name parsing

`ApplyIdentifierPredicates` parses the canonical name and applies leaf and parent predicates to the `ResourceRequestContainer<TEntity>`.

### Stage 3: Delete request advisors

```
IResourceDeleteRequestAdvisor<TEntity>
```

Receives a `DeleteRequest { Name, Etag }`, the container, and the principal. Authorization advisors run here when `WithAuthorization()` is configured.

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

`AdviceDeleteFreshness` fires whenever `DeleteRequest.Etag` is non-empty: any value that differs from the entity's current weak tag - including strong-format or malformed tags - throws `ConcurrencyException` per AIP-154. Only an absent or whitespace tag opts out.

### Soft-delete interception

If the entity implements `ISoftDelete`, the repository-layer advisor `AdviceRemoveSoftDelete<TEntity>` intercepts `RemoveAsync` and converts it to an update that sets `DeleteTime = DateTimeOffset.UtcNow`. It returns `AdviseResult.Handle` to prevent the physical delete. The entity is not removed from the database; it is tombstoned.

To physically delete a soft-deleted entity, call `repository.SuppressSoftDelete()` before `RemoveAsync`, or use the built-in `:expunge` method described below.

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
| Entity implements `ISoftDelete`, `SuppressSoftDelete()` called | Physical delete |
| Entity does not implement `ISoftDelete` | Physical delete |

## Extension points

- Implement `IResourceDeleteRequestAdvisor<TEntity>` to add pre-load logic (e.g., dependency checks).
- Implement `IResourceDeleteAdvisor<TEntity>` to add entity-level logic after load (e.g., cascade soft-delete of children).
- Soft deletes run the `IResourceResponseAdvisor<TEntity, TDetail>` stage on the returned detail (freshness ETags apply); hard deletes have no response stage. Post-delete repository side effects should use `IRepositoryCommittedAdvisor<TEntity>` when they need the committed delete snapshot.

## Built-in soft-delete methods

Resources whose entity implements `ISoftDelete` automatically expose three custom methods per AIP-164/165 (each can be disabled via the `Operations` whitelist or overridden by declaring the same verb):

| Method | Route | Behavior |
|---|---|---|
| `:undelete` | `POST {collection}/{name}:undelete` | Clears `DeleteTime`/`PurgeTime` and returns the restored resource; a live resource fails with `AlreadyExistsException` |
| `:expunge` | `POST {collection}/{name}:expunge` | Physically removes a soft-deleted resource; a live resource fails with `FailedPreconditionException` |
| `:purge` | `POST {collection}:purge` | Collection-scoped AIP-165 purge: required `filter` (`"*"` matches all) plus `force`; `force=false` previews `purge_count` and a `purge_sample` of up to 100 names without deleting; `force=true` physically deletes the matches. Executes through `IOperationDispatcher` as an addressable `operations/{uid}` resource - a Scheduling bridge package (`Schemata.Scheduling.Http`/`Schemata.Scheduling.Grpc`) must be installed, otherwise the handler throws `InvalidOperationException` |

## Design motivation

The delete pipeline returns `DeleteResultBase<TDetail>`. A soft delete responds with the updated resource - HTTP `200` with the JSON body, gRPC the detail message - so callers observe `delete_time` per AIP-164. A hard delete has no body: HTTP `204 No Content`, gRPC `Empty`.

## Caveats

- After a soft-delete, `GetAsync` still returns the entity (it suppresses soft-delete filtering). The caller can inspect `DeleteTime` to determine whether the resource is tombstoned.

## See also

- [Resource Overview](overview.md)
- [Update Pipeline](update-pipeline.md)
- [Advice Pipeline](../core/advice-pipeline.md)
- [Entity Traits](../entity/traits.md)
- [Repository Mutation Pipeline](../repository/mutation-pipeline.md)
