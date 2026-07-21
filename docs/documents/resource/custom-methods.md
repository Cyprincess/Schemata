# Custom Methods

Custom methods add verbs to a resource that do not fit standard CRUD. They follow AIP-136 on both transports,
ride the same advisor pipeline as the CRUD operations, and carry the verb through `AdviceContext` as a
lowerCamelCase string.

## Where the code lives

| Package                        | Key files                                                                                                                                                                 |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Abstractions`        | `Resource/ResourceMethodAttribute.cs`, `Resource/ResourceMethodScope.cs`, `Resource/ResourceHttpMethod.cs`, `Resource/IResourceMethodHandler.cs`                          |
| `Schemata.Resource.Foundation` | `ResourceMethodOperationHandler.cs`, `Advisors/IResourceMethodRequestAdvisor.cs`, `Advisors/IResourceMethodAdvisor.cs`, `Advisors/ResourceMethodVerb.cs`                  |
| `Schemata.Resource.Foundation` | `Advisors/AdviceMethodRequestAnonymous.cs`, `Advisors/AdviceMethodRequestAuthorize.cs`, `Advisors/AdviceMethodRequestIdempotency.cs`, `Advisors/AdviceMethodEntityAuthorize.cs`, `Advisors/AdviceMethodFreshness.cs` |
| `Schemata.Resource.Http`       | `ResourceMethodController.cs`, `ResourceMethodControllerConvention.cs`, `ResourceMethodControllerFeatureProvider.cs`                                                      |
| `Schemata.Resource.Grpc`       | `ResourceCustomMethod.cs`, `ResourceMethodNaming.cs`                                                                                                                      |

## Declaring a custom method

```csharp
[Resource<Job, JobRequest, JobDetail, JobSummary>]
[ResourceMethod("run", typeof(RunJobHandler), ResourceMethodScope.Instance)]
[CanonicalName("jobs/{job}")]
public sealed class Job : IIdentifier, ICanonicalName, ITimestamp
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public Guid    Uid           { get; set; }
    public string? Status        { get; set; }
}

public sealed class RunJobHandler : IResourceMethodHandler<Job, RunJobRequest, RunJobResponse>
{
    public ValueTask<RunJobResponse> InvokeAsync(
        string?           name,
        RunJobRequest     request,
        Job?              entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct) {
        // ...
    }
}
```

`ResourceMethodAttribute(verb, handler, scope = Instance)` stores the verb (lowerCamelCase), the handler type, and
the scope, plus an optional `Method` (`ResourceHttpMethod`). `SchemataResourceFeature` discovers it during
assembly scanning, registers it in `SchemataResourceOptions.Methods` keyed by entity `RuntimeTypeHandle`,
registers the handler in DI, and reads the `TRequest`/`TResponse` types from the handler's
`IResourceMethodHandler<TEntity, TRequest, TResponse>` interface.

### Scope

| Scope        | HTTP route                            | gRPC RPC           | Entity passed to handler |
| ------------ | ------------------------------------- | ------------------ | ------------------------ |
| `Instance`   | `POST /v1/{collection}/{name}:{verb}` | `{Verb}{Singular}` | The loaded resource      |
| `Collection` | `POST /v1/{collection}:{verb}`        | `{Verb}{Singular}` | `null`                   |

The verb follows the colon in the HTTP path; the gRPC RPC is PascalCased and lives on the resource's existing
service (e.g. `JobService.RunJob`, not a separate service). A `ResourceMethodAttribute.Method` of
`ResourceHttpMethod.Get` routes a read-only method as `GET` with its request bound from the query string.

## Stages

`ResourceMethodOperationHandler<TEntity, TRequest, TResponse>.InvokeAsync` runs a verb-scoped pipeline that
mirrors the CRUD stages. Before the gate, it stashes `ResourceMethodVerb(verb)` on `AdviceContext`.

```
ResourceMethodController / ResourceCustomMethod
  -> ResourceMethodOperationHandler.InvokeAsync(handler, verb, name, request, principal, ct)
       1. IResourceRequestAdvisor<TEntity>            gate; operation token is the verb itself
       2. IResourceMethodRequestAdvisor<TEntity, TRequest>   request stage
       3. (instance scope) load entity, then IResourceMethodAdvisor<TEntity, TRequest, TResponse>
       4. handler.InvokeAsync(name, request, entity, principal, ct)
       5. IResourceResponseAdvisor<TEntity, TResponse>   response stage
```

A `Block` at any stage throws `NotFoundException` (`Blocked(name)`); a `Handle` returns a `TResponse` stashed in
`AdviceContext`. For an instance-scoped method the handler binds `request.CanonicalName = name` so the AIP-155
idempotency key distinguishes the same verb against different resources, then loads the entity inside
`SuppressQuerySoftDelete()`; a missing entity throws `ResourceNotFound(name)`. A collection-scoped method
(`name is null`) skips the load and the method-advisor stage, passing a `null` entity to the handler.

### Built-in method advisors

| Advisor                          | Stage   | What it does                                                                                                     |
| -------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------------- |
| `AdviceMethodRequestAnonymous`   | request | Grants anonymous access when the verb is configured for it                                                       |
| `AdviceMethodRequestAuthorize`   | request | Applies row-level entitlement filtering, then authorizes with the verb as the permission token                   |
| `AdviceMethodRequestIdempotency` | request | Replays a cached response keyed by the verb and `RequestId`                                                      |
| `AdviceMethodEntityAuthorize`    | method  | Post-load AIP-211 check against the loaded entity (primary check + parent-read probe); order 100M (`Orders.Base`) |
| `AdviceMethodFreshness`          | method  | Validates the ETag against the target instance per AIP-154; runs after entity authorize at +10M                  |

`AdviceMethodRequestAuthorize` takes both `IAccessProvider<TEntity, TRequest>` and
`IEntitlementProvider<TEntity, TRequest>`: it first appends the entitlement expression to the
container via `container.ApplyWhere(...)` (row-level filtering, applied even for anonymous callers),
then runs the access check, skipping only that check when `AnonymousGranted` is present.
`AdviceMethodEntityAuthorize` runs on instance-scoped methods after the entity loads and skips when
`AnonymousGranted` is present.

`AdviceMethodRequestIdempotency` and `AdviceMethodFreshness` are registered per verb by `RegisterResource`; the
anonymous and authorize advisors are added by `WithAuthorization()`.

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
without a web host.

## Caveats

- HTTP custom methods are `POST` unless `Method` is `ResourceHttpMethod.Get`. Send `{}` when the verb has no
  payload.
- gRPC RPC names are `{PascalVerb}{Singular}` (`RunJob`); avoid verbs that collide with the standard CRUD RPC
  names.
- `TRequest` and `TResponse` must implement `ICanonicalName`.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Delete Pipeline](delete-pipeline.md)
