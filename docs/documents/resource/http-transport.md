# HTTP Transport

The HTTP transport exposes resources as REST endpoints through MVC controllers synthesized per resource at
startup. `MapHttp()` on `SchemataResourceBuilder` adds `SchemataHttpResourceFeature`, which builds a
`ResourceController<TEntity, TRequest, TDetail, TSummary>` for each registered resource.

## Where the code lives

| Package                   | Key files                                                                                                            |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Resource.Http`  | `Features/SchemataHttpResourceFeature.cs`, `Extensions/SchemataResourceBuilderExtensions.cs`                         |
| `Schemata.Resource.Http`  | `ResourceController.cs`, `ResourceControllerConvention.cs`, `ResourceControllerFeatureProvider.cs`                   |
| `Schemata.Resource.Http`  | `ResourceMethodController.cs`, `ResourceMethodControllerConvention.cs`, `ResourceMethodControllerFeatureProvider.cs` |
| `Schemata.Resource.Http`  | `Internal/ResourceHttpConventionHelper.cs`                                                                           |
| `Schemata.Transport.Http` | `Features/SchemataTransportHttpFeature.cs`, `SchemataJsonTraits.cs`                                                  |

## Activation

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Student>();
```

`MapHttp()` adds `SchemataHttpResourceFeature` (`DefaultPriority = SchemataResourceFeature.DefaultPriority +
100_000`) and returns the same `SchemataResourceBuilder`, so registrations and transports chain without an
intermediate builder. A plain `Use<...>()` exposes the resource on every active transport; to restrict it to
HTTP, pass a selector — `Use<Student>(r => r.MapHttp())` — which tags the resource with
`HttpResourceAttribute.Name` (`"HTTP"`) so the feature provider synthesizes a controller for it.

`SchemataHttpResourceFeature` declares `[DependsOn]` on `SchemataResourceFeature` and `SchemataTransportHttpFeature`.
The transport feature (`DefaultPriority = Orders.Extension + 10_000_000`) installs the exception handler and the
JSON wire-name traits. `ConfigureServices` registers `ResourceControllerFeatureProvider` and
`ResourceMethodControllerFeatureProvider`, and adds `ResourceControllerConvention` and
`ResourceMethodControllerConvention` to `MvcOptions.Conventions`. `ConfigureApplication` assigns the discovered
resources and methods to the providers and calls `Commit()` so MVC rebuilds its action-descriptor cache before
the first request.

## Controller synthesis

`ResourceControllerFeatureProvider.PopulateFeature` iterates the registered resources and, for each whose
`Endpoints` includes `HttpResourceAttribute.Name` (or is `null`, meaning all transports), adds a closed
controller type:

```csharp
typeof(ResourceController<,,,>).MakeGenericType(resource.Entity, resource.Request, resource.Detail, resource.Summary);
```

`Commit()` cancels and replaces a `CancellationTokenSource`, firing the `IActionDescriptorChangeProvider` change
token so MVC refreshes its route table. Adding the closed types directly to `ControllerFeature` is how they escape
the `Schemata.*` application-part stripping done by `SchemataControllersFeature`.

## Routing and method mapping

`ResourceControllerConvention` rewrites only `ResourceController<,,,>` instances. For each it sets
`ControllerName` to the descriptor's `Plural`, builds the route from `ResourceHttpConventionHelper.BuildControllerRoute`
— `~/v1/{package}/{collectionPath}` when a package is set, otherwise `~/v1/{collectionPath}` — and drops actions
for verbs excluded by `ResourceAttribute.Operations`.

| Method   | Route                         | Action                                                                                                      | AIP     |
| -------- | ----------------------------- | ----------------------------------------------------------------------------------------------------------- | ------- |
| `GET`    | `/v1/{collectionPath}`        | `ListAsync([FromQuery] ListRequest)`                                                                        | AIP-132 |
| `POST`   | `/v1/{collectionPath}`        | `CreateAsync([FromBody] TRequest)` → `201 Created`                                                          | AIP-133 |
| `GET`    | `/v1/{collectionPath}/{name}` | `GetAsync(string name, [FromQuery] string? readMask)`                                                       | AIP-131 |
| `PATCH`  | `/v1/{collectionPath}/{name}` | `UpdateAsync(string name, [FromBody] TRequest)`                                                             | AIP-134 |
| `DELETE` | `/v1/{collectionPath}/{name}` | `DeleteAsync(string name, [FromQuery] string? etag, [FromQuery(Name = "allow_missing")] bool allowMissing)` | AIP-135 |

For a flat resource `CollectionPath` is the plural (`students`), so the routes are `/v1/students` and
`/v1/students/{name}`. For a nested pattern such as `publishers/{publisher}/books/{book}` the route is
`/v1/publishers/{publisher}/books` and `{publisher}` becomes a route parameter.

Each action passes `HttpContext.User` and `HttpContext.RequestAborted` to the handler. `ListAsync` fills
`request.Parent` from route values; `CreateAsync` calls `SetParentFromRouteValues`; `UpdateAsync` reads the ETag
from `request.EntityTag`, then the `etag` query parameter, then the `If-Match` header; `DeleteAsync` reads the
ETag from the `etag` query parameter, then `If-Match`. A soft delete returns `200` with the detail; a hard delete
returns `204`.

### Custom methods

`ResourceMethodControllerFeatureProvider` synthesizes one closed
`ResourceMethodController<TEntity, TRequest, TResponse, THandler>` per declared method.
`ResourceMethodControllerConvention` sets each action's absolute route template:

| Scope        | Route                                           |
| ------------ | ----------------------------------------------- |
| `Instance`   | `~/v1/{package}/{collectionPath}/{name}:{verb}` |
| `Collection` | `~/v1/{package}/{collectionPath}:{verb}`        |

Methods are `POST` by default; a `ResourceMethodAttribute.Method` of `ResourceHttpMethod.Get` rebinds the request
from the body to the query string and constrains the verb to `GET`. The verb is carried to runtime as
`ResourceMethodVerbMetadata`, and the controller dispatches through `ResourceMethodOperationHandler`. See
[Custom Methods](custom-methods.md).

## Request and response wire format

`SchemataJsonTraits.Apply` is applied to `JsonSerializerOptions`, `Microsoft.AspNetCore.Http.Json.JsonOptions`,
and `Microsoft.AspNetCore.Mvc.JsonOptions` by `SchemataTransportHttpFeature`. It adds a type-info modifier that
runs each property through `ResourceWireNameRules.Resolve`: `ICanonicalName.Name` is dropped, `CanonicalName`
serializes as `name`, `IFreshness.EntityTag` as `etag`, and `IEntitiesResult<T>.Entities` as the resource plural
(e.g. `students`). The configured `PropertyNamingPolicy` (snake_case) then applies to the remaining names.
`ResourceController` serializes through MVC's `JsonResult`, which picks up these options.

## Error mapping

`SchemataTransportHttpFeature.ConfigureApplication` registers `app.UseExceptionHandler`. The handler reads
`IExceptionHandlerPathFeature.Error`; a non-`SchemataException` is wrapped as a 500
`SchemataException(ErrorCodes.Internal)`. It sets `Response.StatusCode = ex.Code`, content type
`application/json`, and writes the body from `ex.CreateErrorResponse(context.TraceIdentifier)`. A handler that
returns `Block` without throwing surfaces as `NotFoundException` (404), hiding the resource's existence per
AIP-211.

## Reflection and metadata

The MVC route table itself is the HTTP surface description; there is no separate reflection endpoint. Hand-written
controllers are honored: when a controller with the resource's `Plural` name already exists, the feature provider
skips synthesizing the generated one.

## Extension points

- Subclass `ResourceController<TEntity, TRequest, TDetail, TSummary>` and override actions; register the subclass
  as a normal MVC controller.
- Add an `IControllerModelConvention` to `MvcOptions.Conventions` to customize templates or filters.
- `[RateLimitPolicy("my-policy")]` on the entity adds `EnableRateLimitingAttribute` to every endpoint.

## Caveats

- The HTTP transport is unary. For streaming, use gRPC.
- `WithAuthorization(scheme)` adds an always-pass `AuthorizeFilter` so the authentication middleware runs for the
  scheme; the actual authorization decision happens in the advisor pipeline.

## See also

- [Resource Overview](overview.md)
- [gRPC Transport](grpc-transport.md)
- [Custom Methods](custom-methods.md)
- [Resource Naming](resource-naming.md)
