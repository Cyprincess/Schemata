# Resource Overview

`Schemata.Resource.Foundation.ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>` runs the five
standard CRUD operations — List, Get, Create, Update, Delete — for one resource. Each operation executes a
fixed sequence of advisor stages: a gate check, an operation-specific request chain, an optional entity-level
chain, persistence, and a response chain. The handler holds no `HttpContext`; the HTTP and gRPC transports
reach it only through `IRequestDispatcher.SendAsync`, passing a `ClaimsPrincipal?` pulled from their own
request context — see [Internal command dispatch](#internal-command-dispatch).

## Where the code lives

| Package                        | Key files                                                                                        |
| ------------------------------ | ------------------------------------------------------------------------------------------------ |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` + `.Create.cs`, `.Get.cs`, `.List.cs`, `.Update.cs`, `.Delete.cs`  |
| `Schemata.Resource.Foundation` | `SchemataResourceBuilder.cs`, `ResourceMethodOperationHandler.cs`                                |
| `Schemata.Resource.Foundation` | `Features/SchemataResourceFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`                 |
| `Schemata.Common`              | `ResourceRequestContainer.cs`, `ResourceIdentifiers.cs`, `IPagination.cs` |
| `Schemata.Abstractions`        | `Entities/ICanonicalName.cs`, `Entities/CanonicalNameAttribute.cs`, `Entities/Operations.cs`     |
| `Schemata.Abstractions`        | `Resource/ResourceAttribute.cs`, `Resource/CreateResultBase.cs` (and the other `*ResultBase`)    |

## The four type parameters

A resource is defined by four types, each constrained to `class, ICanonicalName`:

| Parameter  | Role                                           |
| ---------- | ---------------------------------------------- |
| `TEntity`  | The persistent entity stored in the repository |
| `TRequest` | The DTO accepted on Create and Update          |
| `TDetail`  | The DTO returned from Get, Create, and Update  |
| `TSummary` | The DTO returned from each List item           |

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

`SchemataResourceFeature.DefaultPriority` is `Orders.Extension + 100_000_000` (500M).
`[DependsOn<SchemataRoutingFeature>]`, so routing auto-registers. Anonymous-access plumbing
(`AnonymousAccess`, `AnonymousGranted`) lives in `Schemata.Security.Skeleton`, which the resource feature
consumes without pulling in `SchemataSecurityFeature`.

`ConfigureServices` registers the open-generic `ResourceOperationHandler<,,,>` and
`ResourceMethodOperationHandler<,,>` as scoped, adds the HTTP context accessor and Data Protection,
registers the built-in advisor lanes, the `IResourceTypeResolver` singleton, and the open-generic
`PurgeJob<>` together with its `PurgeJobKeyResolver`. Filter languages are opt-in on
`SchemataResourceBuilder` (`UseAip()`, `UseCel()`, `UseOrdering()`); see [Filtering](filtering.md).

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

**Declarative** — annotate the entity with `[Resource]` so the four type roles live on the entity itself, then
register it with `AddResource<TEntity>()`. The attribute carries the type roles; it does not register anything on
its own.

```csharp
schema.UseResource()
      .MapHttp().MapGrpc()
      .AddResource<Course>();                   // Course carries [Resource<Course, CourseRequest, ...>]
```

`[ResourceMethod]` attributes on the entity are read during that call and stored in `IResourceRegistry`,
retrievable through `GetMethods(entityType)`. The registry seals on its first read, so every resource must be
registered while services are being configured.

> **Registration is always explicit.** No assembly is scanned and no type is discovered by decoration alone. A
> `[Resource]`-decorated entity that is never passed to `AddResource<T>()` or `Use<...>()` has no endpoints, and
> `IResourceTypeResolver` cannot resolve it from a resource name. If an upgrade turns resource endpoints into 404s
> or makes name resolution fail, the fix is to add the missing `AddResource<T>()` call for each entity that used to
> be picked up by decoration.

`Use<...>(endpoints, configure)` also accepts an `Action<ResourceAttribute>` so a caller can set
`Operations`, `Endpoints`, or `Methods` without entity attributes.

Both calls land in the same place. Registration first runs `EnsureAddressablePattern`, which throws
`InvalidOperationException` when an `ICanonicalName` entity carries no `[CanonicalName]` pattern ending in a
placeholder preceded by a collection literal. It then keys the `ResourceAttribute` on `entity.TypeHandle`, registers
per-entity Create/Update idempotency advisors, and — for `ISoftDelete` entities — adds the built-in `undelete`,
`expunge`, and `purge` methods (each skipped when the `Operations` whitelist excludes it or the entity already
declares that verb).

`SchemataResourceBuilder` owns the registry that receives all of this. The first builder constructed over a given
`SchemataOptions` creates it, stores it in the options bag, and registers it as the `IResourceRegistry` singleton;
every later builder — including the ones Flow, Report and Scheduling construct for their own resources — picks up
that same instance. There is no second way in: the underlying `AddResource` extension is internal and takes the
registry as an argument.

## Cross-resource references

A string property annotated with `[ResourceReference]` carries the full AIP-122 canonical name of an
independent resource. Write-time validation runs in the repository pipeline
(`AdviceValidateResourceReferences`, order 140M): typed references (`[ResourceReference(typeof(Book))]`)
must resolve to that exact type through `IResourceTypeResolver`; polymorphic references must resolve to
some registered type.

Setting `ValidateExistence = true` on the attribute additionally requires the referenced row to exist.
`AdviceValidateResourceReferenceExistence` (add and update, order 150M, registered by `UseOwner()` in
`Schemata.Entity.Owner`) queries the target repository by canonical name with owner filtering
suppressed, so cross-owner references resolve. A missing row throws `NotFoundException`; an
unregistered `IResourceTypeResolver` throws `InvalidOperationException`. Polymorphic targets that do
not implement `ICanonicalName` are skipped.

## Filtering requests

List and custom-method requests that carry an AIP-160 `filter` implement
`Schemata.Abstractions.Resource.IFilterRequest` (`string? Filter { get; }`). Inside an advisor,
`QueryableExtensions.ApplyFilter(query, request, services)` compiles the filter through the
`IExpressionCompiler` keyed `aip` and appends it to the query; a malformed filter throws
`InvalidArgumentException` (`INVALID_FILTER`) with field violations on `filter`. The List operation's
full filter pipeline (language resolution, residual evaluation, ordering) is covered in
[Filtering](filtering.md).

## Purge scoping

`PurgeResourceRequest<TEntity>` (the AIP-165 purge wire DTO) carries an optional `Parent` that narrows a purge to
the child collection of one parent resource, applied through `ResourceIdentifiers.ApplyParent`. `Force = false`
is a preview: the `PurgeJob` reports the matching rows (up to a 100-row sample) without deleting them. See
[Delete Pipeline](delete-pipeline.md).

## Handler stages

Every operation runs the same fixed stage sequence. The stage order is hard-coded in the handler; advisor
`Order` only sequences advisors within one stage.

```
IResource{Create|Get|List|Update|Delete}RequestAdvisor   operation-specific request chain (authorizes, shapes the query)
  mapper.Map<TRequest, TEntity>            Create only
    IResource{Create|Update|Delete}Advisor entity-level chain (Create/Update/Delete)
      repository.AddAsync / UpdateAsync / RemoveAsync, then CommitAsync
        mapper.Map<TEntity, TDetail>
          IResourceResponseAdvisor<TEntity, TDetail>   response chain
```

The request stage receives the operation's request DTO, a `ResourceRequestContainer<TEntity>`, and the
`ClaimsPrincipal?`. Each stage runs through
`ResourcePipelineRunner<Operations>.RunAsync`, which interprets the `AdviseResult`:

- `Continue` — proceed to the next stage.
- `Block` — throw the stage's blocked exception. For CRUD that is `NotFoundException`. AIP-211 instead requires
  `PERMISSION_DENIED` with an ambiguous message for authorization failures.
- `Handle` — return a result the advisor stashed in `AdviceContext`, or the handler's fallback (`() => new()`
  for Delete).

Custom methods run through `ResourceMethodOperationHandler<TEntity, TRequest, TResponse>`, which mirrors the
sequence with verb-scoped advisor sockets. See [Custom Methods](custom-methods.md).

## Internal command dispatch

`CreateResourceRequest<,,>`, `UpdateResourceRequest<,,>`, and `DeleteResourceRequest<,>` are
`ICommand<TResult>`; `GetResourceQueryRequest<,>` and `ListResourceQueryRequest<,>` are
`IQuery<TResult>`. Registering a resource with `Use<...>()` / `AddResource<T>()` registers the
matching `Default{Create|Get|List|Update|Delete}ResourceHandler<TEntity,TRequest,TDetail,TSummary>`
as `IRequestHandler<TRequest, TResponse>` (keyed `ResourceConstants.Handlers.Default`, with an
unkeyed alias every dispatcher resolves through). Each default handler is a one-line forward into
`ResourceOperationHandler`'s matching verb method — the stage pipeline above is the actual
orchestration; the handler is just how the dispatcher reaches it.

The HTTP controller and the gRPC service are the only production callers: both resolve
`IRequestDispatcher` and call `SendAsync<TRequest, TResponse>` for every verb — there is no facade
method that wraps the dispatcher the way `IFlowRunner`/`IScheduler`/`IInsightService` do for their
modules. A registered `ICommandAdvisor<TRequest>` / `IQueryAdvisor<TRequest>`
(`Schemata.Messaging.Skeleton.Advisors`) runs before the handler on every dispatch.

**Unlike Flow, Scheduling, and Insight, `ResourceOperationHandler` does not dispatch — it *is* the
continuation point for the CRUD verbs above.** Its `CreateAsync`/`GetAsync`/etc. read
`AdviceContext.Current` (`ResourceAdviceContext.Create`) rather than resolving a dispatcher
themselves, throwing `InvalidOperationException` when no ambient context exists; the dispatcher is
the only caller that reaches it, so whatever a command/query advisor stashed with `ctx.Set<T>(...)`
on the way in is visible to the resource advisors on the way through.

**AIP-136 custom methods are not a continuation of the CRUD pipeline above — they are a second,
independent ambient root whose own dispatch flows through `IRequestDispatcher` to the verb's
`IRequestHandler<TRequest, TResponse>`.** `ResourceMethodOperationHandler` does not call
`ResourceOperationHandler`; it runs its own advisor stages (`IResourceMethodRequestAdvisor` /
`IResourceMethodAdvisor` / etc., see [Handler stages](#handler-stages) above) and is entered directly
by `ResourceMethodController` / `ResourceCustomMethod` for authorization and target validation. It
establishes its own `AdviceContext` when none is ambient — on the same footing as the dispatcher, not a
downstream consumer of the CRUD dispatcher — then assigns `request.Principal = principal` and calls
`IRequestDispatcher.SendAsync<TRequest, TResponse>(request, ct)`. The dispatcher runs any registered
`ICommandAdvisor<TRequest>` / `IQueryAdvisor<TRequest>` before invoking the verb's single
`IRequestHandler<TRequest, TResponse>`. The built-in soft-delete verbs (`undelete`, `expunge`, `purge`)
follow the same path with their own `IRequestHandler<TRequest, TResponse>` implementations registered
in DI as scoped. See [Messaging](../messaging/overview.md#ambient-advicecontext-root-establishes-downstream-continues)
for the ambient `AdviceContext` rules and its sanctioned roots.

## Operation results

Each operation returns a thin result base carrying the response DTO:

| Operation | Result type                 | Members                                                                |
| --------- | --------------------------- | ---------------------------------------------------------------------- |
| Create    | `CreateResultBase<TDetail>` | `TDetail? Detail`                                                      |
| Get       | `GetResultBase<TDetail>`    | `TDetail? Detail`                                                      |
| Update    | `UpdateResultBase<TDetail>` | `TDetail? Detail`                                                      |
| Delete    | `DeleteResultBase<TDetail>` | `TDetail? Detail` (set only for a soft delete)                         |
| List      | `ListResultBase<TSummary>`  | `IList<TSummary>? Entities`, `int? TotalSize`, `string? NextPageToken` |

`ListResultBase<TSummary>` implements `IEntitiesResult<TSummary>`, which drives the plural wire-name rename of
`Entities` (see [HTTP Transport](http-transport.md)).

## Built-in advisor lanes

`SchemataResourceFeature.ConfigureServices` registers these advisors for every resource:

| Advisor                                            | Stage                  |
| -------------------------------------------------- | ---------------------- |
| `AdviceCreateRequestSanitize<TEntity, TRequest>`   | Create request         |
| `AdviceCreateRequestValidation<TEntity, TRequest>` | Create request         |
| `AdviceUpdateRequestSanitize<TEntity, TRequest>`   | Update request         |
| `AdviceUpdateRequestValidation<TEntity, TRequest>` | Update request         |
| `AdviceApplyChildParent<TEntity, TRequest>`        | Create / Update entity |
| `AdviceUpdateSoftDeleted<TEntity, TRequest>`       | Update entity          |
| `AdviceUpdateFreshness<TEntity, TRequest>`         | Update entity          |
| `AdviceDeleteFreshness<TEntity>`                   | Delete entity          |
| `AdviceResponseParent<TEntity, TDetail>`           | Response               |
| `AdviceResponseFreshness<TEntity, TDetail>`        | Response               |
| `AdviceListResponseParent<TSummary>`               | List response          |
| `AdviceResponseIdempotency<TEntity, TDetail>`      | Response               |

`RegisterResource` adds the per-entity Create/Update idempotency advisors
(`AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail>`,
`AdviceUpdateRequestIdempotency<TEntity, TRequest, TDetail>`). Authorization advisors
(`AdviceXxxRequestAnonymous`, `AdviceXxxRequestAuthorize`) are added only when `WithAuthorization()` is called.

## `SchemataResourceBuilder`

`UseResource()` returns a `SchemataResourceBuilder` with these methods:

| Method                                                  | Effect                                                                                                          |
| ------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `WithAuthorization(scheme?)`                            | Registers anonymous + authorize advisors for all operations; sets the default `AuthenticationScheme` when `scheme` is given |
| `WithoutCreateValidation()`                             | Sets `SchemataResourceOptions.SuppressCreateValidation = true`                                                  |
| `WithoutUpdateValidation()`                             | Sets `SchemataResourceOptions.SuppressUpdateValidation = true`                                                  |
| `WithoutFreshness()`                                    | Sets `SchemataResourceOptions.SuppressFreshness = true`                                                         |
| `Use<TEntity, TRequest, TDetail, TSummary>(endpoints?)` | Registers a resource imperatively                                                                               |
| `Use<TEntity...>(Action<ResourceEndpointSelector>)`     | Registers a resource restricted to the selected transports                                                      |
| `MapHttp()`                                             | Adds `SchemataHttpResourceFeature`, returns the same `SchemataResourceBuilder`                                  |
| `MapGrpc()`                                             | Adds `SchemataGrpcResourceFeature`, returns the same `SchemataResourceBuilder`                                  |

## Extension points

- Implement `IResource{Create|Get|List|Update|Delete}RequestAdvisor<...>` for per-operation request hooks
  (authorization, sanitization, validation, idempotency).
- Implement `IResource{Create|Update|Delete}Advisor<...>` for entity-stage logic that runs after mapping and
  before persistence.
- Implement `IResourceResponseAdvisor<TEntity, TDetail>` to post-process the response DTO (freshness,
  idempotency cache).
- Implement `IRequestHandler<TRequest, TResponse>` (`Schemata.Messaging.Skeleton`) for AIP-136 custom verbs
  and reference the handler type from `[ResourceMethod]`; see [Custom Methods](custom-methods.md).
- Register the advisors as scoped through `services.TryAddEnumerable(ServiceDescriptor.Scoped(...))`. Pick an
  `Order` outside the reserved `[100_000_000, 900_000_000]` window.

## Design rationale

The four type parameters separate the persistent shape (`TEntity`), the write surface (`TRequest`), the rich
read surface (`TDetail`), and the list-optimized surface (`TSummary`). Each surface is its own type, so the
handler controls per-operation field visibility through mapping. Keeping the handler free of `HttpContext`
lets the foundation layer run under either transport and stay unit-testable without a web host.

## Caveats

- All four type parameters must implement `ICanonicalName`. An entity with a different identity scheme needs
  `Name` and `CanonicalName` properties plus an addressable `[CanonicalName("...")]` pattern.
- `SchemataResourceFeature` is registered through `AddFeature`, which deduplicates by `RuntimeTypeHandle`, so
  calling `UseResource()` twice is safe.
- Decorating an entity with `[Resource]` does not register it. The attribute supplies the type roles; the entity
  still has to reach `AddResource<T>()` or `Use<...>()`.

## See also

- [Create Pipeline](create-pipeline.md)
- [Read Pipeline](read-pipeline.md)
- [Update Pipeline](update-pipeline.md)
- [Delete Pipeline](delete-pipeline.md)
- [Resource Naming](resource-naming.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Custom Methods](custom-methods.md)
