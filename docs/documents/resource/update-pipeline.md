# Update Pipeline

`ResourceOperationHandler.UpdateAsync` takes a name, a `TRequest`, and a `ClaimsPrincipal?`. It loads the
existing entity, runs the update stages, applies the changes, persists, and returns an `UpdateResultBase<TDetail>`.
Authorization is checked before the entity loads, per AIP-211. The stage order is fixed; advisor `Order` only
sequences advisors within a stage.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.Update.cs`, `ResourceWireMask.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceUpdateRequestSanitize.cs`, `Advisors/AdviceUpdateRequestValidation.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceUpdateRequestIdempotency.cs`, `Advisors/AdviceApplyChildParent.cs`, `Advisors/AdviceUpdateSoftDeleted.cs`, `Advisors/AdviceUpdateFreshness.cs` |
| `Schemata.Abstractions` | `Resource/UpdateResultBase.cs`, `Resource/IUpdateMask.cs` |

## Stages

### 1. Gate — `IResourceRequestAdvisor<TEntity>`

Receives the principal and the token `nameof(Operations.Update)`. `Block` throws `ResourceNotFound(name)`.

### 2. Parent clearing and name binding

`ResourceNameDescriptor.ClearParentProperties(request)` nulls the request's parent-segment properties so a client
cannot re-parent the resource. The handler then sets `request.CanonicalName = name` so the AIP-155 idempotency key
distinguishes updates to different resources that share a `RequestId`. `ApplyIdentifierPredicates` adds the leaf
and parent `Where` predicates to the `ResourceRequestContainer<TEntity>`.

### 3. Update request — `IResourceUpdateRequestAdvisor<TEntity, TRequest>`

| Advisor | What it does |
| --- | --- |
| `AdviceUpdateRequestAnonymous` | Grants anonymous access when configured |
| `AdviceUpdateRequestAuthorize` | Authorizes the request through the access provider |
| `AdviceUpdateRequestSanitize` | Clears server-managed fields (the same `SystemFields` list as Create) |
| `AdviceUpdateRequestValidation` | Runs validation; skipped when `UpdateRequestValidationSuppressed` is present |
| `AdviceUpdateRequestIdempotency` | On a `RequestId` hit returns the cached result; on a miss reserves the key |

### 4. Entity load

The entity is loaded inside `_repository.SuppressQuerySoftDelete()`, so a tombstoned resource can be updated. A
null result throws `ResourceNotFound(name)`.

### 5. Update entity — `IResourceUpdateAdvisor<TEntity, TRequest>`

| Advisor | What it does |
| --- | --- |
| `AdviceApplyChildParent` | Reverse-parses `request.Parent` into the entity's mode-A parent field for `IChild` DTOs; runs first |
| `AdviceUpdateSoftDeleted` | Rejects updates to a soft-deleted entity with `FailedPreconditionException`; runs before freshness |
| `AdviceUpdateFreshness` | Validates the request ETag against the entity's freshness tag per AIP-154; skipped when `FreshnessSuppressed` is present |

`AdviceUpdateFreshness` fires when the request implements `IFreshness` and supplies a non-empty `EntityTag`: any
value differing from the entity's current weak tag throws. Only an absent or whitespace tag opts out.

### 6. Mapping (field mask)

```csharp
var mask = (request as IUpdateMask)?.UpdateMask;
if (mask is null || mask.Trim() == Wildcards.Any) {
    _mapper.Map(request, entity);
} else {
    _mapper.Map(request, entity, ResolveMaskFields(mask));
}
```

With no mask (or `update_mask=*`) the mapper merges the whole request. With a mask, `ResolveMaskFields` converts
the wire paths to CLR leaf paths through `MaskTree.FromWire(typeof(TEntity), mask, false, ResourceWireMask.Convert)`
and copies only those fields. An unknown segment throws `ValidationException` (`InvalidUpdateMask`).

### 7. Persistence

`_repository.UpdateAsync(entity, ct)` then `_repository.CommitAsync(ct)`.

### 8. Response — `IResourceResponseAdvisor<TEntity, TDetail>`

The updated entity is mapped to `TDetail` and the response chain runs (`AdviceResponseParent` derives
`IChild.Parent`; `AdviceResponseFreshness` writes the new ETag; `AdviceResponseReadMask` trims to `read_mask`).

## Field masks (AIP-161)

`UpdateMask` is a comma-separated list of wire-format (snake_case) field paths. Dot paths target nested object
fields; `ResourceWireMask.Convert` maps the AIP wire aliases (`name` to the canonical-name property, `etag` to
the entity-tag property, and the plural collection field) before falling back to PascalCase. Only the listed
fields are applied.

```csharp
public class StudentRequest : ICanonicalName, IUpdateMask {
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? FullName      { get; set; }
    public ProfileRequest? Profile { get; set; }
    public string? UpdateMask    { get; set; }
}

// PATCH /v1/students/alice
// { "profile": { "display_name": "Alice Smith" }, "update_mask": "profile.display_name" }
```

## Extension points

- Implement `IResourceUpdateRequestAdvisor<TEntity, TRequest>` for pre-load logic.
- Implement `IResourceUpdateAdvisor<TEntity, TRequest>` for entity-level logic after load (state transitions).
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process the detail.

## Design rationale

Authorization runs in the request stage, before the entity is loaded, so an unauthorized caller cannot probe
existence by timing a not-found against an authorization failure.

## Caveats

- `UpdateMask` is excluded from `SystemFields`, so `AdviceUpdateRequestSanitize` leaves it on the request and it
  reaches the mapping step.
- `SuppressFreshness = true` on `SchemataResourceOptions` places `FreshnessSuppressed` on `AdviceContext`, which
  bypasses both `AdviceUpdateFreshness` and the ETag written by `AdviceResponseFreshness`.
- `AdviceUpdateRequestIdempotency` keys the cache under `nameof(Operations.Update)`, disjoint from Create and
  custom-method verbs.

## See also

- [Resource Overview](overview.md)
- [Create Pipeline](create-pipeline.md)
- [Delete Pipeline](delete-pipeline.md)
