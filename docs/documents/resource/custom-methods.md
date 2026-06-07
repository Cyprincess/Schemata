# Custom Methods

Custom methods extend a resource with verbs that do not fit the standard CRUD vocabulary. They mirror [Google AIP-136](https://google.aip.dev/136) semantics on both HTTP and gRPC, ride the same advisor pipeline as BREAD operations, and carry the AIP-136 verb token through `AdviceContext` as a lowerCamelCase string.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Abstractions` | `Resource/ResourceMethodAttribute.cs`, `Resource/ResourceMethodScope.cs`, `Resource/IResourceMethodHandler.cs`, `Entities/Operations.cs` (`Method` token) |
| `Schemata.Resource.Foundation` | `ResourceMethodOperationHandler.cs`, `Advisors/IResourceMethodAdvisor.cs`, `Advisors/IResourceMethodRequestAdvisor.cs`, `Advisors/AdviceMethodRequestIdempotency.cs`, `Advisors/AdviceMethodRequestAnonymous.cs`, `Advisors/AdviceMethodRequestAuthorize.cs`, `Advisors/AdviceMethodFreshness.cs`, `Advisors/ResourceMethodVerb.cs`, `Advisors/MethodIdempotencySuppressed.cs`, `Features/SchemataResourceFeature.cs` |
| `Schemata.Resource.Http` | `ResourceMethodController.cs`, `ResourceMethodControllerConvention.cs`, `ResourceMethodControllerFeatureProvider.cs` |
| `Schemata.Resource.Grpc` | `ResourceCustomMethod.cs`, `ResourceMethodNaming.cs` |

## Declaring a custom method

```csharp
[Resource<Job, JobRequest, JobDetail, JobSummary>]
[ResourceMethod("run", typeof(RunJobHandler), ResourceMethodScope.Instance)]
public sealed class Job : IIdentifier, ICanonicalName, ITimestamp, IConcurrency
{
    public string? Name          { get; set; }
    public string  CanonicalName { get; set; } = null!;
    public string? Status        { get; set; }
}

public sealed class RunJobHandler : IResourceMethodHandler<Job, RunJobRequest, RunJobResponse>
{
    public Task<RunJobResponse> HandleAsync(
        Job                entity,
        RunJobRequest      request,
        AdviceContext      ctx,
        CancellationToken  ct) {
        // ...
    }
}
```

`ResourceMethodAttribute` stores the (verb, handler type, scope) tuple. `SchemataResourceFeature.ConfigureServices` discovers it during assembly scanning and registers it in `SchemataResourceOptions.Methods` keyed by entity `RuntimeTypeHandle`. The handler's `IResourceMethodHandler<TEntity, TRequest, TResponse>` generic arguments are resolved from the handler type at registration time; request and response types for the method pipeline are inferred from those arguments.

### Scope

`ResourceMethodScope` controls whether the method targets a single instance or the collection:

| Scope | HTTP route | gRPC RPC pattern | Sample payload target |
| --- | --- | --- | --- |
| `Instance` | `POST ~/v1/{collection}/{name}:{verb}` | `{Verb}{Singular}` | One row |
| `Collection` | `POST ~/v1/{collection}:{verb}` | `{Verb}{Singular}` | Many rows / aggregate |

The HTTP path follows AIP-136: the verb is appended after a colon and is always `POST`. The gRPC RPC name is `PascalCase` and lives on the existing resource service (e.g., `Jobs.RunJob`, not a separate `JobRun` service); `google.api.http` mirrors the HTTP path so REST gateways can transcode the call.

## Pipeline

`ResourceMethodOperationHandler<TEntity, TRequest, TResponse>` runs a verb-scoped pipeline that mirrors the BREAD pipeline lanes, hard-coding the order of stages and letting `Order` decide within a stage.

```
HTTP / gRPC request
    |
    v
ResourceMethodController / ResourceCustomMethod
    |
    v
ResourceMethodOperationHandler(principal, request, payload, ct)
    |
    |-- AdviceContext primed with ResourceMethodVerb { Verb = "run" }
    |
    |-- IResourceMethodRequestAdvisor pipeline (Order lanes):
    |     50M  AdviceMethodRequestIdempotency  -- replay cached response keyed by Method
    |     80M  AdviceMethodRequestAnonymous    -- honor [Anonymous(Operations.Method)]
    |     100M AdviceMethodRequestAuthorize    -- IAccessProvider gate; verb is the permission token
    |     300M AdviceMethodFreshness           -- If-Match ETag against the target instance
    |
    |-- IResourceMethodAdvisor pipeline (application-level)
    |
    |-- IResourceMethodHandler<TEntity, TRequest, TResponse>.HandleAsync(...)
    |
    v
JsonResult / RPC response
```

### Per-stage notes

- **Idempotency**: keyed by `(Method, verb, RequestId)` so cache entries partition across Create, Update, and verb-scoped methods. `AdviceCreateRequestIdempotency`, `AdviceUpdateRequestIdempotency`, and `AdviceMethodRequestIdempotency` share the same `PendingIdempotencyKey` record but write disjoint cache keys: `idempotency\x1e{op}\x1e{requestId}`.
- **Anonymous**: `[Anonymous(Operations.Method)]` on the entity bypasses authorization for the verb. Without it, every call must satisfy `IAccessProvider`.
- **Authorization**: the permission token resolved by `IAccessProvider` is the verb itself (`"run"`, `"cancel"`, …). Wire your access provider to map verb → permission claim.
- **Freshness**: `If-Match` headers compare against the target instance's `EntityTag` when the entity implements `IFreshness`.

## Suppression markers

State markers under `Schemata.Resource.Foundation.Advisors` toggle individual lanes per request:

| Marker | Effect |
| --- | --- |
| `MethodIdempotencySuppressed` | Skip `AdviceMethodRequestIdempotency`. Use for non-idempotent verbs. |
| `UpdateIdempotencySuppressed` | Skip `AdviceUpdateRequestIdempotency` on the Update pipeline. |

Add markers to `AdviceContext` from an upstream `IResourceMethodRequestAdvisor` via `ctx.Set(new MethodIdempotencySuppressed())`.

## Extension points

- **Custom verbs**: add additional `[ResourceMethod]` attributes on the same entity. Each verb gets its own synthesized controller (HTTP) or RPC binding (gRPC).
- **Method advisors**: implement `IResourceMethodAdvisor<TEntity, TRequest, TResponse>` for verb-specific advisors that should fire after authorization. Register via `TryAddEnumerable` as scoped.
- **Method request advisors**: implement `IResourceMethodRequestAdvisor<TEntity>` for cross-verb request-stage hooks (e.g., rate limits, allow-list checks). Register via `TryAddEnumerable` as scoped.
- **Handlers**: implement `IResourceMethodHandler<TEntity, TRequest, TResponse>` for the verb body. The handler receives the resolved entity, the request payload, the `AdviceContext`, and the cancellation token.

## Caveats

- HTTP custom methods require `POST` per AIP-136. Send `{}` when the verb takes no payload.
- gRPC RPC names are `{PascalVerb}{Singular}` (`RunJob`). Avoid verbs that collide with the standard CRUD RPC names.
- The `Method` `Operations` token gates all custom methods on an entity uniformly. For per-verb gating, encode the verb in the permission token and check it inside `IAccessProvider`.

## See also

- [Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Create Pipeline](create-pipeline.md) — for the shared advisor lane numbers
- [Filtering](filtering.md)
