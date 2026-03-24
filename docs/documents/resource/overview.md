# Overview

The resource system provides a transport-agnostic CRUD pipeline for entities. At its core sits `ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>`, which orchestrates five standard operations -- List, Get, Create, Update, and Delete -- through a chain of [advisors](../core/advice-pipeline.md) before and after persistence.

## Type Parameters

Every resource is defined by four types. All four must implement `ICanonicalName`.

| Parameter  | Purpose                                                                          |
| ---------- | -------------------------------------------------------------------------------- |
| `TEntity`  | The persistent entity stored in the database.                                    |
| `TRequest` | The inbound DTO for Create and Update operations.                                |
| `TDetail`  | The outbound DTO returned from Get, Create, and Update.                          |
| `TSummary` | The outbound DTO returned from List operations (typically a lighter projection). |

When you do not need separate types, convenience overloads let you specify fewer parameters:

- `Use<TEntity>()` -- uses `TEntity` for all four.
- `Use<TEntity, TRequest>()` -- uses `TRequest` for detail and summary.
- `Use<TEntity, TRequest, TDetail>()` -- uses `TDetail` as the summary type.

## Registering Resources

Resources are wired up through a fluent builder obtained from `SchemataBuilder`:

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .WithAuthorization()
          .MapHttp()
          .Use<Book, BookRequest, BookDetail, BookSummary>();
});
```

The call chain breaks down as follows:

1. **`UseResource()`** -- adds `SchemataResourceFeature`, which registers the core services:
   - `ResourceOperationHandler<,,,>` as a scoped open generic.
   - Built-in advisors for validation, freshness, and idempotency.
   - A default `IIdempotencyStore` backed by `IDistributedCache`.
   - Scans all loaded assemblies for types decorated with `[ResourceAttribute]` and registers each one automatically.

2. **`WithAuthorization()`** (optional) -- registers authorization advisors for all five operations. Without this call, no access checks run.

3. **`MapHttp()`** / **`MapGrpc()`** -- activates a transport layer and returns a transport-specific builder. See [HTTP Transport](./http-transport.md) and [gRPC Transport](./grpc-transport.md).

4. **`Use<>()`** -- registers a specific resource with the chosen transport. The full four-parameter form is `Use<TEntity, TRequest, TDetail, TSummary>()`.

## Attribute-Based Registration

Instead of calling `Use<>()` in the builder, you can decorate your entity class with one of the `ResourceAttribute` generic variants:

```csharp
[ResourceAttribute<Book, BookRequest, BookDetail, BookSummary>]
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { ... }
```

`SchemataResourceFeature` scans all exported types in loaded assemblies during `ConfigureServices`. Any type with a `[ResourceAttribute]` is registered automatically. Shorter forms are available:

- `ResourceAttribute<TEntity>` -- all four types are `TEntity`.
- `ResourceAttribute<TEntity, TRequest>` -- detail defaults to `TEntity`.
- `ResourceAttribute<TEntity, TRequest, TDetail>` -- summary defaults to `TDetail`.

You can restrict which transports a resource is exposed on by setting the `Endpoints` property, or by applying `[HttpResource]` and/or `[GrpcResource]` attributes on the entity. When `Endpoints` is null or empty, the resource is exposed on all active transports.

## Advisor Pipeline

Every operation follows the same general advisor flow:

1. `IResourceRequestAdvisor<TEntity>` -- a general gate that runs before any operation-specific logic, receiving the `HttpContext` and the `Operations` enum value.
2. Operation-specific request advisor (e.g., `IResourceCreateRequestAdvisor<TEntity, TRequest>`).
3. Operation-specific entity advisor (e.g., `IResourceCreateAdvisor<TEntity, TRequest>`), which has access to both the request and the entity.
4. Persistence via `IRepository<TEntity>`.
5. `IResourceResponseAdvisor<TEntity, TDetail>` -- post-processing on the mapped detail DTO.

Each advisor returns one of three results:

| Result     | Effect                                                         |
| ---------- | -------------------------------------------------------------- |
| `Continue` | Proceed to the next advisor or step.                           |
| `Handle`   | Short-circuit with a result stored in the `AdviceContext`.     |
| `Block`    | Silently deny the operation (returns an empty/blocked result). |

See the individual pipeline pages for the full step-by-step flow: [Create](./create-pipeline.md), [Read](./read-pipeline.md), [Update](./update-pipeline.md), [Delete](./delete-pipeline.md).

## Built-In Advisors

`SchemataResourceFeature` registers these advisors globally for all resources:

| Advisor                         | Interface                       | Order       |
| ------------------------------- | ------------------------------- | ----------- |
| `AdviceCreateRequestValidation` | `IResourceCreateRequestAdvisor` | 120,000,000 |
| `AdviceUpdateRequestValidation` | `IResourceUpdateRequestAdvisor` | 110,000,000 |
| `AdviceUpdateFreshness`         | `IResourceUpdateAdvisor`        | 100,000,000 |
| `AdviceDeleteFreshness`         | `IResourceDeleteAdvisor`        | 100,000,000 |
| `AdviceResponseFreshness`       | `IResourceResponseAdvisor`      | 100,000,000 |
| `AdviceResponseIdempotency`     | `IResourceResponseAdvisor`      | 900,000,000 |

Per-resource registration additionally adds:

| Advisor                          | Interface                       | Order       |
| -------------------------------- | ------------------------------- | ----------- |
| `AdviceCreateRequestIdempotency` | `IResourceCreateRequestAdvisor` | 100,000,000 |

When `WithAuthorization()` is called, these are also registered:

| Advisor                        | Interface                       | Order       |
| ------------------------------ | ------------------------------- | ----------- |
| `AdviceListRequestAuthorize`   | `IResourceListRequestAdvisor`   | 100,000,000 |
| `AdviceGetRequestAuthorize`    | `IResourceGetRequestAdvisor`    | 100,000,000 |
| `AdviceCreateRequestAuthorize` | `IResourceCreateRequestAdvisor` | 110,000,000 |
| `AdviceUpdateRequestAuthorize` | `IResourceUpdateRequestAdvisor` | 100,000,000 |
| `AdviceDeleteRequestAuthorize` | `IResourceDeleteRequestAdvisor` | 100,000,000 |

## Global Options

`SchemataResourceBuilder` exposes methods to globally suppress certain behavior:

- `WithoutCreateValidation()` -- sets `SchemataResourceOptions.SuppressCreateValidation` to `true`.
- `WithoutUpdateValidation()` -- sets `SchemataResourceOptions.SuppressUpdateValidation` to `true`.
- `WithoutFreshness()` -- sets `SchemataResourceOptions.SuppressFreshness` to `true`, which places a `SuppressFreshness` marker in the `AdviceContext` for every operation, disabling ETag generation and checking.

## Dependencies

The handler depends on:

- `IRepository<TEntity>` from the [entity repository](../repository/overview.md) system.
- `ISimpleMapper` from the mapping system for converting between entity and DTO types.
- `IServiceProvider` for resolving advisors and options.
