# HTTP Transport

The HTTP transport exposes resources as REST endpoints via dynamically generated MVC controllers. Calling `MapHttp()` on `SchemataResourceBuilder` registers `SchemataHttpResourceFeature` (Priority `SchemataResourceFeature.DefaultPriority + 100_000` = 490,100,000), which synthesizes a `ResourceController<TEntity, TRequest, TDetail, TSummary>` instance for each registered resource at application startup.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Http` | `SchemataHttpResourceBuilder.cs` |
| `Schemata.Resource.Http` | `Features/SchemataHttpResourceFeature.cs` |
| `Schemata.Resource.Http` | `Extensions/SchemataResourceBuilderExtensions.cs` |
| `Schemata.Resource.Http` | `Extensions/SchemataHttpResourceBuilderExtensions.cs` |
| `Schemata.Resource.Http` | `ResourceController.cs` |
| `Schemata.Resource.Http` | `ResourceControllerConvention.cs` |
| `Schemata.Resource.Http` | `ResourceControllerFeatureProvider.cs` |
| `Schemata.Resource.Http` | `ResourceMethodController.cs` |
| `Schemata.Resource.Http` | `ResourceMethodControllerConvention.cs` |
| `Schemata.Resource.Http` | `ResourceMethodControllerFeatureProvider.cs` |

## Setup

```csharp
builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseControllers();
    schema.UseResource()
          .MapHttp()
          .Use<Student>();
});
```

`MapHttp()` is an extension on `SchemataResourceBuilder` that adds `SchemataHttpResourceFeature` and returns a `SchemataHttpResourceBuilder`. Subsequent `Use<...>()` calls on the HTTP builder register resources tagged with `HttpResourceAttribute.Name` so the HTTP feature provider knows to synthesize controllers for them.

## `SchemataHttpResourceFeature`

`SchemataHttpResourceFeature` depends on `SchemataResourceFeature` and `SchemataTransportHttpFeature` via `[DependsOn<T>]`. The transport feature carries the MVC exception handler, the JSON wire-name traits (`SchemataJsonTraits`), and `SchemataControllersFeature` (transitively); the resource HTTP feature focuses on dynamic controller synthesis on top of it.

`ConfigureServices` registers:

- `ResourceControllerFeatureProvider` as a singleton `IApplicationFeatureProvider<ControllerFeature>` and `IActionDescriptorChangeProvider`.
- `ResourceControllerConvention` via `MvcOptions.Conventions`, passing the configured authentication scheme.

`ConfigureApplication` reads `SchemataResourceOptions.Resources`, assigns it to `ResourceControllerFeatureProvider.Resources`, and calls `provider.Commit()` to signal MVC that the controller set has changed. MVC rebuilds its action descriptor cache before the first request is served.

## Controller synthesis

`ResourceControllerFeatureProvider.PopulateFeature` iterates `Resources` and, for each resource whose `Endpoints` list includes `HttpResourceAttribute.Name` (or is `null`, meaning all transports), calls:

```csharp
var controller = typeof(ResourceController<,,,>)
    .MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!)
    .GetTypeInfo();
feature.Controllers.Add(controller);
```

A controller is skipped if a controller with the same name (the resource's `Plural` form) or the entity's `{Name}Controller` already exists in the feature — this lets you provide a hand-written controller that overrides the generated one.

`Commit()` cancels the current `CancellationTokenSource` and creates a new one, which triggers `IActionDescriptorChangeProvider.GetChangeToken()` to fire and causes MVC to refresh its route table.

## Route conventions

`ResourceControllerConvention` implements `IControllerModelConvention` and rewrites routes only for `ResourceController<,,,>` instances. For each such controller:

1. `controller.ControllerName` is set to `descriptor.Plural` (e.g., `"Students"`).
2. The route template is set to `~/{package}/{collectionPath}` when a package is configured, or `~/{collectionPath}` otherwise. A `Book` entity with pattern `"publishers/{publisher}/books/{book}"` and package `"library"` gets route `~/library/publishers/{publisher}/books`.
3. If the entity has `[RateLimitPolicy]`, `EnableRateLimitingAttribute` is added to the controller's selectors.
4. If an authentication scheme is configured via `WithAuthorization(scheme)`, an `AuthorizeFilter` with an always-pass assertion policy is added. This forces ASP.NET Core to run the authentication middleware for the scheme before the controller action executes; actual authorization decisions happen in the advisor pipeline.

## `ResourceController<TEntity, TRequest, TDetail, TSummary>`

The generated controller exposes five actions:

| HTTP method | Route | Action | Returns |
|---|---|---|---|
| `GET` | `/{collection}` | `ListAsync([FromQuery] ListRequest)` | `JsonResult(ListResultBase<TSummary>)` |
| `GET` | `/{collection}/{name}` | `GetAsync(string name, [FromQuery] string? readMask)` | `JsonResult(TDetail)` |
| `POST` | `/{collection}` | `CreateAsync([FromBody] TRequest)` | `JsonResult(TDetail)` with `201 Created` |
| `PATCH` | `/{collection}/{name}` | `UpdateAsync(string name, [FromBody] TRequest)` | `JsonResult(TDetail)` |
| `DELETE` | `/{collection}/{name}` | `DeleteAsync(string name, [FromQuery] string? etag)` | `200` with the updated resource for soft deletes, `204 No Content` for hard deletes |

All actions delegate to `ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>`, passing `HttpContext.User` as the principal and `HttpContext.RequestAborted` as the cancellation token.

`ListAsync` auto-populates `request.Parent` from route values so nested resources (`/publishers/{publisher}/books`) are scoped to the parent without the client supplying it.

`CreateAsync` calls `ResourceNameDescriptor.ForType<TEntity>().SetParentFromRouteValues(request, routeValues)` to populate parent properties on the request from the route.

`UpdateAsync` reads the ETag from `request.EntityTag`, then falls back to the `etag` query parameter, then to the `If-Match` request header.

`DeleteAsync` reads the ETag from the `etag` query parameter, then falls back to the `If-Match` header.

## Custom methods

Each `[ResourceMethod(verb, handler, scope)]` on the entity yields one synthesized closed `ResourceMethodController<TEntity, TRequest, TResponse, THandler>` through `ResourceMethodControllerFeatureProvider`. `ResourceMethodControllerConvention` rewrites its route to `~/v1/{package}/{collectionPath}` and the action template to `{name}:{verb}` for `ResourceMethodScope.Instance` or `:{verb}` for `ResourceMethodScope.Collection`. All custom methods are `POST` and dispatch through `ResourceMethodOperationHandler<TEntity, TRequest, TResponse>` before reaching the registered `IResourceMethodHandler<TEntity, TRequest, TResponse>`.

| Scope | HTTP method | Route |
|---|---|---|
| `Instance` | `POST` | `~/v1/{package}/{collectionPath}/{name}:{verb}` |
| `Collection` | `POST` | `~/v1/{package}/{collectionPath}:{verb}` |

The `{package}/` segment is dropped when the entity has no `[ResourcePackage]`. `[RateLimitPolicy]` and `WithAuthorization(scheme)` propagate to custom-method controllers identically to the CRUD controller.

See [Custom Methods](custom-methods.md) for the verb-scoped advisor pipeline.

## JSON serialization

The wire-format conventions (snake_case property naming, `long`-as-string, AIP `@type` discriminator) live in `Schemata.Transport.Http`'s `SchemataJsonTraits` and are applied to `JsonSerializerOptions`, `Microsoft.AspNetCore.Http.Json.JsonOptions`, and `Microsoft.AspNetCore.Mvc.JsonOptions` by `SchemataTransportHttpFeature`. `ResourceController` and `ResourceMethodController` both serialize responses through MVC's `JsonResult`, which picks up the configured options.

## Extension points

- Subclass `ResourceController<TEntity, TRequest, TDetail, TSummary>` and override individual action methods. Register the subclass as a regular MVC controller; the feature provider skips synthesizing a duplicate when a matching controller name or `{Entity}Controller` already exists.
- Implement `IControllerModelConvention` and add it to `MvcOptions.Conventions` to further customize route templates or filters.
- Use `[RateLimitPolicy("my-policy")]` on the entity type to apply rate limiting to all HTTP endpoints for that resource.

## Caveats

- `ResourceControllerFeatureProvider` adds the synthesized closed-generic controller types directly to `ControllerFeature.Controllers`, which is how they escape the `Schemata.*` assembly-part stripping performed by `SchemataControllersFeature`.
- The HTTP transport is unary. For streaming use cases, use the gRPC transport.
- When an advisor returns `Block` without throwing, the operation handler throws `NotFoundException` (404), hiding the resource's existence per AIP-211. Advisors that want a different HTTP status throw the matching `SchemataException` subtype instead.

## See also

- [Resource Overview](overview.md)
- [Custom Methods](custom-methods.md)
- [gRPC Transport](grpc-transport.md)
- [Resource Naming](resource-naming.md)
- [Filtering](filtering.md)
- [Advice Pipeline](../core/advice-pipeline.md)
