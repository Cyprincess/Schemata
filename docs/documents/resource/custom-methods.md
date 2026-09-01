# Custom Methods

Custom methods add verbs to a resource that do not fit standard CRUD. They follow AIP-136 on both transports,
ride the same advisor pipeline as the CRUD operations, and carry the verb through `AdviceContext` as a
lowerCamelCase string.

## Where the code lives

| Package                        | Key files                                                                                                                                                                 |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Abstractions`        | `Resource/ResourceMethodAttribute.cs`, `Resource/ResourceMethodScope.cs`, `Resource/ResourceHttpMethod.cs`                                                       |
| `Schemata.Resource.Foundation` | `ResourceMethodOperationHandler.cs`, `Advisors/IResourceMethodRequestAdvisor.cs`, `Advisors/IResourceMethodAdvisor.cs`, `Advisors/ResourceMethodVerb.cs`                  |
| `Schemata.Resource.Foundation` | `Advisors/AdviceMethodRequestAnonymous.cs`, `Advisors/AdviceMethodRequestAuthorize.cs`, `Advisors/AdviceMethodRequestIdempotency.cs`, `Advisors/AdviceMethodEntityAuthorize.cs`, `Advisors/AdviceMethodFreshness.cs` |
| `Schemata.Resource.Http`       | `ResourceMethodController.cs`, `ResourceMethodControllerConvention.cs`, `ResourceMethodControllerFeatureProvider.cs`                                                      |
| `Schemata.Resource.Grpc`       | `ResourceCustomMethod.cs`, `ResourceMethodNaming.cs`                                                                                                                      |

## Declaring a custom method

```csharp
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;

[Resource<Job, JobRequest, JobDetail, JobSummary>]
[ResourceMethod("run", typeof(RunJobHandler), ResourceMethodScope.Instance)]
[CanonicalName("jobs/{job}")]
public sealed class Job : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? Status        { get; set; }
}

public sealed class RunJobRequest : ICommand<RunJobResponse>, IRequestPrincipal, ICanonicalName
{
    public string? Filter { get; set; }

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed class RunJobResponse : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}

public sealed class RunJobHandler : IRequestHandler<RunJobRequest, RunJobResponse>
{
    public Task<RunJobResponse> HandleAsync(
        RunJobRequest      request,
        CancellationToken  ct = default) {
        // ...
        var response = new RunJobResponse {
            Name          = request.Name,
            CanonicalName = request.CanonicalName,
        };
        return Task.FromResult(response);
    }
}
```

`ResourceMethodAttribute(verb, handler, scope = Instance)` stores the verb (lowerCamelCase), the handler type,
and the scope, plus an optional `Method` (`ResourceHttpMethod`). Each custom verb owns a dedicated wire request
implementing `IRequest<TResponse>` (normally `ICommand<TResponse>` or `IQuery<TResponse>`),
`IRequestPrincipal`, and — for instance-scoped methods — `ICanonicalName` so the URI target can be carried on
the request for AIP-155 idempotency. The handler implements
`Schemata.Messaging.Skeleton.IRequestHandler<TRequest, TResponse>` with a single
`HandleAsync(TRequest, CancellationToken)` method.

During resource registration (`SchemataResourceFeature.AddResource` →
`ServiceCollectionExtensions.AddResource`), every `ResourceMethodAttribute` is read off the entity. The handler
type is matched against the closed `IRequestHandler<TRequest, TResponse>` interface through
`ResourceMethodHandlerHelper.Describe`, which requires the request argument to implement `IRequestPrincipal`
and the response argument to implement `ICanonicalName`. A handler that does not match throws
`InvalidOperationException`. The matched interface is registered with DI as scoped, the verb and methods are
stored against the entity in `IResourceRegistry`, and — whenever the request implements
`ICanonicalName` — the per-verb idempotency and freshness advisors are added
(`AdviceMethodRequestIdempotency`, `AdviceMethodFreshness`).

### Scope

| Scope        | HTTP route                            | gRPC RPC           | Target validation |
| ------------ | ------------------------------------- | ------------------ | ----------------- |
| `Instance`   | `POST /v1/{collection}/{name}:{verb}` | `{Verb}{Singular}` | Resource is loaded and must exist before dispatch |
| `Collection` | `POST /v1/{collection}:{verb}`        | `{Verb}{Singular}` | No resource target |

The verb follows the colon in the HTTP path; the gRPC RPC is PascalCased and lives on the resource's existing
service (e.g. `JobService.RunJob`, not a separate service). A `ResourceMethodAttribute.Method` of
`ResourceHttpMethod.Get` routes a read-only method as `GET` with its request bound from the query string.

## Stages

`ResourceMethodOperationHandler<TEntity, TRequest, TResponse>.InvokeAsync(verb, name, request, principal, ct)`
runs the verb-scoped advisor pipeline, then dispatches the request through `IRequestDispatcher`. Before the
gate, it stashes `ResourceMethodVerb(verb)` on `AdviceContext`. The operation handler is the resource
pipeline's transport-facing root for authorization and target validation; the dispatcher invokes the
`IRequestHandler<TRequest, TResponse>` implementation with the request alone.

```
ResourceMethodController (HTTP) / ResourceCustomMethod (gRPC)
  -> ResourceMethodOperationHandler.InvokeAsync(verb, name, request, principal, ct)
       1. IResourceRequestAdvisor<TEntity>            gate; operation token is the verb itself
       2. IResourceMethodRequestAdvisor<TEntity, TRequest>   request stage
       3. (instance scope) load entity, then IResourceMethodAdvisor<TEntity, TRequest, TResponse>
       4. request.Principal = principal
          -> IRequestDispatcher.SendAsync<TRequest, TResponse>(request, ct)
                -> IRequestHandler<TRequest, TResponse>.HandleAsync(request, ct)
       5. IResourceResponseAdvisor<TEntity, TResponse>   response stage
```

A `Block` at any stage throws `NotFoundException` (`Blocked(name)`); a `Handle` returns a `TResponse` stashed in
`AdviceContext`. For an instance-scoped method the handler binds `request.CanonicalName = name` when the
request implements `ICanonicalName`, so the AIP-155 idempotency key distinguishes the same verb against
different resources, then loads the entity inside `_repository.SuppressQuerySoftDelete()`; a missing entity
throws `ResourceNotFound(name)`. A collection-scoped method (`name is null`) skips the load and the
method-advisor stage. After the advisors run, the operation handler assigns `request.Principal = principal`
and dispatches the request through `IRequestDispatcher.SendAsync<TRequest, TResponse>`; the dispatcher runs
any registered `ICommandAdvisor<TRequest>` / `IQueryAdvisor<TRequest>` (`Schemata.Messaging.Skeleton.Advisors`)
and then invokes the single registered `IRequestHandler<TRequest, TResponse>`.

### Built-in method advisors

| Advisor                          | Stage   | What it does                                                                                                     |
| -------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------------- |
| `AdviceMethodRequestAnonymous`   | request | Grants anonymous access when the verb is configured for it                                                       |
| `AdviceMethodRequestAuthorize`   | request | Applies row-level entitlement filtering, then authorizes with the verb as the permission token                   |
| `AdviceMethodRequestIdempotency` | request | Replays a cached response keyed by the verb and `RequestId`; registered per verb by `AddResource`                |
| `AdviceMethodEntityAuthorize`    | method  | Post-load AIP-211 check against the loaded entity (primary check + parent-read probe); order 100M (`Orders.Base`) |
| `AdviceMethodFreshness`          | method  | Validates the ETag against the target instance per AIP-154; runs after entity authorize at +10M                  |

`AdviceMethodRequestAuthorize` takes both `IAccessProvider<TEntity, TRequest>` and
`IEntitlementProvider<TEntity, TRequest>`: it first appends the entitlement expression to the
container via `container.ApplyWhere(...)` (row-level filtering, applied even for anonymous callers),
then runs the access check, skipping only that check when `AnonymousGranted` is present.
`AdviceMethodEntityAuthorize` runs on instance-scoped methods after the entity loads and skips when
`AnonymousGranted` is present.

## Extension points

- Add more `[ResourceMethod]` attributes, or supply `ResourceMethodAttribute` instances through
  `ResourceAttribute.Methods` when registering with `Use<...>()`. Each verb gets its own synthesized controller
  (HTTP) or RPC binding (gRPC).
- `ISoftDelete` entities automatically gain `:undelete`, `:expunge`, and collection-scoped `:purge`; declaring the
  same verb on the entity overrides the built-in, and the `Operations` whitelist can exclude them.
- Implement `IResourceMethodAdvisor<TEntity, TRequest, TResponse>` for verb-specific entity-stage logic, or
  `IResourceMethodRequestAdvisor<TEntity, TRequest>` for request-stage hooks. Register both as scoped via
  `TryAddEnumerable`.

## Design rationale

Custom methods ride the same advisor pipeline as CRUD so authorization, idempotency, and freshness apply
uniformly across both. The verb is carried on `AdviceContext` as `ResourceMethodVerb` so a single set of advisor
interfaces dispatches by verb without per-verb advisor types. HTTP and gRPC reuse the same handlers because the
verb-scoped stages depend only on `ClaimsPrincipal?` — never on `HttpContext` — keeping the handler unit-testable
without a web host. Each verb's own logic is a plain `IRequestHandler<TRequest, TResponse>`, dispatchable
through the same `IRequestDispatcher` that carries every other command or query, so cross-cutting messaging
advisors (`ICommandAdvisor` / `IQueryAdvisor`) apply uniformly.

## Caveats

- HTTP custom methods are `POST` unless `Method` is `ResourceHttpMethod.Get`. Send `{}` when the verb has no
  payload.
- gRPC RPC names are `{PascalVerb}{Singular}` (`RunJob`); avoid verbs that collide with the standard CRUD RPC
  names.
- `TResponse` must implement `ICanonicalName`. An `ICanonicalName` `TRequest` receives the instance name for
  AIP-155 request identification; other request types remain valid.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Delete Pipeline](delete-pipeline.md)
