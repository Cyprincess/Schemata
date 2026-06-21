# Delete Pipeline

`ResourceOperationHandler.DeleteAsync` takes a name, an optional ETag, and a `ClaimsPrincipal?`. It loads the
entity, runs the delete stages, removes it, and returns a `DeleteResultBase<TDetail>`: a soft delete carries the
updated resource in `Detail` per AIP-164, a hard delete carries nothing. Authorization is checked before the
entity loads, per AIP-211. The stage order is fixed; advisor `Order` only sequences advisors within a stage.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.Delete.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceDeleteFreshness.cs`, `Advisors/IResourceDeleteRequestAdvisor.cs`, `Advisors/IResourceDeleteAdvisor.cs` |
| `Schemata.Resource.Foundation` | `UndeleteHandler.cs`, `ExpungeHandler.cs`, `PurgeHandler.cs` |
| `Schemata.Abstractions` | `Resource/DeleteRequest.cs`, `Resource/DeleteResultBase.cs`, `Entities/ISoftDelete.cs` |

## Stages

### 1. Gate — `IResourceRequestAdvisor<TEntity>`

Receives the principal and the token `nameof(Operations.Delete)`. `Block` throws `ResourceNotFound(name)`.
`Handle` returns the stashed result, or the fallback `() => new()` (an empty delete result). The handler then
builds a `DeleteRequest { Name, Etag, AllowMissing }`.

### 2. Name predicates

`ApplyIdentifierPredicates` adds the leaf and parent `Where` predicates to the
`ResourceRequestContainer<TEntity>`.

### 3. Delete request — `IResourceDeleteRequestAdvisor<TEntity>`

Receives the `DeleteRequest`, the container, and the principal. Authorization advisors run here when
`WithAuthorization()` is configured:

| Advisor | What it does |
| --- | --- |
| `AdviceDeleteRequestAnonymous` | Grants anonymous access when configured |
| `AdviceDeleteRequestAuthorize` | Authorizes the request through the access provider |

### 4. Entity load

The entity is loaded inside `_repository.SuppressQuerySoftDelete()`, so an already-tombstoned entity can be
hard-deleted. A null result throws `ResourceNotFound(name)` — unless `DeleteRequest.AllowMissing` is set
(AIP-135), in which case the delete returns an empty success without committing. Over HTTP the flag is the
`allow_missing` query parameter; over gRPC it is `DeleteRequest.AllowMissing`.

### 5. Delete entity — `IResourceDeleteAdvisor<TEntity>`

| Advisor | What it does |
| --- | --- |
| `AdviceDeleteFreshness` | Validates `DeleteRequest.Etag` against the entity's freshness tag per AIP-154; skipped when `FreshnessSuppressed` is present |

`AdviceDeleteFreshness` fires when `Etag` is non-empty: any value differing from the entity's current weak tag
throws `ConcurrencyException`. Only an absent or whitespace tag opts out.

### 6. Persistence and soft-delete branching

`_repository.RemoveAsync(entity, ct)` then `_repository.CommitAsync(ct)`. For an `ISoftDelete` entity, a
repository remove advisor turns the physical delete into an update that sets `DeleteTime`. After commit the
handler inspects the entity: `entity is ISoftDelete { DeleteTime: not null }` identifies the soft path, maps the
entity to `TDetail`, runs `IResourceResponseAdvisor<TEntity, TDetail>`, and returns it in `Detail`. A hard delete
returns an empty `DeleteResultBase<TDetail>`.

## Soft delete vs. hard delete

| Scenario | Result |
| --- | --- |
| Entity implements `ISoftDelete` | Row tombstoned (`DeleteTime` set); `Detail` carries the updated resource |
| Entity does not implement `ISoftDelete` | Row removed; `Detail` is null |

The transport renders this split: HTTP returns `200` with the JSON body for a soft delete and `204 No Content`
for a hard delete; gRPC returns the detail message or `google.protobuf.Empty`.

## Built-in soft-delete methods

`SchemataResourceFeature.RegisterResource` adds three AIP-164/165 custom methods to every `ISoftDelete` resource.
Each is skipped when the `Operations` whitelist excludes it or the entity already declares the same verb.

| Method | Route | Handler | Behavior |
| --- | --- | --- | --- |
| `:undelete` | `POST /v1/{collection}/{name}:undelete` | `UndeleteHandler<TEntity, TDetail>` | Clears `DeleteTime` and `PurgeTime`, returns the restored detail; a live resource throws `AlreadyExistsException` |
| `:expunge` | `POST /v1/{collection}/{name}:expunge` | `ExpungeHandler<TEntity>` | Physically removes a tombstoned resource under `SuppressSoftDelete()`, returns `EmptyResourceResponse`; a live resource throws `FailedPreconditionException` |
| `:purge` | `POST /v1/{collection}:purge` | `PurgeHandler<TEntity>` | Collection-scoped AIP-165 purge dispatched through `IScheduler` as a `PurgeJob<TEntity>` long-running operation; the job and its `ScheduledJobBinding` are registered only when the built-in purge method is active, and the handler throws `FAILED_PRECONDITION` when no scheduler is enabled |

## Extension points

- Implement `IResourceDeleteRequestAdvisor<TEntity>` for pre-load logic (dependency checks).
- Implement `IResourceDeleteAdvisor<TEntity>` for entity-level logic after load (cascade soft-delete).
- A soft delete runs the response chain on the returned detail; a hard delete has no response stage.

## Design rationale

Returning `DeleteResultBase<TDetail>` lets a soft delete surface the tombstoned resource so callers observe
`delete_time` per AIP-164, while a hard delete returns nothing. Suppressing the soft-delete query filter during
the load lets the same path hard-delete an already-tombstoned row.

## Caveats

- After a soft delete, `GetAsync` still returns the entity (it suppresses the soft-delete filter); inspect
  `DeleteTime` to tell whether it is tombstoned.
- `:expunge` is authorized as the `expunge` permission, independent of `delete`.

## See also

- [Resource Overview](overview.md)
- [Update Pipeline](update-pipeline.md)
- [Custom Methods](custom-methods.md)
