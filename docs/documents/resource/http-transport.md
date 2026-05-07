# HTTP Transport

The HTTP transport exposes resources as REST endpoints via dynamically generated MVC controllers. It is activated by calling `MapHttp()` on the resource builder.

## Setup

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .MapHttp()
          .Use<Book, BookRequest, BookDetail, BookSummary>();
});
```

`MapHttp()` adds the `SchemataHttpResourceFeature`, which depends on:

- `SchemataControllersFeature` -- MVC controller infrastructure.
- `SchemataJsonSerializerFeature` -- JSON serialization configuration.
- `SchemataResourceFeature` -- the core resource system.

## ResourceController

`ResourceController<TEntity, TRequest, TDetail, TSummary>` is a generic `ControllerBase` subclass decorated with `[ApiController]`. One controller instance is created per registered resource type.

### Endpoint Mapping

| HTTP Method | Route                        | Action        | Request Source                                        | Response                                       |
| ----------- | ---------------------------- | ------------- | ----------------------------------------------------- | ---------------------------------------------- |
| `GET`       | `/{collection}`              | `ListAsync`   | `[FromQuery] ListRequest`                             | JSON `ListResult<TSummary>`                    |
| `GET`       | `/{name={collection}/*}`     | `GetAsync`    | Route `name`                                          | JSON `TDetail`                                 |
| `POST`      | `/{collection}`              | `CreateAsync` | `[FromBody] TRequest`                                 | 201 Created, JSON `TDetail`, `Location` header |
| `PATCH`     | `/{name={collection}/*}`     | `UpdateAsync` | Route `name`, `[FromBody] TRequest`                   | JSON `TDetail`                                 |
| `DELETE`    | `/{name={collection}/*}`     | `DeleteAsync` | Route `name`, `[FromQuery] etag`, `[FromQuery] force` | 204 No Content                                 |

The `{name=students/*}` syntax follows [AIP-127](https://google.aip.dev/127): the variable captures the entire resource name (e.g. `students/a1b2c3d4`), not just the ID component. The `*` wildcard matches all URI-safe characters except `/`.

For hierarchical resources with a `[CanonicalName]` pattern, the parent segments appear in the `{parent=.../*}` variable for collection endpoints and in the `{name=.../*/.../*}` variable for item endpoints. For example, `[CanonicalName("publishers/{publisher}/books/{book}")]` produces:

| Method   | Route                                     |
| -------- | ----------------------------------------- |
| `GET`    | `/{parent=publishers/*}/books`            |
| `GET`    | `/{name=publishers/*/books/*}`            |
| `POST`   | `/{parent=publishers/*}/books`            |
| `PATCH`  | `/{name=publishers/*/books/*}`            |
| `DELETE` | `/{name=publishers/*/books/*}`            |

When `[ResourcePackage("api/v1")]` is applied, the package prefixes the route: `GET /api/v1/{parent=publishers/*}/books`.

### List Endpoint Details

The `ListAsync` action binds all `ListRequest` parameters from the query string:

- `?filter=status="active"` -- AIP-160 filter expression.
- `?orderBy=create_time desc` -- ordering clause.
- `?pageSize=10` -- items per page.
- `?skip=5` -- items to skip.
- `?pageToken=...` -- continuation token.
- `?showDeleted=true` -- include soft-deleted resources.
- `?parent=publishers/acme` -- explicit parent (auto-resolved from the `{parent=publishers/*}` route variable if not provided).

If the `Parent` parameter is not explicitly provided, the controller resolves it automatically from HTTP route values using `ResourceNameDescriptor.ResolveParent`.

When the operation is blocked, an empty result is returned. Otherwise, a `JsonResult` with the `ListResult<TSummary>` is returned.

### Update Endpoint Details

The `UpdateAsync` action reads the ETag for freshness validation from multiple sources, in order:

1. The `IFreshness.EntityTag` property on the request body.
2. The `etag` query parameter.
3. The `If-Match` HTTP header.

The first non-empty value is used.

### Delete Endpoint Details

The `DeleteAsync` action accepts:

- `name` from the route.
- `etag` as an optional query parameter. Falls back to the `If-Match` header if not provided.
- `force` as an optional boolean query parameter (defaults to `false`).

## Dynamic Controller Registration

`ResourceControllerFeatureProvider` implements both `IApplicationFeatureProvider<ControllerFeature>` and `IActionDescriptorChangeProvider`. During startup, it:

1. Iterates all registered resources from `SchemataResourceOptions`.
2. Skips resources whose `Endpoints` list does not include `"HTTP"`.
3. Skips resources that already have a manually-created controller (matched by plural name or `{Entity}Controller` name).
4. Constructs a closed generic `ResourceController<TEntity, TRequest, TDetail, TSummary>` and adds it to the MVC controller feature.

The `Commit()` method signals MVC to refresh action descriptors via a `CancellationChangeToken`, which is called during `ConfigureApplication`.

## Route Convention

`ResourceControllerConvention` is an `IControllerModelConvention` that customizes routing for generated controllers:

1. Sets `ControllerName` to the pluralized entity name (e.g., `Books`).
2. Replaces the route template with the `CollectionPath` from `ResourceNameDescriptor`.
3. If `[ResourcePackage]` is present, prepends the package prefix.
4. If `[RateLimitPolicy]` is present on the entity type, adds an `EnableRateLimitingAttribute` to endpoint metadata.

## JSON Serialization

`ResourceJsonOptions` creates a customized `JsonSerializerOptions` instance with these modifications:

1. The `Name` property on types implementing `ICanonicalName` is populated with the full qualified resource name (e.g. `"students/a1b2c3d4"`). HTTP clients use this value directly as the path segment in subsequent `GET`, `PATCH`, and `DELETE` calls.

2. **Collection name in ListResult.** The `Entities` property in `ListResult<TSummary>` is renamed to the pluralized entity name (per AIP-132). The pluralized name is obtained from a `ResourceNameDescriptor` for the entity type and is then run through the active `PropertyNamingPolicy` (normally `SnakeCaseLower`). For example, `ListResult<BookSummary>` serializes the collection as `"books"` instead of `"entities"`.

3. **EntityTag renaming.** The `IFreshness.EntityTag` property is serialized as `"etag"`. Under the default snake_case naming policy the C# property would serialize as `entity_tag`; this modifier overrides that to produce the conventional `etag` field name.

The gRPC transport applies equivalent but different mappings -- see [gRPC Transport § Comparison with HTTP](grpc-transport.md#comparison-with-http) for a side-by-side table.

## AnonymousAttribute

`[Anonymous]` marks a resource as allowing unauthenticated access:

```csharp
[Anonymous]                           // All operations anonymous
[Anonymous(Operations.List, Operations.Get)]  // Only read operations anonymous
public class PublicBook : ICanonicalName { ... }
```

When no operations are specified, all operations are anonymous. When specific operations are listed, only those operations skip authorization.

The authorization advisors check this attribute via `AnonymousAccessHelper.IsAnonymous<TEntity>(operation)` and skip the `IAccessProvider` call when the operation is anonymous.

## RateLimitPolicyAttribute

`[RateLimitPolicy]` associates a named rate-limiting policy with a resource:

```csharp
[RateLimitPolicy("api-standard")]
public class Book : ICanonicalName { ... }
```

`ResourceControllerConvention` reads this attribute and adds an `EnableRateLimitingAttribute` to every endpoint of the controller. The policy name must correspond to a policy registered with ASP.NET Core's rate limiting middleware.

The same attribute is also respected by the [gRPC transport](./grpc-transport.md), which calls `RequireRateLimiting` on the gRPC endpoint convention builder.

## Restricting to HTTP Only

To register a resource for HTTP only (not gRPC), use the `MapHttp()` builder chain:

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Book, BookRequest, BookDetail, BookSummary>();
```

This passes `["HTTP"]` as the `Endpoints` list, so `SchemataGrpcResourceFeature` skips this resource. Alternatively, apply `[HttpResource]` on the entity class for attribute-based registration.
