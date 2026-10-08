# Resource Overview

`ResourceOperationHandler<TEntity,TRequest,TDetail,TSummary>` implements List, Get, Create, Update, and Delete. HTTP and gRPC submit typed request envelopes through `IRequestDispatcher`, so dispatcher-wrap policy and handler-stage policy receive the same ambient `AdviceContext`.

## Type roles

| Type | Role |
| --- | --- |
| `TEntity` | Persistent resource entity |
| `TRequest` | Create and Update payload |
| `TDetail` | Get, Create, and Update response detail |
| `TSummary` | List item |

All four types implement `ICanonicalName`. `Use<TEntity>()` fills omitted trailing type arguments from the preceding type; `ResourceAttribute` carries the equivalent declarative roles.

## Registration and transports

`UseResource()` returns `SchemataResourceBuilder`, which implements `IResourceBuilder`. Register each resource explicitly with `Use<...>()` or `AddResource<TEntity>()`; an attribute alone does not register endpoints.

```csharp
builder.UseSchemata(schema => {
    schema.UseSecurity();
    schema.UseResource()
          .WithAuthentication("Bearer")
          .WithAuthorization()
          .MapHttp()
          .MapGrpc()
          .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
});
```

`WithAuthentication` and `WithAuthorization` are the shared generic Security extensions. Their resource registration supplies the domain-specific advisor closures. `MapHttp()` and `MapGrpc()` are concrete Resource transport extensions that activate one Resource transport feature. The feature dependencies activate shared HTTP or gRPC behavior.

`ResourceAttribute` and `ResourceMethodAttribute` are mutable configuration inputs. `ResourceRegistry` copies them during registration and publishes get-only `ResourceRegistration` and `ResourceMethodRegistration` snapshots when first read. `Resources`, `GetResource`, and `GetMethods` share that sealed result; register every resource before reading the registry. Later changes to the input attributes, endpoint arrays, operation arrays, and custom-method lists leave the installed routes and policies unchanged.

When input `Endpoints` is null, the registration input-copy boundary resolves `[HttpResource]` and `[GrpcResource]` declarations from the entity. Without endpoint declarations, the resource uses every enabled adapter. An explicit empty endpoint list selects every enabled adapter even on a transport-decorated entity. The resulting snapshot supplies HTTP discovery, gRPC routes, and reflection; these runtime consumers read the copied policy. Source: `src/Schemata.Core/Building/ResourceRegistration.cs`.

Repeated registration of one entity requires the same entity/request/detail/summary tuple. An unset authentication scheme, page size, total-size policy, or operation whitelist can be supplied by a later registration. Conflicting explicit values fail at the registration call before installing that registration's services. Explicit empty operations suppress every standard method; custom methods remain available. Endpoint restrictions are copied and unioned, with an unrestricted registration exposing the resource on every enabled transport. Equivalent custom methods deduplicate by verb; a conflicting handler, scope, or HTTP method fails.

The builder and `AddResource` use the same registry. Its merged snapshots supply handler wiring, paging, authentication, HTTP conventions, gRPC model configuration, and reflection. Resource naming remains owned by the entity's `ResourceNameMetadata`, independently of request/detail/summary DTOs. Source: `src/Schemata.Core/Building/ResourceRegistry.cs`, `ResourceRegistration.cs`, and the Resource Foundation registration extensions.

`WithoutCreateValidation()`, `WithoutUpdateValidation()`, and `WithoutFreshness()` select the stages installed for the host. The shared registry applies these choices when resources register and when feature configuration installs the shared pipeline into the host collection. Choices can precede resource registration or follow it; repeated `UseResource()` retains them. `WithoutFreshness()` removes built-in freshness checks and the default response ETag provider while preserving host-supplied providers. The `ValidateOnly` terminator and per-request suppression markers retain their own behavior.

## Dispatcher-wrap pipeline

Resource registration adds the following wraps for the closed request envelopes. The dispatcher runs before segments in ascending `SecurityOrders` order and runs after segments in reverse order.

| Order | Before work | After work |
| --- | --- | --- |
| `Authentication` | Authenticates non-anonymous operations. | |
| `Authorization` | Matches the verb and resource permission for non-anonymous operations. | |
| `Sanitize` | Clears server-managed fields from Create and Update payloads. | |
| `Validation` | Runs Create and Update request validation. | |
| `Idempotency` | Reserves or replays an AIP-155 request. | Commits the fully shaped payload. |
| `ResponseFamily` | | Shapes list summaries and detail responses. |

The idempotency advisor holds its reservation in local invocation state. Suppression markers remain in `AdviceContext`; cache identity and serialized payloads do not.

Detail-response wraps derive `IChild.Parent` from the response detail's canonical name, then obtain an ETag from `IEntityTagProvider` when the detail implements `IFreshness`. The default provider derives the weak tag from a populated concurrency timestamp. List-response wraps derive parent values for summaries.

## Handler stages

Handlers retain work that needs a loaded entity or a query container:

```text
handler request stage: entitlement predicate and container-scoped policy
  map or load entity
    handler entity stage: instance access, freshness, and entity policy
      repository work and commit
```

The instance access advisor calls `IAccessProvider<TEntity,TRequest>` with the mapped entity on Create and the loaded entity on Get, Update, Delete, and instance methods. Entitlement advisors apply `IEntitlementProvider<TEntity,TRequest>` expressions to the request container, including anonymous query operations. Resource request and entity extension points remain `IAdvisor<>` interfaces and run through `Advisor.For<>()`.

## Custom methods

`ResourceMethodOperationHandler<TEntity,TRequest,TResponse>` constructs `ResourceMethodRequest<TEntity,TRequest,TResponse>` with the verb, target name, payload, and principal and dispatches it. The envelope makes the verb available to Authentication, Authorization, response, and idempotency wraps. Its Resource dispatch handler then runs method request and entity stages, loads an instance target when needed, and dispatches the inner method request.

## Security behavior

Authentication and coarse authorization are independently enabled. Authentication throws `UNAUTHENTICATED` for a non-anonymous caller without an authenticated identity. Coarse authorization uses `IPermissionResolver` and `IPermissionMatcher`. A denied Get returns `NOT_FOUND`; a denied Update or Delete returns `PERMISSION_DENIED` only when the principal matches the corresponding Get permission, otherwise `NOT_FOUND`. Create, List, and method operations return `PERMISSION_DENIED` on a coarse denial.

`IAccessProvider` customizes instance authorization. `IPermissionResolver` and `IPermissionMatcher` customize coarse authorization. A closed registration can override either provider for a specific entity and request type.

## Resource result types

| Operation | Result |
| --- | --- |
| Create | `CreateResultBase<TDetail>` |
| Get | `GetResultBase<TDetail>` |
| Update | `UpdateResultBase<TDetail>` |
| Delete | `DeleteResultBase<TDetail>` |
| List | `ListResultBase<TEntity, TSummary>` |

## See also

- [Create pipeline](create-pipeline.md)
- [Read pipeline](read-pipeline.md)
- [Update pipeline](update-pipeline.md)
- [Delete pipeline](delete-pipeline.md)
- [Custom methods](custom-methods.md)
- [Security](../security.md)
