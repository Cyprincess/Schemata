# Resource Overview

`Schemata.Resource.Foundation.ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>` runs the five
standard CRUD operations — List, Get, Create, Update, Delete — for one resource. Each operation executes a
fixed sequence of advisor stages: a gate check, an operation-specific request chain, an optional entity-level
chain, persistence, and a response chain. The handler holds no `HttpContext`; the HTTP and gRPC transports
both call it, passing a `ClaimsPrincipal?` pulled from their own request context.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` + `.Create.cs`, `.Get.cs`, `.List.cs`, `.Update.cs`, `.Delete.cs` |
| `Schemata.Resource.Foundation` | `SchemataResourceBuilder.cs`, `ResourceRequestContainer.cs`, `ResourceMethodOperationHandler.cs` |
| `Schemata.Resource.Foundation` | `Features/SchemataResourceFeature.cs`, `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Abstractions` | `Entities/ICanonicalName.cs`, `Entities/CanonicalNameAttribute.cs`, `Entities/Operations.cs` |
| `Schemata.Abstractions` | `Resource/ResourceAttribute.cs`, `Resource/CreateResultBase.cs` (and the other `*ResultBase`) |

## The four type parameters

A resource is defined by four types, each constrained to `class, ICanonicalName`:

| Parameter | Role |
| --- | --- |
| `TEntity` | The persistent entity stored in the repository |
| `TRequest` | The DTO accepted on Create and Update |
| `TDetail` | The DTO returned from Get, Create, and Update |
| `TSummary` | The DTO returned from each List item |

`ICanonicalName` requires `string? Name` and `string? CanonicalName`. A resource is addressed externally by its
AIP-122 canonical name; internal identity (`Uid`) stays in the persistence layer.

### Collapsing type parameters

Registering with fewer than four types fills the trailing slots from the last supplied type. The
`SchemataResourceBuilderExtensions` overloads (and their HTTP/gRPC equivalents) delegate to the four-parameter
`SchemataResourceBuilder.Use<TEntity, TRequest, TDetail, TSummary>`:

```csharp
.Use<Student>()                                   // Student, Student, Student, Student
.Use<Student, StudentRequest>()                   // Student, StudentRequest, StudentRequest, StudentRequest
.Use<Student, StudentRequest, StudentDetail>()    // Student, StudentRequest, StudentDetail, StudentDetail
.Use<Student, StudentRequest, StudentDetail, StudentSummary>()
```

The declarative `[Resource]` attribute mirrors this: `ResourceAttribute(entity, request = null, detail = null,
summary = null)` defaults `request`/`detail` to `entity` and `summary` to `detail`. The generic
`[Resource<TEntity>]` expands to `[Resource<TEntity, TEntity, TEntity, TEntity>]`.

## Enabling the resource system

`SchemataBuilder.UseResource()` adds `SchemataResourceFeature` and returns a `SchemataResourceBuilder`:

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

`SchemataResourceFeature.DefaultPriority` is `Orders.Extension + 90_000_000` (490M). It declares `[DependsOn]`
on `SchemataRoutingFeature`, `Schemata.Mapping.Foundation.Features.SchemataMappingFeature\`1`, and
`Schemata.Security.Foundation.Features.SchemataSecurityFeature`, so those features auto-register.

`ConfigureServices` registers the open-generic `ResourceOperationHandler<,,,>` and
`ResourceMethodOperationHandler<,,>` as scoped, calls `services.AddAipExpressions()`, adds the HTTP context
accessor and Data Protection, and registers the built-in advisor lanes.

## Registering a resource

A resource is registered two ways, both converging on `SchemataResourceFeature.RegisterResource`.

**Imperative** — activate a transport, then call `Use<...>()`:

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

`MapHttp()` / `MapGrpc()` return the same `SchemataResourceBuilder`, so registrations and transports
chain freely. To restrict one resource to specific transports, pass a selector:

```csharp
schema.UseResource()
      .MapHttp().MapGrpc()                      // activate both transports
      .Use<Student>(r => r.MapHttp())           // Student over HTTP only
      .Use<Course>();                           // Course over every active transport
```

**Declarative** — annotate the entity with `[Resource]`. `ConfigureServices` scans every non-dynamic, non-`Schemata.*`
assembly in `AppDomain.CurrentDomain.GetAssemblies()` and registers each type carrying a `ResourceAttribute`. The
same scan reads `[ResourceMethod]` attributes and stores them in `SchemataResourceOptions.Methods` keyed by
`entity.TypeHandle`.

`Use<...>(endpoints, configure)` also accepts an `Action<ResourceAttribute>` so a caller can set
`Operations`, `Endpoints`, or `Methods` without entity attributes.

`RegisterResource` keys the `ResourceAttribute` on `entity.TypeHandle`, registers per-entity Create/Update
idempotency advisors, and — for `ISoftDelete` entities — adds the built-in `undelete`, `expunge`, and `purge`
methods (each skipped when the `Operations` whitelist excludes it or the entity already declares that verb).

## Handler stages

Every operation runs the same fixed stage sequence. The stage order is hard-coded in the handler; advisor
`Order` only sequences advisors within one stage.

```
IResourceRequestAdvisor<TEntity>             gate (all operations; second arg is the operation token)
  IResource{Create|Get|List|Update|Delete}RequestAdvisor   operation-specific request chain
    mapper.Map<TRequest, TEntity>            Create only
      IResource{Create|Update|Delete}Advisor entity-level chain (Create/Update/Delete)
        repository.AddAsync / UpdateAsync / RemoveAsync, then CommitAsync
          mapper.Map<TEntity, TDetail>
            IResourceResponseAdvisor<TEntity, TDetail>   response chain
```

The gate's second argument is a `string`: `nameof(Operations.List | Get | Create | Update | Delete)` for CRUD,
or the lowerCamelCase verb for an AIP-136 custom method. Each stage runs through
`ResourcePipelineRunner<Operations>.RunAsync`, which interprets the `AdviseResult`:

- `Continue` — proceed to the next stage.
- `Block` — throw the stage's blocked exception. For CRUD that is `NotFoundException`, hiding the resource's
  existence per AIP-211.
- `Handle` — return a result the advisor stashed in `AdviceContext`, or the handler's fallback (`() => new()`
  for Delete).

Custom methods run through `ResourceMethodOperationHandler<TEntity, TRequest, TResponse>`, which mirrors the
sequence with verb-scoped advisor sockets. See [Custom Methods](custom-methods.md).

## Operation results

Each operation returns a thin result base carrying the response DTO:

| Operation | Result type | Members |
| --- | --- | --- |
| Create | `CreateResultBase<TDetail>` | `TDetail? Detail` |
| Get | `GetResultBase<TDetail>` | `TDetail? Detail` |
| Update | `UpdateResultBase<TDetail>` | `TDetail? Detail` |
| Delete | `DeleteResultBase<TDetail>` | `TDetail? Detail` (set only for a soft delete) |
| List | `ListResultBase<TSummary>` | `IList<TSummary>? Entities`, `int? TotalSize`, `string? NextPageToken` |

`ListResultBase<TSummary>` implements `IEntitiesResult<TSummary>`, which drives the plural wire-name rename of
`Entities` (see [HTTP Transport](http-transport.md)).

## Built-in advisor lanes

`SchemataResourceFeature.ConfigureServices` registers these advisors for every resource:

| Advisor | Stage |
| --- | --- |
| `AdviceCreateRequestSanitize<TEntity, TRequest>` | Create request |
| `AdviceCreateRequestValidation<TEntity, TRequest>` | Create request |
| `AdviceUpdateRequestSanitize<TEntity, TRequest>` | Update request |
| `AdviceUpdateRequestValidation<TEntity, TRequest>` | Update request |
| `AdviceApplyChildParent<TEntity, TRequest>` | Create / Update entity |
| `AdviceUpdateSoftDeleted<TEntity, TRequest>` | Update entity |
| `AdviceUpdateFreshness<TEntity, TRequest>` | Update entity |
| `AdviceDeleteFreshness<TEntity>` | Delete entity |
| `AdviceFillChildParentResponse<TEntity, TDetail>` | Response |
| `AdviceResponseFreshness<TEntity, TDetail>` | Response |
| `AdviceResponseReadMask<TEntity, TDetail>` | Response |
| `AdviceFillChildParentListResponse<TSummary>` | List response |
| `AdviceListResponseReadMask<TSummary>` | List response |
| `AdviceResponseIdempotency<TEntity, TDetail>` | Response |

`RegisterResource` adds the per-entity Create/Update idempotency advisors
(`AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail>`,
`AdviceUpdateRequestIdempotency<TEntity, TRequest, TDetail>`). Authorization advisors
(`AdviceXxxRequestAnonymous`, `AdviceXxxRequestAuthorize`) are added only when `WithAuthorization()` is called.

## `SchemataResourceBuilder`

`UseResource()` returns a `SchemataResourceBuilder` with these methods:

| Method | Effect |
| --- | --- |
| `WithAuthorization(scheme?)` | Registers anonymous + authorize advisors for all operations; sets `AuthenticationScheme` when `scheme` is given |
| `WithoutCreateValidation()` | Sets `SchemataResourceOptions.SuppressCreateValidation = true` |
| `WithoutUpdateValidation()` | Sets `SchemataResourceOptions.SuppressUpdateValidation = true` |
| `WithoutFreshness()` | Sets `SchemataResourceOptions.SuppressFreshness = true` |
| `Use<TEntity, TRequest, TDetail, TSummary>(endpoints?)` | Registers a resource imperatively |
| `Use<TEntity...>(Action<ResourceEndpointSelector>)` | Registers a resource restricted to the selected transports |
| `MapHttp()` | Adds `SchemataHttpResourceFeature`, returns the same `SchemataResourceBuilder` |
| `MapGrpc()` | Adds `SchemataGrpcResourceFeature`, returns the same `SchemataResourceBuilder` |

## Extension points

- Implement `IResource{Create|Get|List|Update|Delete}RequestAdvisor<...>` for per-operation request hooks
  (authorization, sanitization, validation, idempotency).
- Implement `IResource{Create|Update|Delete}Advisor<...>` for entity-stage logic that runs after mapping and
  before persistence.
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process the response DTO (freshness, mask,
  idempotency cache).
- Implement `IResourceMethodHandler<TEntity, TRequest, TResponse>` for AIP-136 custom verbs; see
  [Custom Methods](custom-methods.md).
- Register the advisors as scoped through `services.TryAddEnumerable(ServiceDescriptor.Scoped(...))`. Pick an
  `Order` outside the reserved `[100_000_000, 900_000_000]` window.

## Design rationale

The four type parameters separate the persistent shape (`TEntity`), the write surface (`TRequest`), the rich
read surface (`TDetail`), and the list-optimized surface (`TSummary`). Each surface is its own type, so the
handler controls per-operation field visibility through mapping. Keeping the handler free of `HttpContext`
lets the foundation layer run under either transport and stay unit-testable without a web host.

## Caveats

- All four type parameters must implement `ICanonicalName`. An entity with a different identity scheme needs
  `Name` and `CanonicalName` properties plus a `[CanonicalName("...")]` pattern.
- `SchemataResourceFeature` is registered through `AddFeature`, which deduplicates by `RuntimeTypeHandle`, so
  calling `UseResource()` twice is safe.
- Assembly-scan discovery reads `AppDomain.CurrentDomain.GetAssemblies()` during `ConfigureServices`. A type in
  an assembly loaded after that point is not discovered.

## See also

- [Create Pipeline](create-pipeline.md)
- [Read Pipeline](read-pipeline.md)
- [Update Pipeline](update-pipeline.md)
- [Delete Pipeline](delete-pipeline.md)
- [Resource Naming](resource-naming.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Custom Methods](custom-methods.md)
