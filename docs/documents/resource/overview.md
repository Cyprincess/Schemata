# Resource Overview

`ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>` orchestrates all five standard CRUD operations (List, Get, Create, Update, Delete) for a resource. Each operation runs a fixed sequence of advisor stages: a general gate check, an operation-specific request advisor chain, optional entity-level advisors, persistence, and a response advisor chain. The handler is transport-agnostic — HTTP and gRPC entry points both delegate to it, passing a `ClaimsPrincipal?` extracted from their respective request contexts.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs`, `SchemataResourceBuilder.cs`, `ResourceRequestContainer.cs` |
| `Schemata.Resource.Foundation` | `Features/SchemataResourceFeature.cs` |
| `Schemata.Resource.Foundation` | `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Abstractions` | `Entities/ICanonicalName.cs`, `Entities/CanonicalNameAttribute.cs` |
| `Schemata.Abstractions` | `Resource/ResourceAttribute.cs` |

## The four type parameters

Every resource is identified by four type parameters:

| Parameter | Role | Constraint |
|---|---|---|
| `TEntity` | Persistent entity stored in the repository | `class, ICanonicalName` |
| `TRequest` | DTO received on Create and Update | `class, ICanonicalName` |
| `TDetail` | DTO returned from Get, Create, and Update | `class, ICanonicalName` |
| `TSummary` | DTO returned from List | `class, ICanonicalName` |

All four must implement `ICanonicalName`, which requires `Name` and `CanonicalName` string properties. Resources are addressed externally by the AIP-122 canonical name string; internal `Uid` values stay inside the persistence layer.

### Rightward-collapse rule

When you register a resource with fewer than four type parameters, missing slots are filled rightward from the last supplied type:

```csharp
// All four slots collapse to Student
builder.UseResource().MapHttp().Use<Student>();
// Equivalent to:
builder.UseResource().MapHttp().Use<Student, Student, Student, Student>();

// TDetail and TSummary collapse to StudentRequest
builder.UseResource().MapHttp().Use<Student, StudentRequest>();
// Equivalent to:
builder.UseResource().MapHttp().Use<Student, StudentRequest, StudentRequest, StudentRequest>();

// TSummary collapses to StudentDetail
builder.UseResource().MapHttp().Use<Student, StudentRequest, StudentDetail>();
// Equivalent to:
builder.UseResource().MapHttp().Use<Student, StudentRequest, StudentDetail, StudentDetail>();
```

This collapse is implemented by the `SchemataHttpResourceBuilderExtensions` and `SchemataGrpcResourceBuilderExtensions` overloads, each of which delegates to the four-parameter `Use<TEntity, TRequest, TDetail, TSummary>` on `SchemataResourceBuilder`.

## Enabling the resource system

Call `UseResource()` on `SchemataBuilder` to register `SchemataResourceFeature` and get back a `SchemataResourceBuilder`:

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

`SchemataResourceFeature` has `Priority = Orders.Extension + 90_000_000` (490M). It depends on `SchemataRoutingFeature`, `SchemataMappingFeature`, and `SchemataSecurityFeature` via `[DependsOn<T>]` attributes, so those features are auto-registered if not already present.

## Resource registration

A resource can be registered in two ways.

**Imperative** — call `Use<TEntity, ...>()` on the transport builder:

```csharp
schema.UseResource().MapHttp().Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

**Declarative** — annotate the entity with `[Resource]`. `SchemataResourceFeature.ConfigureServices` scans every non-dynamic assembly in `AppDomain.CurrentDomain.GetAssemblies()` and calls `RegisterResource` for every type carrying `[ResourceAttribute]`. The same scan picks up any `[ResourceMethod]` attributes on the same class and stores them in `SchemataResourceOptions.Methods` keyed by `(entity.TypeHandle, verb)`.

Both paths converge at `SchemataResourceFeature.RegisterResource`, which keys the `ResourceAttribute` on `entity.TypeHandle` and registers per-entity idempotency advisors for Create and Update.

## Handler stages

Each operation runs a fixed sequence of stages. The order of stages is hard-coded in the handler; advisor `Order` only controls sequencing within a stage.

```
IResourceRequestAdvisor<TEntity>          (gate: all operations, second arg is the operation token)
  IResourceXxxRequestAdvisor<...>         (operation-specific request chain)
    mapper.Map<TRequest, TEntity>         (Create/Update only)
      IResourceXxxAdvisor<...>            (entity-level: Update/Delete only)
        repository.AddAsync / UpdateAsync / RemoveAsync + CommitAsync
          mapper.Map<TEntity, TDetail>
            IResourceResponseAdvisor<TEntity, TDetail>
```

The second argument to `IResourceRequestAdvisor<TEntity>` is a `string`: `nameof(Operations.List|Get|Create|Update|Delete)` for CRUD and the lowerCamelCase verb (`"run"`, `"archive"`, `"batchCreate"`) for AIP-136 custom methods. A `Block` result at any stage short-circuits to `XxxResultBase<T>.Blocked`. A `Handle` result means "I stashed the answer in `AdviceContext`; pull it and return." The handler checks `ctx.TryGet<XxxResultBase<T>>(out var result)` after every `Handle`.

Custom methods run through `ResourceMethodOperationHandler<TEntity, TRequest, TResponse>`, which mirrors the same stage sequence with verb-scoped sockets (`IResourceMethodRequestAdvisor`, `IResourceMethodAdvisor`, `IResourceResponseAdvisor`) and a verb-keyed idempotency lane. See [Custom Methods](custom-methods.md).

## Built-in advisor lanes

`SchemataResourceFeature` registers these advisors for all resources:

| Advisor | Stage | Order |
|---|---|---|
| `AdviceCreateRequestSanitize<TEntity, TRequest>` | Create request | `AdviceCreateRequestAuthorize.DefaultOrder + 10M` |
| `AdviceCreateRequestValidation<TEntity, TRequest>` | Create request | `AdviceCreateRequestSanitize.DefaultOrder + 10M` |
| `AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail>` | Create request | `AdviceCreateRequestValidation.DefaultOrder + 10M` |
| `AdviceUpdateRequestSanitize<TEntity, TRequest>` | Update request | same lane as Create |
| `AdviceUpdateRequestValidation<TEntity, TRequest>` | Update request | same lane as Create |
| `AdviceUpdateRequestIdempotency<TEntity, TRequest, TDetail>` | Update request | `AdviceUpdateRequestValidation.DefaultOrder + 10M` |
| `AdviceUpdateFreshness<TEntity, TRequest>` | Update entity | `Orders.Base` (100M) |
| `AdviceDeleteFreshness<TEntity>` | Delete entity | `Orders.Base` (100M) |
| `AdviceResponseFreshness<TEntity, TDetail>` | Response | `Orders.Base` (100M) |
| `AdviceResponseIdempotency<TEntity, TDetail>` | Response | after freshness |

Authorization advisors (`AdviceXxxRequestAnonymous`, `AdviceXxxRequestAuthorize`) are registered separately by calling `WithAuthorization()` on `SchemataResourceBuilder`.

Idempotency advisors are registered per-entity by `RegisterResource`, not globally. `PendingIdempotencyKey` carries an `Operation` token so cache keys partition across Create, Update, and each custom-method verb: `idempotency\x1e{Operation}\x1e{RequestId}`.

## `SchemataResourceBuilder` configuration

`UseResource()` returns a `SchemataResourceBuilder` with these methods:

| Method | Effect |
|---|---|
| `WithAuthorization(scheme?)` | Registers anonymous + authorize advisors for all five operations |
| `WithoutCreateValidation()` | Sets `SchemataResourceOptions.SuppressCreateValidation = true` |
| `WithoutUpdateValidation()` | Sets `SchemataResourceOptions.SuppressUpdateValidation = true` |
| `WithoutFreshness()` | Sets `SchemataResourceOptions.SuppressFreshness = true` |
| `Use<TEntity, TRequest, TDetail, TSummary>(endpoints?)` | Registers a resource imperatively |
| `MapHttp()` | Adds `SchemataHttpResourceFeature`, returns `SchemataHttpResourceBuilder` |
| `MapGrpc()` | Adds `SchemataGrpcResourceFeature`, returns `SchemataGrpcResourceBuilder` |

## Design motivation

The four type parameters separate the persistent shape (`TEntity`), write surface (`TRequest`), rich read surface (`TDetail`), and list-optimized surface (`TSummary`). Each surface is its own type so the handler controls field visibility per operation without conditional logic.

The handler is free of `HttpContext` — both transports extract a `ClaimsPrincipal?` and pass it down. This keeps the foundation layer testable without an HTTP stack.

## Caveats

- All four type parameters must implement `ICanonicalName`. If your entity uses a different identity scheme, add `Name` and `CanonicalName` properties and annotate with `[CanonicalName("...")]`.
- `SchemataControllersFeature` strips all `Schemata.*` assembly parts from MVC's `ApplicationPartManager`. Framework controllers are opt-in only. `SchemataHttpResourceFeature` synthesizes `ResourceController<...>` instances at startup; you don't register them manually.
- `AddFeature` deduplicates by `RuntimeTypeHandle`. `SchemataResourceFeature` is a singleton; calling `UseResource()` twice is safe.

## See also

- [Create Pipeline](create-pipeline.md)
- [Read Pipeline](read-pipeline.md)
- [Update Pipeline](update-pipeline.md)
- [Delete Pipeline](delete-pipeline.md)
- [Resource Naming](resource-naming.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Filtering](filtering.md)
- [Custom Methods](custom-methods.md)
- [Advice Pipeline](../core/advice-pipeline.md)
- [Entity Traits](../entity/traits.md)
