# Update Pipeline

`ResourceOperationHandler.UpdateAsync` accepts a canonical name string, a `TRequest` DTO, and a `ClaimsPrincipal?`. It loads the existing entity, runs it through a fixed sequence of advisor stages, applies the changes, persists, and returns an `UpdateResultBase<TDetail>`. Authorization is checked before the entity is loaded, per AIP-211.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` (lines 436-514) |
| `Schemata.Resource.Foundation` | `Advisors/AdviceUpdateRequestSanitize.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceUpdateRequestValidation.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceUpdateRequestIdempotency.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceUpdateFreshness.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceResponseFreshness.cs` |

## Pipeline walkthrough

### Stage 1: Gate check

```
IResourceRequestAdvisor<TEntity>
```

Receives the principal and the operation token `nameof(Operations.Update)`. A `Block` throws `NotFoundException`.

### Stage 2: Parent property clearing

Before the request advisors run, `ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request)` sets all parent-segment properties on the request to `null`. This prevents clients from changing the parent of a resource via an update.

### Stage 3: Name parsing

`ApplyIdentifierPredicates` parses the canonical name and applies leaf and parent predicates to the `ResourceRequestContainer<TEntity>`.

### Stage 4: Update request advisors

```
IResourceUpdateRequestAdvisor<TEntity, TRequest>
```

Built-in advisors run in this order:

| Order | Advisor | What it does |
|---|---|---|
| `AdviceUpdateRequestAnonymous.DefaultOrder` (80M) | `AdviceUpdateRequestAnonymous` | Honors `[Anonymous(Operations.Update)]` |
| `AdviceUpdateRequestAuthorize.DefaultOrder` (100M) | `AdviceUpdateRequestAuthorize` | Calls `IAccessProvider`; throws `AuthorizationException` on denial |
| `AdviceUpdateRequestSanitize.DefaultOrder` (110M) | `AdviceUpdateRequestSanitize` | Clears server-managed fields on the request (same `SystemFields` list as Create) |
| `AdviceUpdateRequestValidation.DefaultOrder` (120M) | `AdviceUpdateRequestValidation` | Runs all `IValidationAdvisor<TRequest>` implementations |
| `AdviceUpdateRequestIdempotency.DefaultOrder` (130M) | `AdviceUpdateRequestIdempotency` | Checks cache for a prior result keyed by `IRequestIdentification.RequestId` under operation token `nameof(Operations.Update)`; returns `Handle` on hit, reserves the key on miss |

### Stage 5: Entity load

```csharp
var entity = await _repository.Once()
                              .SuppressQuerySoftDelete()
                              .SingleOrDefaultAsync(q => container.Query(q), ct)
          ?? throw ResourceNotFound(name);
```

The entity is loaded with soft-delete suppressed so tombstoned resources can be updated (e.g., to restore them). If no entity matches, `NotFoundException` is thrown.

### Stage 6: Update entity advisors

```
IResourceUpdateAdvisor<TEntity, TRequest>
```

| Order | Advisor | What it does |
|---|---|---|
| `Orders.Base` (100M) | `AdviceUpdateFreshness` | Compares the request ETag (`W/"..."`) against the entity's `IConcurrency.Timestamp`; throws `ConcurrencyException` on mismatch |

`AdviceUpdateFreshness` fires whenever the request implements `IFreshness` and supplies a non-empty ETag: any value that differs from the entity's current weak tag - including strong-format or malformed tags - throws `ConcurrencyException` per AIP-154. Only an absent or whitespace tag opts out. `AdviceUpdateSoftDeleted` runs before it and rejects updates to soft-deleted entities with `FailedPreconditionException` (suppressed by the `SoftDeleteGuardSuppressed` context marker).

### Stage 7: Mapping

```csharp
if (request is IUpdateMask { UpdateMask: { } mask }) {
    var fields = MaskTree.FromWire(typeof(TEntity), mask, allowCollectionTraversal: false)
                         .LeafPaths();
    _mapper.Map(request, entity, fields);
} else {
    _mapper.Map(request, entity);
}
```

If the request implements `IUpdateMask` and `UpdateMask` is non-null, only the listed fields are copied from the request to the entity. Field paths are validated against the entity type, converted from wire format (snake_case) to CLR member dot paths via `MaskTree`, and rejected with `invalid_update_mask` when a segment is unknown or traverses a collection element.

### Stage 8: Persistence

```csharp
await _repository.UpdateAsync(entity, ct)
await _repository.CommitAsync(ct)
```

The entity is updated in the repository and committed. `EntityFrameworkCoreRepository.UpdateAsync` calls `Detach(entity)` before `Context.Update(entity)` to clear any tracker entry left by an earlier load in the same provider context. See [Detach before Update](../repository/providers.md#detach-before-update).

### Stage 9: Response mapping and advisors

The updated entity is mapped to `TDetail`, then `IResourceResponseAdvisor<TEntity, TDetail>` runs. `AdviceResponseFreshness` writes the new ETag onto the detail.

## Field masks

Field masks follow AIP-161. The `UpdateMask` property on the request is a comma-separated list of field paths in wire format (snake_case). Dot paths update nested object fields, for example `profile.display_name`. Collection element traversal is not supported for updates. Only the listed fields are applied; all others retain their current values on the entity.

```csharp
// Request with a field mask
public class StudentRequest : ICanonicalName, IUpdateMask {
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? DisplayName { get; set; }
    public ProfileRequest? Profile { get; set; }
    public string? UpdateMask { get; set; }
}

public class ProfileRequest {
    public string? DisplayName { get; set; }
    public string? Locale { get; set; }
}

// PATCH /students/alice
// Body: { "profile": { "display_name": "Alice Smith" }, "update_mask": "profile.display_name" }
```

## Extension points

- Implement `IResourceUpdateRequestAdvisor<TEntity, TRequest>` to add pre-load logic.
- Implement `IResourceUpdateAdvisor<TEntity, TRequest>` to add entity-level logic after load (e.g., state machine transitions).
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process the detail DTO.

## Design motivation

Authorization runs before the entity is loaded (stage 4, not stage 5) per AIP-211. This prevents timing attacks where an attacker probes entity existence by observing whether authorization or not-found errors are returned. The entity is loaded only after the request has been authorized.

## Caveats

- Requests without an ETag bypass the freshness check. To enforce mandatory ETags, add a validation advisor that rejects requests with an empty `EntityTag`.
- An omitted `update_mask` behaves as an implicit mask of populated fields: the framework mappers ignore null and whitespace-only source members when merging onto an existing entity, while an explicit mask stays authoritative and can clear fields. `update_mask=*` performs full replacement.
- `read_mask` supports the same AIP-161 dot syntax for nested response fields. Read masks may traverse collection elements, so `courses.title` keeps each course title and clears sibling course fields.
- Requests implementing `IAllowMissing` with `AllowMissing = true` create the resource when the addressed name does not exist (AIP-134): create-stage advisors run, every field applies, the mask is ignored, and the name comes from the request URI. Without the flag a missing resource fails with `NotFoundException`.
- `UpdateMask` is excluded from `SystemFields`, so `AdviceUpdateRequestSanitize` leaves it on the request and the mask reaches the mapping step.
- `SuppressFreshness = true` on `SchemataResourceOptions` sets `FreshnessSuppressed` in `AdviceContext`, which causes `AdviceUpdateFreshness` to skip the ETag check for all requests.
- `AdviceUpdateRequestIdempotency` only runs when the request implements `IRequestIdentification` and `UpdateIdempotencySuppressed` is not present on `AdviceContext`. Set `ctx.Set(new UpdateIdempotencySuppressed())` from an upstream advisor to bypass the idempotency lane for that request. The cache key format mirrors Create: `idempotency\x1e{Operation}\x1e{RequestId}` with `Operation = nameof(Operations.Update)`.

## See also

- [Resource Overview](overview.md)
- [Create Pipeline](create-pipeline.md)
- [Advice Pipeline](../core/advice-pipeline.md)
- [Entity Traits](../entity/traits.md)
- [Repository Mutation Pipeline](../repository/mutation-pipeline.md)
