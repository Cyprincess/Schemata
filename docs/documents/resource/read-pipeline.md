# Read Pipeline

The resource system provides two read operations: **List** and **Get**. Both run through the advisor pipeline and share the `IResourceRequestAdvisor<TEntity>` gate. Neither operation writes to the repository; both use `_repository.Once()` to get a fresh, isolated repository instance that doesn't pollute the caller's repository state.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` (lines 105-318) |
| `Schemata.Resource.Foundation` | `ResourceRequestContainer.cs` |
| `Schemata.Resource.Foundation` | `Advisors/IResourceListRequestAdvisor.cs` |
| `Schemata.Resource.Foundation` | `Advisors/IResourceGetRequestAdvisor.cs` |
| `Schemata.Resource.Foundation` | `Advisors/IResourceListResponseAdvisor.cs` |
| `Schemata.Resource.Foundation` | `Advisors/IResourceResponseAdvisor.cs` |

## List operation

`ListAsync` accepts a `ListRequest` and returns a `ListResultBase<TSummary>`.

### Stage 1: Gate check

```
IResourceRequestAdvisor<TEntity>
```

Receives the principal and the operation token `nameof(Operations.List)`. A `Block` throws `NotFoundException`.

### Stage 2: List request advisors

```
IResourceListRequestAdvisor<TEntity>
```

Receives the `ListRequest`, a `ResourceRequestContainer<TEntity>`, and the principal. Authorization advisors run here when `WithAuthorization()` is configured.

### Stage 3: Parent scoping

If `request.Parent` is non-empty, `ResourceNameDescriptor.ParseParent` parses it against the entity's pattern and calls `container.ApplyModification(predicate)` to scope the query to that parent. If any parent segment is `"-"` (AIP-159 wildcard) and the entity does not have `[ReadAcross]`, a `ValidationException` is thrown.

### Stage 4: Page token validation

The page token is decrypted from `request.PageToken` (Brotli-compressed JSON sealed with ASP.NET Core Data Protection, so clients can neither read nor alter it). A token that fails to decode throws `ValidationException` with reason `FieldReasons.InvalidPageToken`. If a token is present, its `Parent`, `Filter`, `OrderBy`, and `ShowDeleted` fields must match the current request — mismatches throw `ValidationException` with reason `FieldReasons.InvalidPageToken`. A negative `page_size` throws `ValidationException` with reason `FieldReasons.InvalidPageSize`; zero or absent falls back to 25; values above 100 are capped at 100. A deterministic key ordering (primary key, falling back to `Uid`, then `Name`) is always appended after any `order_by`, keeping page boundaries stable.

### Stage 5: Filter compilation

If `request.Filter` is non-empty, `ListAsync` resolves `IExpressionCompiler` keyed by `AipLanguage.Name` ("aip") and compiles the filter string to an `Expression<Func<TEntity, bool>>`. The compiled predicate is applied via `container.ApplyFiltering(filter)`.

This key is hard-wired to `AipLanguage.Name`. Registering a different `IExpressionCompiler` under a custom key does not affect `ListAsync`. See [Filtering](filtering.md) for details.

### Stage 6: Order compilation

If `request.OrderBy` is non-empty, `ListAsync` resolves `IOrderCompiler` keyed by `AipLanguage.Name` and compiles the order expression to a `Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>`. Applied via `container.ApplyOrdering(order)`.

### Stage 7: Soft-delete suppression

If `request.ShowDeleted` is `true`, the repository instance is wrapped with `SuppressQuerySoftDelete()` so tombstoned rows are included.

### Stage 8: Count and fetch

```csharp
var totalSize = ResolveTotalSizeMode() switch {
    TotalSizeMode.None      => (int?)null,
    TotalSizeMode.Estimated => (int)Math.Min(await repository.EstimateCountAsync(q => container.Query(q), ct), int.MaxValue),
    var _                   => await repository.CountAsync(q => container.Query(q), ct),
};
container.ApplyPaginating(token);
var entities = repository.ListAsync(q => container.Query(q), ct);
var summaries = await _mapper.EachAsync<TEntity, TSummary>(entities, ct).ToListAsync(ct);
```

The total count is fetched before pagination is applied. Pagination (skip/take) is then added to the container and the entity stream is mapped to summaries.

### Stage 9: Next page token

The query fetches one look-ahead row beyond the page size; a next page token is produced only when that extra row exists, so the exactly-full last page omits `next_page_token` per AIP-158. The token advances `token.Skip` by `token.PageSize` before sealing.

### Stage 10: List response advisors

```
IResourceListResponseAdvisor<TSummary>
```

Receives the immutable summary array and the principal. Use this stage to post-process the list (e.g., redact fields, add computed properties).

## Get operation

`GetAsync` accepts a canonical name string and returns a `GetResultBase<TDetail>`.

### Stage 1: Gate check

```
IResourceRequestAdvisor<TEntity>
```

Receives the principal and the operation token `nameof(Operations.Get)`.

### Stage 2: Name parsing

`ApplyIdentifierPredicates` calls `ResourceNameDescriptor.ForType<TEntity>().ParseCanonicalName(name)` to split the name into parent values and a leaf name. Both are applied as `Where` predicates on the container. An empty or unparseable name throws `ValidationException`.

### Stage 3: Get request advisors

```
IResourceGetRequestAdvisor<TEntity>
```

Receives a `GetRequest { Name = name }`, the container, and the principal.

### Stage 4: Entity load

```csharp
var entity = await _repository.Once()
                              .SuppressQuerySoftDelete()
                              .SingleOrDefaultAsync(q => container.Query(q), ct)
          ?? throw ResourceNotFound(name);
```

Get always suppresses soft-delete filtering so it can return tombstoned rows (the caller can inspect `DeleteTime`). If no entity matches, a `NotFoundException` is thrown with a `ResourceInfoDetail` payload.

### Stage 5: Response mapping and advisors

The entity is mapped to `TDetail`, then `IResourceResponseAdvisor<TEntity, TDetail>` runs. `AdviceResponseFreshness` writes the ETag onto the detail if it implements `IFreshness`.

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` to add pre-query logic (e.g., entitlement filtering via `container.ApplyModification`).
- Implement `IResourceGetRequestAdvisor<TEntity>` to add per-get logic (e.g., audit logging).
- Implement `IResourceListResponseAdvisor<TSummary>` to post-process the list.
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process individual detail DTOs.

## Design motivation

`_repository.Once()` creates a fresh repository instance with a fresh `AdviceContext` for each read. This prevents read-side state (e.g., `SuppressQuerySoftDelete`) from leaking into the repository instance used by write operations. Get always suppresses soft-delete so callers can inspect tombstoned resources; List only suppresses it when `ShowDeleted = true`.

## Caveats

- The filter and order compilers are resolved as keyed singletons. They are registered by `SchemataResourceFeature` via `services.AddAipExpressions()`. If the AIP package is not referenced, `ListAsync` will throw `InvalidOperationException` on any request with a non-empty filter or order.
- `ListAsync` does not support server-side cursors beyond skip/take. For large datasets, use `PageSize` and `PageToken` to paginate.
- `CountAsync` runs a separate query before pagination. On large tables this can be expensive; consider caching the count or using approximate counts.

## See also

- [Resource Overview](overview.md)
- [Filtering](filtering.md)
- [Resource Naming](resource-naming.md)
- [Advice Pipeline](../core/advice-pipeline.md)
- [Repository Query Pipeline](../repository/query-pipeline.md)
