# Create Pipeline

`ResourceOperationHandler.CreateAsync` takes a `TRequest` and a `ClaimsPrincipal?`, runs the create stages,
persists the new entity, and returns a `CreateResultBase<TDetail>`. The stage order is fixed; advisor `Order`
only sequences advisors within a stage.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.Create.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceCreateRequestSanitize.cs`, `Advisors/AdviceCreateRequestValidation.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceCreateRequestIdempotency.cs`, `Advisors/AdviceApplyChildParent.cs` |
| `Schemata.Resource.Foundation` | `Advisors/AdviceResponseFreshness.cs`, `Advisors/AdviceResponseIdempotency.cs`, `Advisors/AdviceFillChildParentResponse.cs` |
| `Schemata.Abstractions` | `Resource/CreateResultBase.cs` |

## Stages

### 1. Gate — `IResourceRequestAdvisor<TEntity>`

Runs first, receiving the `ClaimsPrincipal?` and the operation token `nameof(Operations.Create)`. `Block`
throws `CollectionNotFound()` (a `NotFoundException` naming the collection). `Handle` returns a stashed
`CreateResultBase<TDetail>`. The authorization advisors (`AdviceCreateRequestAnonymous`,
`AdviceCreateRequestAuthorize`) sit in the request stage below and run only when `WithAuthorization()` is set.

### 2. Create request — `IResourceCreateRequestAdvisor<TEntity, TRequest>`

Receives the `TRequest`, a `ResourceRequestContainer<TEntity>`, and the principal. Built-in advisors run in
`Order` sequence:

| Advisor | What it does |
| --- | --- |
| `AdviceCreateRequestAnonymous` | Grants anonymous access when the resource is configured for it |
| `AdviceCreateRequestAuthorize` | Authorizes the request through the access provider |
| `AdviceCreateRequestSanitize` | Clears server-managed fields on the request |
| `AdviceCreateRequestValidation` | Runs validation; skipped when `CreateRequestValidationSuppressed` is present |
| `AdviceCreateRequestIdempotency` | On an AIP-155 `RequestId` hit returns the cached result; on a miss reserves the key |

`AdviceCreateRequestSanitize.SystemFields` is the property-name list cleared from the request:
`Name`, `CanonicalName`, `Timestamp` (`IConcurrency`), `EntityTag` (`IFreshness`), `Uid`, `Owner`, `State`,
`CreateTime`, `UpdateTime`, `DeleteTime`, `PurgeTime`. Names absent from `TRequest` are skipped.

### 3. Mapping

`_mapper.Map<TRequest, TEntity>(request)` converts the sanitized request to an entity. A `null` result throws
`ValidationException` with reason `FieldReasons.InvalidPayload`.

### 4. Create entity — `IResourceCreateAdvisor<TEntity, TRequest>`

Receives the original request and the freshly mapped entity. This is the socket for entity-level logic that must
run before persistence. `AdviceApplyChildParent` reverse-parses `request.Parent` into the entity's mode-A parent
field for `IChild` DTOs; other trait behavior such as timestamp, canonical name, and uniqueness is applied by
repository add advisors during `AddAsync`.

### 5. Persistence

`_repository.AddAsync(entity, ct)` then `_repository.CommitAsync(ct)`. When the handler runs non-finalizing
(inside a batched operation), it maps the staged entity to `TDetail` and returns without committing or running
response advisors.

### 6. Response mapping and advisors

`_mapper.Map<TEntity, TDetail>(entity)` maps the persisted entity (with server-assigned fields populated), then
`IResourceResponseAdvisor<TEntity, TDetail>` runs:

| Advisor | What it does |
| --- | --- |
| `AdviceFillChildParentResponse` | Derives `IChild.Parent` from the entity's canonical name; runs before freshness |
| `AdviceResponseFreshness` | Sets the ETag on `TDetail` when it implements `IFreshness`; skipped when `FreshnessSuppressed` is present |
| `AdviceResponseReadMask` | Trims the detail to the requested AIP-157 `read_mask` fields |
| `AdviceResponseIdempotency` | Caches the result under the reserved `RequestId` key |

## Extension points

- Implement `IResourceCreateRequestAdvisor<TEntity, TRequest>` for pre-persistence logic (quota checks,
  enrichment).
- Implement `IResourceCreateAdvisor<TEntity, TRequest>` for entity-level logic after mapping (computed fields).
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process the detail.
- Register advisors as scoped via `services.TryAddEnumerable(ServiceDescriptor.Scoped(...))`.

## Design rationale

Sanitization runs before validation so validators never see server-managed fields a client should not supply.
Idempotency runs last in the request chain so authorization, sanitization, and validation are evaluated on a
request's first arrival; a cached result is returned only after that request would itself have been valid.

## Caveats

- `AdviceCreateRequestIdempotency` keys the cache on `IRequestIdentification.RequestId` under
  `nameof(Operations.Create)`. Update and each custom-method verb write under their own operation token, so one
  `RequestId` can legitimately appear in several cache entries.
- `CreateRequestValidationSuppressed` is placed on `AdviceContext` by `ResourceAdviceContext.Create` when
  `SchemataResourceOptions.SuppressCreateValidation` is set; it affects only the current request scope.

## See also

- [Resource Overview](overview.md)
- [Update Pipeline](update-pipeline.md)
- [Delete Pipeline](delete-pipeline.md)
