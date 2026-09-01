# Read Pipeline

The resource system has two read operations, List and Get, both on `ResourceOperationHandler`. Neither writes to
the repository. The stage order is fixed; advisor `Order` only sequences advisors within a stage.

## Where the code lives

| Package                        | Key files                                                                                                                                                  |
| ------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.List.cs`, `ResourceOperationHandler.Get.cs`                                                                                      |
| `Schemata.Resource.Foundation` | `Models/PageToken.cs`, `KeyOrdering.cs`                                                                                                                    |
| `Schemata.Common`              | `ResourceRequestContainer.cs`, `ResourceIdentifiers.cs`                                                                                                    |
| `Schemata.Resource.Foundation` | `Advisors/IResourceListRequestAdvisor.cs`, `Advisors/IResourceGetRequestAdvisor.cs`                                                                        |
| `Schemata.Resource.Foundation` | `Advisors/AdviceResponseParent.cs`, `Advisors/AdviceListResponseParent.cs`                                                                                 |
| `Schemata.Abstractions`        | `Resource/ListRequest.cs`, `Resource/GetRequest.cs`, `Resource/ListResultBase.cs`                                                                          |

## List

`ListAsync` takes a `ListRequest` and returns a `ListResultBase<TSummary>`.

### 1. List request — `IResourceListRequestAdvisor<TEntity>`

Receives the `ListRequest`, a `ResourceRequestContainer<TEntity>`, and the principal. Authorization advisors run
here when `WithAuthorization()` is configured; an entitlement advisor adds predicates via
`container.ApplyWhere`.

### 2. Parent scoping

When `request.Parent` is set, `ResourceNameDescriptor.ParseParent` matches it against the entity's pattern. A
parent that fails to match throws `ValidationException` (`INVALID_PARENT`). A `-` wildcard segment on an entity
without `[ReadAcross]` throws `ValidationException` (`CROSS_PARENT_UNSUPPORTED`). Otherwise
`BuildParentPredicate` produces a `Where` predicate applied via `container.ApplyWhere`.

### 3. Page token and paging parameters

`PageToken.FromStringAsync` decodes `request.PageToken`; a token that fails to decode throws `ValidationException`
(`INVALID_PAGE_TOKEN`). A decoded token whose `Parent`, `Filter`, `OrderBy`, or `ShowDeleted` differ from the
current request throws `ValidationException` (`INVALID_PAGE_TOKEN`). A negative `page_size` throws
`ValidationException` (`INVALID_PAGE_SIZE`); `<= 0` defaults to 25 and `> 100` is capped at 100. `Skip` accumulates
onto the token and is floored at 0.

### 4. Filter compilation

When `request.Filter` is set, the handler resolves the requested language from the resource expression profile,
then resolves `IExpressionCompiler` keyed by `resolved.Language`. It parses and compiles the filter to
`Expression<Func<TEntity, bool>>` and applies the pushed or complete predicate through `container.ApplyWhere`.
Residual mode also resolves `IExpressionPushdownPlanner` with the same language key. An `ExpressionException` or
`ArgumentException` becomes `ValidationException` (`INVALID_FILTER`). See [Filtering](filtering.md).

### 5. Order compilation

When `request.OrderBy` is set, the handler resolves the non-keyed `IOrderCompiler` and compiles to
`Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>`. A parse failure becomes `ValidationException`
(`INVALID_ORDER_BY`). `KeyOrdering<TEntity>.Compose` appends a deterministic key ordering after any `order_by`,
keeping page boundaries stable.

### 6. Count and fetch

`ResolveTotalSizeMode()` selects the count strategy: `None` skips counting (`TotalSize` is null), `Estimated`
calls `_repository.EstimateCountAsync`, otherwise `_repository.CountAsync`. The query fetches one look-ahead row
beyond `page_size`; `_mapper.EachAsync<TEntity, TSummary>` maps the stream to summaries. The extra row sets
`hasMore` and is then
removed; `next_page_token` is sealed only when `hasMore`, so an exactly-full last page omits it per AIP-158.
When `request.ShowDeleted` is true, the count and row fetch run inside `_repository.SuppressQuerySoftDelete()`
so tombstoned rows are included.

### 7. List response — `IResourceListResponseAdvisor<TSummary>`

Receives the immutable summary array and the principal. `AdviceListResponseParent` derives
`IChild.Parent` on each summary. Schemata returns full summaries and provides no AIP-157 partial-response
selection stage or response trimming in this chain.

## Get

`GetAsync` accepts a name string or a `GetRequest` and returns a `GetResultBase<TDetail>`.

### 1. Name predicates

`ResourceIdentifiers.Apply` resolves the name (`request.CanonicalName ?? request.Name`) into leaf and parent
`Where` predicates on the container.

### 2. Get request — `IResourceGetRequestAdvisor<TEntity>`

Receives the `GetRequest`, the container, and the principal. Authorization advisors run here when configured.

### 3. Entity load

Get loads the entity inside `_repository.SuppressQuerySoftDelete()`, so Get returns
tombstoned rows and the caller can inspect `DeleteTime`. A null result throws `ResourceNotFound(name)` carrying a
`ResourceInfoDetail`.

### 4. Response — `IResourceResponseAdvisor<TEntity, TDetail>`

The entity is mapped to `TDetail`, then the response chain runs: `AdviceResponseParent` derives
`IChild.Parent`, `AdviceResponseFreshness` sets the ETag, `AdviceResponseIdempotency` is a no-op for
reads. The response chain maps the full detail and has no partial-response selection or trimming stage.

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` to add predicates via `container.ApplyWhere`
  (entitlement, tenant scoping).
- Implement `IResourceGetRequestAdvisor<TEntity>` for per-get logic, such as additional
  `container.ApplyWhere` predicates evaluated during the entity load.
- Implement `IResourceListResponseAdvisor<TSummary>` or `IResourceResponseAdvisor<TEntity, TDetail>` to
  post-process responses.

## Design rationale

Get always suppresses soft-delete filtering so a caller can read and inspect a tombstoned resource; List
suppresses it only when `ShowDeleted` is true. The look-ahead row makes `next_page_token` exact without relying
on `total_size`, which is optional under `TotalSizeMode.None`.

## Caveats

- List resolves the filter language from the resource expression profile; enable languages on
  `SchemataResourceBuilder` before exposing filterable endpoints.
- `CountAsync` runs a separate query before paging. On large tables, use `TotalSizeMode.Estimated` or `None`.

## See also

- [Resource Overview](overview.md)
- [Filtering](filtering.md)
- [Resource Naming](resource-naming.md)
