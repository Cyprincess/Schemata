# Custom Methods

Custom methods add named resource verbs that do not fit standard CRUD. The HTTP and gRPC surfaces use the verb declared by `ResourceMethodAttribute`; the dispatcher carries that verb in `ResourceMethodRequest<TEntity,TRequest,TResponse>`.

## Declaring a method

```csharp
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

[Resource<Job, JobRequest, JobDetail, JobSummary>]
[ResourceMethod("run", typeof(RunJobHandler), ResourceMethodScope.Instance)]
[CanonicalName("jobs/{job}")]
public sealed class Job : ICanonicalName
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
}

public sealed class RunJobRequest : ICommand<RunJobResponse>, IRequestPrincipal, ICanonicalName
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed class RunJobResponse : ICanonicalName
{
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
}

public sealed class RunJobHandler : IRequestHandler<RunJobRequest, RunJobResponse>
{
    public Task<RunJobResponse> HandleAsync(RunJobRequest request, CancellationToken ct = default)
        => Task.FromResult(new RunJobResponse {
            Name = request.Name,
            CanonicalName = request.CanonicalName,
        });
}
```

`ResourceMethodAttribute` records a lower-camel-case verb, handler type, scope, and optional HTTP method. The handler must implement `IRequestHandler<TRequest,TResponse>` and the request must implement `IRequestPrincipal`. Instance methods also use `ICanonicalName` to receive their URI target.

| Scope | HTTP route | gRPC RPC |
| --- | --- | --- |
| Instance | `POST /v1/{collection}/{name}:{verb}` | `{Verb}{Singular}` |
| Collection | `POST /v1/{collection}:{verb}` | `{Verb}{Singular}` |

A `ResourceHttpMethod.Get` declaration uses a read-only HTTP route with query binding.

## Dispatch path

```text
ResourceMethodController (HTTP) / ResourceCustomMethod (gRPC)
  -> ResourceMethodOperationHandler.InvokeAsync(verb, name, request, principal, ct)
    -> IRequestDispatcher.SendAsync(ResourceMethodRequest<TEntity,TRequest,TResponse>)
      -> wrap pipeline: authentication, coarse authorization, response shaping, idempotency
        -> ResourceMethodDispatchHandler
          -> handler method request and entity advisor stages
            -> inner IRequestDispatcher.SendAsync(TRequest)
              -> IRequestHandler<TRequest,TResponse>
```

`ResourceMethodOperationHandler` writes the target canonical name to an `ICanonicalName` request before its Resource handler stages run. The instance handler loads the target under soft-delete suppression and throws `NotFoundException` if it is absent. Collection methods skip that load.

The envelope lets wrap advisors use the verb for `[Anonymous]` matching, permission resolution, and idempotency. The method idempotency wrap partitions its cache key by request ID, verb, resource type, principal, target, and payload hash. It replays a finalized response or reserves a pending key before calling the remainder of the pipeline; it commits the response after response shaping.

## Handler extension points

Implement `IResourceMethodRequestAdvisor<TEntity,TRequest>` for method request stages such as query shaping. Implement `IResourceMethodAdvisor<TEntity,TRequest,TResponse>` for logic that needs the loaded instance. Register either with `TryAddEnumerable`.

Authentication and coarse authorization run on the envelope. Instance access runs after an instance load, and entitlement expressions apply to the request container. Configure the shared security extensions on the domain builder; see [Security](../security.md).

## See also

- [Resource overview](overview.md)
- [HTTP transport](http-transport.md)
- [gRPC transport](grpc-transport.md)
- [Messaging](../messaging/overview.md)
