# gRPC Transport

The gRPC transport exposes resources as code-first gRPC services using protobuf-net (ProtoBuf.Grpc). It is activated by calling `MapGrpc()` on the resource builder.

## Setup

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .MapGrpc()
          .Use<Book, BookRequest, BookDetail, BookSummary>();
});
```

`MapGrpc()` adds the `SchemataGrpcResourceFeature`, which depends on `SchemataResourceFeature`.

## IResourceService

`IResourceService<TEntity, TRequest, TDetail, TSummary>` is the service contract interface. It is decorated with `[Service]` and each method with `[Operation]` (from `ProtoBuf.Grpc.Configuration`).

| Method        | Request Type    | Return Type                       |
| ------------- | --------------- | --------------------------------- |
| `ListAsync`   | `ListRequest`   | `ValueTask<ListResult<TSummary>>` |
| `GetAsync`    | `GetRequest`    | `ValueTask<TDetail>`              |
| `CreateAsync` | `TRequest`      | `ValueTask<TDetail>`              |
| `UpdateAsync` | `TRequest`      | `ValueTask<TDetail>`              |
| `DeleteAsync` | `DeleteRequest` | `ValueTask`                       |

All methods accept an optional `CallContext` parameter from ProtoBuf.Grpc.

## ResourceService

`ResourceService<TEntity, TRequest, TDetail, TSummary>` implements `IResourceService` and delegates to `ResourceOperationHandler`. It is registered as a scoped open generic.

Key differences from the HTTP controller:

- **Entity resolution uses canonical names**: `GetAsync`, `UpdateAsync`, and `DeleteAsync` resolve entities via `Handler.GetByCanonicalNameAsync(request.CanonicalName, ct)`, parsing the full resource path. HTTP uses route-based resolution instead.
- **Blocked results throw**: When an operation is blocked, the gRPC service throws `NoContentException` (for Get, Create, Update), returns an empty `ListResult` (for List), or completes silently (for Delete). The `ExceptionMappingInterceptor` translates exceptions to gRPC status codes.
- **No Location header**: Unlike HTTP, there is no equivalent of the 201 Created + Location header pattern.

## Service Naming

`ResourceServiceBinder` (a custom `ServiceBinder` from ProtoBuf.Grpc) controls how gRPC services and methods are named:

### Service Name

For `IResourceService<Book, BookRequest, BookDetail, BookSummary>`:

1. Reads `Package` from `ResourceNameDescriptor` (set via `[ResourcePackage]`). Falls back to the entity's namespace.
2. Combines with the singular name: `{package}.{Singular}Service`.

Examples:

- `[ResourcePackage("library.v1")]` on `Book` produces `library.v1.BookService`.
- No package, namespace `MyApp.Models` produces `MyApp.Models.BookService`.

### Method Names

The binder strips the `Async` suffix and adjusts the method name:

| Interface Method | gRPC Method Name                        |
| ---------------- | --------------------------------------- |
| `ListAsync`      | `List{Plural}` (e.g., `ListBooks`)      |
| `GetAsync`       | `Get{Singular}` (e.g., `GetBook`)       |
| `CreateAsync`    | `Create{Singular}` (e.g., `CreateBook`) |
| `UpdateAsync`    | `Update{Singular}` (e.g., `UpdateBook`) |
| `DeleteAsync`    | `Delete{Singular}` (e.g., `DeleteBook`) |

## ProtoBuf Serialization

`RuntimeTypeModelConfigurator` creates a `RuntimeTypeModel` at `CompatibilityLevel.Level300` and auto-configures all resource types:

1. Configures `ListRequest`, `GetRequest`, and `DeleteRequest` as base types.
2. For each registered gRPC resource, configures `TRequest`, `TDetail`, `TSummary`, and `ListResult<TSummary>`.
3. For each type, discovers writable properties via `AppDomainTypeCache.GetWritableProperties` and adds them as protobuf fields with sequential field numbers starting at 1.
4. Property names are converted to snake_case via Humanizer's `Underscore()`.

Special property name mappings:

| Property                       | Serialized As | Notes                                                                                            |
| ------------------------------ | ------------- | ------------------------------------------------------------------------------------------------ |
| `ICanonicalName.Name`          | (skipped)     | Not serialized. gRPC uses only the full resource path.                                           |
| `ICanonicalName.CanonicalName` | `name`        | The fully-qualified path (e.g. `publishers/acme/books/les-miserables`) becomes the `name` field. |
| `IFreshness.EntityTag`         | `etag`        | Per AIP conventions.                                                                             |

For `ListResult<TSummary>`, the `entities` field is renamed to the pluralized entity name in snake_case (e.g., `books` for `ListResult<BookSummary>`).

### Comparison with HTTP

The HTTP transport applies its own equivalent mappings via `ResourceJsonOptions` (see [HTTP Transport § JSON Serialization](http-transport.md#json-serialization)). The two transports intentionally diverge on how resource names are exposed:

| C# Property                    | HTTP (JSON)               | gRPC (ProtoBuf)           |
| ------------------------------ | ------------------------- | ------------------------- |
| `ICanonicalName.Name`          | `name` (kept)             | (skipped)                 |
| `ICanonicalName.CanonicalName` | (removed)                 | `name`                    |
| `IFreshness.EntityTag`         | `etag`                    | `etag`                    |
| `ListResult<T>.Entities`       | `{plural}` (e.g. `books`) | `{plural}` (e.g. `books`) |

HTTP exposes the short resource ID as `name` because the canonical path is already implicit in the URL. gRPC exposes the fully-qualified `CanonicalName` as `name`.

## ExceptionMappingInterceptor

`ExceptionMappingInterceptor` is a gRPC `Interceptor` registered via `AddCodeFirstGrpc`. It wraps all unary server calls and translates exceptions to structured gRPC errors:

### Exception to Status Code Mapping

| SchemataException Code | gRPC StatusCode          |
| ---------------------- | ------------------------ |
| `InvalidArgument`      | `InvalidArgument` (3)    |
| `NotFound`             | `NotFound` (5)           |
| `PermissionDenied`     | `PermissionDenied` (7)   |
| `Aborted`              | `Aborted` (10)           |
| `AlreadyExists`        | `AlreadyExists` (6)      |
| `FailedPrecondition`   | `FailedPrecondition` (9) |
| `Unauthenticated`      | `Unauthenticated` (16)   |
| `ResourceExhausted`    | `ResourceExhausted` (8)  |
| (any other)            | `Internal` (13)          |

Non-`SchemataException` exceptions are mapped to `Internal` (13).

### Structured Error Details

The interceptor builds a `Google.Rpc.Status` protobuf message with:

1. The numeric status code.
2. The exception message.
3. Typed detail messages packed as `google.protobuf.Any`:
   - `BadRequest` with `FieldViolation` entries from `BadRequestDetail`.
   - `ErrorInfo` from `ErrorInfoDetail`.
   - `ResourceInfo` from `ResourceInfoDetail`.
   - `PreconditionFailure` from `PreconditionFailureDetail`.
   - `QuotaFailure` from `QuotaFailureDetail`.
   - `RequestInfo` with the `TraceIdentifier` as the request ID.

The status is serialized and attached as `grpc-status-details-bin` trailing metadata on the `RpcException`, following the standard gRPC richer error model.

## Reflection Service

`SchemataGrpcResourceFeature` registers a gRPC reflection service (`ProtoBuf.Grpc.Reflection.ReflectionService`) when at least one gRPC resource is registered.

`ReflectionServiceFactory` generates the schema:

1. Uses `SchemaGenerator` from ProtoBuf.Grpc to produce a proto schema for all service interfaces.
2. Adds well-known Google RPC proto definitions (`code.proto`, `status.proto`, `error_details.proto`) including messages for `ErrorInfo`, `BadRequest`, `ResourceInfo`, `PreconditionFailure`, `QuotaFailure`, `RequestInfo`, `RetryInfo`, `DebugInfo`, `Help`, and `LocalizedMessage`.
3. Assembles everything into a `FileDescriptorSet` and creates the reflection service.

This enables tools like `grpcurl` and gRPC UI to discover and invoke resource services.

## Rate Limiting

If the entity type has a `[RateLimitPolicy]` attribute, `SchemataGrpcResourceFeature` calls `RequireRateLimiting(policyName)` on the `GrpcServiceEndpointConventionBuilder` for that service.

## Restricting to gRPC Only

To register a resource for gRPC only (not HTTP), use the `MapGrpc()` builder chain:

```csharp
schema.UseResource()
      .MapGrpc()
      .Use<Book, BookRequest, BookDetail, BookSummary>();
```

This passes `["gRPC"]` as the `Endpoints` list, so `SchemataHttpResourceFeature` skips this resource. Alternatively, apply `[GrpcResource]` on the entity class for attribute-based registration.

## Dual Transport

To expose a resource on both HTTP and gRPC:

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Book, BookRequest, BookDetail, BookSummary>()
      .Builder
      .MapGrpc()
      .Use<Book, BookRequest, BookDetail, BookSummary>();
```

Or use attribute-based registration with both `[HttpResource]` and `[GrpcResource]` on the entity, or simply use `[ResourceAttribute]` without specifying endpoints (all active transports are used).
