# Read Pipeline

The resource system provides two read operations: **List** and **Get**. Both run through the [advisor pipeline](../core/advice-pipeline.md) and share the `IResourceRequestAdvisor<TEntity>` gate.

## List Operation

`ResourceOperationHandler.ListAsync` accepts a `ListRequest` and returns a `ListResult<TSummary>`.

### ListRequest Parameters

| Parameter     | Type      | Description                                                           |
| ------------- | --------- | --------------------------------------------------------------------- |
| `Parent`      | `string?` | Parent resource name for scoped listings (e.g., `publishers/acme`).   |
| `Filter`      | `string?` | AIP-160 filter expression. See [Filtering](./filtering.md).           |
| `OrderBy`     | `string?` | Comma-separated ordering clause (e.g., `create_time desc`).           |
| `ShowDeleted` | `bool?`   | When `true`, includes soft-deleted entities in results.               |
| `PageSize`    | `int?`    | Maximum items per page. Clamped to 1--100; defaults to 25.            |
| `Skip`        | `int?`    | Number of items to skip (added to the page token's accumulated skip). |
| `PageToken`   | `string?` | Opaque continuation token for the next page.                          |

### Pipeline Steps

#### 1. General Request Advisor

```
IResourceRequestAdvisor<TEntity> -- Operations.List
```

The first gate. Returns `Continue`, `Handle` (with `ListResult<TSummary>` in context), or `Block` (returns `ListResult<TSummary>.Blocked`).

#### 2. List Request Advisor

```
IResourceListRequestAdvisor<TEntity>
```

Receives the `ListRequest`, a `ResourceRequestContainer<TEntity>`, and the `HttpContext`. Advisors can inspect the request and add query modifications to the container.

When `WithAuthorization()` is enabled, `AdviceListRequestAuthorize` runs at order 100,000,000:

1. Checks `[Anonymous(Operations.List)]` on the entity type.
2. Calls `IAccessProvider.HasAccessAsync` -- throws `AuthorizationException` if denied.
3. Calls `IEntitlementProvider.GenerateEntitlementExpressionAsync` and applies the returned predicate to the container. This scopes query results to what the current user is allowed to see.

#### 3. Parent Resolution

If `ListRequest.Parent` is non-empty, the handler parses it against the resource's `CanonicalName` pattern to extract parent placeholder values (e.g., `publishers/acme` yields `{ "publisher": "acme" }`).

If any parent value is the wildcard `"-"`, the handler checks `ResourceNameDescriptor.SupportsReadAcross`. Resources must opt in to cross-parent listing via the `[ReadAcross]` attribute; otherwise an `InvalidArgumentException` is thrown.

Valid parent values produce a `Where` predicate that is applied to the query container, scoping results to that parent.

#### 4. Page Token Handling

If `PageToken` is provided, it is decoded from a Brotli-compressed Base64 URL-safe string back into a `PageToken` object. The token carries the original `Parent`, `Filter`, `OrderBy`, and `ShowDeleted` values. If any of these differ from the current request, an `InvalidArgumentException` is thrown -- page tokens are bound to their originating query.

If no `PageToken` is provided, a fresh `PageToken` is created from the request parameters.

The `PageSize` is clamped: values <= 0 become 25, values > 100 become 100.

`Skip` from the request is added to the token's accumulated skip offset (which starts at 0 and grows by `PageSize` on each page).

#### 5. Filter Parsing

If `Filter` is non-empty, it is parsed using the AIP-160 grammar parser. A `ParseException` results in an `InvalidArgumentException` with field `"filter"`. The parsed filter is applied to the container via `ApplyFiltering`. See [Filtering](./filtering.md) for grammar details.

#### 6. Order Parsing

If `OrderBy` is non-empty, it is parsed using the order grammar (`member [ASC|DESC]`, comma-separated). A `ParseException` results in an `InvalidArgumentException` with field `"order_by"`. Parsed orderings are applied via `ApplyOrdering`.

#### 7. Soft Delete Handling

When `ShowDeleted` is `true`, the repository is configured with `SuppressQuerySoftDelete()`, which disables the automatic soft-delete filter so deleted entities appear in results.

#### 8. Query Execution

1. A `COUNT` query runs first to determine `TotalSize`.
2. Pagination (skip/take) is applied to the container.
3. The repository's `ListAsync` streams entities, which are mapped to `TSummary` via `ISimpleMapper.EachAsync`.

#### 9. Next Page Token

If the result count is greater than or equal to `PageSize`, a next page token is generated. The current token's skip is advanced by `PageSize`, then serialized back to a Brotli-compressed Base64 URL-safe string.

#### 10. List Response Advisor

```
IResourceListResponseAdvisor<TSummary>
```

Receives the immutable array of summaries. Can inspect, filter, or replace the result.

#### 11. Result

A `ListResult<TSummary>` is returned with:

- `Entities` -- the immutable array of summary DTOs.
- `TotalSize` -- total count before pagination.
- `NextPageToken` -- the continuation token, or `null` if this is the last page.

## Get Operation

`ResourceOperationHandler.GetAsync` accepts a `TEntity` (already resolved) and returns a `GetResult<TDetail>`.

The handler provides several entity-resolution methods:

| Method                                   | Lookup Strategy                                                                                                                      |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `GetByNameAsync(name, http, ct)`         | Finds by `Name` property, extracting parent values from HTTP route values.                                                           |
| `GetByNameAsync(name, parentValues, ct)` | Finds by `Name` with explicit parent values dictionary.                                                                              |
| `GetByCanonicalNameAsync(name, ct)`      | Parses a full canonical name (e.g., `publishers/acme/books/les-miserables`), extracts the leaf name and parent values, then queries. |

All of these call `FindByNameAsync` internally, which queries with `SuppressQuerySoftDelete()` so that soft-deleted entities can be found (useful for undelete scenarios). If no entity matches, a `NotFoundException` is thrown with the resource type and name in the error details.

### Pipeline Steps

#### 1. General Request Advisor

```
IResourceRequestAdvisor<TEntity> -- Operations.Get
```

#### 2. Get Request Advisor

```
IResourceGetRequestAdvisor<TEntity>
```

Receives a `GetRequest` with `Name` set to the entity's name.

When `WithAuthorization()` is enabled, `AdviceGetRequestAuthorize` runs and checks `[Anonymous(Operations.Get)]` before calling `IAccessProvider.HasAccessAsync`.

#### 3. Response Mapping and Advisor

The entity is mapped to `TDetail` via `ISimpleMapper.Map<TEntity, TDetail>`.

```
IResourceResponseAdvisor<TEntity, TDetail>
```

`AdviceResponseFreshness` sets the ETag on the detail if applicable.

#### 4. Result

A `GetResult<TDetail>` is returned. In the [HTTP transport](./http-transport.md), the detail is returned directly as a JSON response.

## ResourceRequestContainer

The `ResourceRequestContainer<T>` accumulates query modifications throughout the list pipeline:

- `ApplyFiltering(filter)` -- composes a parsed filter expression into the query.
- `ApplyOrdering(order)` -- composes ordering specifications.
- `ApplyPaginating(token)` -- applies skip/take from the page token.
- `ApplyModification(predicate)` -- adds an arbitrary `Where` predicate (used for parent scoping and entitlement filtering).

The `FilterConfigure` property allows customizing the filter grammar container before expression building, which is how custom functions can be registered for specific resources.
