# Create Pipeline

`ResourceOperationHandler.CreateAsync` accepts a `TRequest` DTO and a `ClaimsPrincipal?`, runs it through a fixed sequence of advisor stages, persists the new entity, and returns a `CreateResultBase<TDetail>`. The pipeline is stage-locked: advisor `Order` controls sequencing within a stage, but the stages themselves always run in the order described here.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` (lines 329-407) |
| `Schemata.Resource.Foundation` | `Advisors/AdviceCreateRequestSanitize.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceCreateRequestValidation.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceCreateRequestIdempotency.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceResponseFreshness.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceResponseIdempotency.cs` |

## Pipeline walkthrough

### Stage 1: Gate check

```
IResourceRequestAdvisor<TEntity>
```

The gate runs before any operation-specific logic. It receives the `ClaimsPrincipal?` and the operation token `nameof(Operations.Create)`. A `Block` result returns `CreateResultBase<TDetail>.Blocked` immediately. A `Handle` result returns a cached result from `AdviceContext`.

Authorization advisors (`AdviceCreateRequestAnonymous`, `AdviceCreateRequestAuthorize`) live in this stage when `WithAuthorization()` is called on `SchemataResourceBuilder`.

### Stage 2: Create request advisors

```
IResourceCreateRequestAdvisor<TEntity, TRequest>
```

Receives the `TRequest`, a `ResourceRequestContainer<TEntity>`, and the principal. Built-in advisors run in this order:

| Order | Advisor | What it does |
|---|---|---|
| `AdviceCreateRequestAnonymous.DefaultOrder` (80M) | `AdviceCreateRequestAnonymous` | Honors `[Anonymous(Operations.Create)]`; bypasses authorize |
| `AdviceCreateRequestAuthorize.DefaultOrder` (100M) | `AdviceCreateRequestAuthorize` | Calls `IAccessProvider`; throws `AuthorizationException` on denial |
| `AdviceCreateRequestSanitize.DefaultOrder` (110M) | `AdviceCreateRequestSanitize` | Clears server-managed fields: `Name`, `Uid`, `Timestamp`, `Owner`, `State`, `CreateTime`, `UpdateTime`, `DeleteTime`, `PurgeTime` |
| `AdviceCreateRequestValidation.DefaultOrder` (120M) | `AdviceCreateRequestValidation` | Runs all `IValidationAdvisor<TRequest>` implementations; throws `ValidationException` on failure |
| `AdviceCreateRequestIdempotency.DefaultOrder` (130M) | `AdviceCreateRequestIdempotency` | Checks cache for a prior result keyed by `IRequestIdentification.RequestId` under operation token `nameof(Operations.Create)`; returns `Handle` on hit, reserves the key on miss |

The sanitize advisor clears fields using `AdviceCreateRequestSanitize.SystemFields`, which is a static array of property names matched against `TRequest` by reflection. Properties not present on `TRequest` are silently skipped.

### Stage 3: Mapping

```
_mapper.Map<TRequest, TEntity>(request)
```

The mapper converts the sanitized, validated request DTO to an entity instance. If the result is `null`, a `ValidationException` is thrown with reason `FieldReasons.InvalidPayload`.

### Stage 4: Create entity advisors

```
IResourceCreateAdvisor<TEntity, TRequest>
```

Receives the original `TRequest` and the newly mapped `TEntity`. This is where traits fire:

- `AdviceAddTimestamp<TEntity>` sets `CreateTime` and `UpdateTime` if the entity implements `ITimestamp`.
- `AdviceAddConcurrency<TEntity>` mints a new `Guid` for `Timestamp` if the entity implements `IConcurrency`.
- `AdviceAddCanonicalName<TEntity>` resolves and sets `Name` and `CanonicalName` if the entity implements `ICanonicalName` with a pattern.
- `AdviceAddSoftDelete<TEntity>` sets `DeleteTime = null` if the entity implements `ISoftDelete`.

### Stage 5: Persistence

```
await _repository.AddAsync(entity, ct)
await _repository.CommitAsync(ct)
```

The entity is added to the repository and committed. If the repository is enlisted in a unit of work, commit happens at the UoW boundary. Committed repository advisors run after the commit succeeds.

### Stage 6: Response mapping

```
_mapper.Map<TEntity, TDetail>(entity)
```

The persisted entity (with server-assigned fields populated) is mapped to the detail DTO.

### Stage 7: Response advisors

```
IResourceResponseAdvisor<TEntity, TDetail>
```

| Order | Advisor | What it does |
|---|---|---|
| `Orders.Base` (100M) | `AdviceResponseFreshness` | Writes a weak ETag (`W/"..."`) onto `TDetail.EntityTag` if the detail implements `IFreshness` |
| after freshness | `AdviceResponseIdempotency` | Persists the `CreateResultBase<TDetail>` to cache under the `RequestId` key for future idempotent replays |

### Idempotency key cleanup on failure

If any stage throws after the idempotency key was reserved (stage 2), the `catch` block in `CreateAsync` calls `TryReleaseIdempotencyKeyAsync` to remove the pending sentinel from cache. This prevents the key from being permanently locked by a failed request.

## Extension points

- Implement `IResourceCreateRequestAdvisor<TEntity, TRequest>` to add pre-persistence logic (e.g., quota checks, enrichment).
- Implement `IResourceCreateAdvisor<TEntity, TRequest>` to add entity-level logic after mapping (e.g., setting computed fields).
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process the detail DTO.
- Register advisors as `Scoped` via `services.TryAddEnumerable(ServiceDescriptor.Scoped(...))`.

## Design motivation

Sanitization runs before validation so that validators never see server-managed fields that clients should not supply. Idempotency runs last in the request chain so that authorization, sanitization, and validation are evaluated even on a cache hit's first arrival — a cached result is only returned if the request would have been valid anyway.

## Caveats

- `AdviceCreateRequestIdempotency` requires `ICacheProvider` to be registered. If no cache is configured, the advisor is still registered but throws at runtime when a request carries `IRequestIdentification.RequestId`.
- The idempotency key uses a 5-minute pending TTL (`PendingTtl = TimeSpan.FromMinutes(5)`). Two concurrent requests with the same `RequestId` produce a `ConcurrencyException` for the loser.
- The cache key format is `idempotency\x1e{Operation}\x1e{RequestId}`, where `Operation` is `nameof(Operations.Create)` here. Update and AIP-136 custom methods write under different `Operation` tokens, so a single `RequestId` may legitimately appear in multiple cache entries.
- Suppression flags (`CreateRequestValidationSuppressed`, `CreateIdempotencySuppressed`) are set on `AdviceContext` by `SchemataResourceOptions` at the start of each request. They affect only the current request scope.

## See also

- [Resource Overview](overview.md)
- [Update Pipeline](update-pipeline.md)
- [Advice Pipeline](../core/advice-pipeline.md)
- [Entity Traits](../entity/traits.md)
- [Repository Mutation Pipeline](../repository/mutation-pipeline.md)
