# Scheduling HTTP Transport

The scheduling HTTP transport exposes the scheduler's two persistent entities — `SchemataJob`
(scheduled entries) and `SchemataJobExecution` (each fire) — as Schemata resources, plus three
AIP-136 custom methods: `:run` on a job, `:cancel` and `:wait` on an execution. The transport
inherits its controller synthesis, routing, JSON wire format, and exception handler from the
Resource HTTP transport; this feature only registers the resources and their custom-method
handlers. `MapHttp()` on `SchedulingBuilder` activates `SchemataSchedulingHttpFeature` (priority
`SchemataSchedulingFeature.DefaultPriority + 200_000` = `470_200_000`).

## Where the code lives

| Package                        | Key files                                                                                                                                                                                                      |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Http`     | `Features/SchemataSchedulingHttpFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                                                         |
| `Schemata.Scheduling.Skeleton` | `RunJobHandler.cs`, `CancelOperationHandler.cs`, `WaitOperationHandler.cs`, `RunJobRequest.cs`, `WaitOperationRequest.cs`, `OperationMapper.cs`, `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs` |

## Activation

```csharp
schema.UseScheduling()
      .MapHttp()
      .WithJob<HelloJob>("*/5 * * * *");
```

`MapHttp()` adds `SchemataSchedulingHttpFeature` and returns the same `SchedulingBuilder`, so job
registrations and transports chain. The feature declares `[DependsOn<SchemataSchedulingFeature>]`
and `[DependsOn<SchemataHttpResourceFeature>]`, pulling in the scheduler and the resource HTTP
transport (canonical-name routing, JSON traits, exception handler) when missing.

## Feature registration

`SchemataSchedulingHttpFeature.ConfigureServices` performs two steps:

1. Registers the three custom-method handlers as scoped services — `RunJobHandler`,
   `CancelOperationHandler`, `WaitOperationHandler` — plus the mapper that projects
   `SchemataJobExecution` to the AIP-151 `Operation` DTO (`OperationMapper.FromExecution`).
2. Registers two resources tagged with `HttpResourceAttribute.Name`:
   - `SchemataJob` (entity = request = detail = summary), with the `:run` custom method on
     `RunJobHandler`.
   - `SchemataJobExecution` projected through the `Operation` DTO (request = detail = summary =
     `Operation`), with `Operations = [Get, List, Delete]` and the `:cancel` / `:wait` custom
     methods.

The resource registrations route through `SchemataResourceFeature`, so all routing, controller
synthesis, and convention rewriting happen in the Resource transport pipeline.

## Routing and method mapping

`SchemataJob` carries `[DisplayName("Job")]` and `[CanonicalName("jobs/{job}")]`, so the Resource
HTTP transport mounts it at `/v1/jobs`. `SchemataJobExecution` is projected through `Operation`,
which carries `[DisplayName("Operation")]` and `[CanonicalName("operations/{operation}")]`, mounting
at `/v1/operations`.

| Method   | Route                          | Action                                 | AIP     |
| -------- | ------------------------------ | -------------------------------------- | ------- |
| `GET`    | `/v1/jobs`                     | List jobs                              | AIP-132 |
| `POST`   | `/v1/jobs`                     | Create job                             | AIP-133 |
| `GET`    | `/v1/jobs/{name}`              | Get job                                | AIP-131 |
| `PATCH`  | `/v1/jobs/{name}`              | Update job                             | AIP-134 |
| `DELETE` | `/v1/jobs/{name}`              | Delete job                             | AIP-135 |
| `POST`   | `/v1/jobs/{name}:run`          | Trigger one fire → returns `Operation` | AIP-136 |
| `GET`    | `/v1/operations`               | List executions                        | AIP-132 |
| `GET`    | `/v1/operations/{name}`        | Get execution                          | AIP-131 |
| `DELETE` | `/v1/operations/{name}`        | Cancel + remove                        | AIP-135 |
| `POST`   | `/v1/operations/{name}:cancel` | Cancel a running execution             | AIP-136 |
| `POST`   | `/v1/operations/{name}:wait`   | Long-poll for terminal state           | AIP-136 |

`Operations` on `SchemataJobExecution` is set to `[Get, List, Delete]`; `Create` and `Update` are
not synthesized. The `:run` handler returns an `Operation` representing the queued execution; the
caller polls `/v1/operations/{name}` or calls `:wait` for terminal state.

## Request and response wire format

`SchemataTransportHttpFeature` (pulled in by the resource HTTP transport) configures
`SchemataJsonTraits.Apply`, which projects `ICanonicalName.Name` away, renames
`CanonicalName` to `name`, `IFreshness.EntityTag` to `etag`, and `IEntitiesResult<T>.Entities` to
the resource's plural (`jobs`, `operations`). Property names then go through snake_case via the
configured `PropertyNamingPolicy`.

Custom-method request bodies live in `Schemata.Scheduling.Skeleton`:

```csharp
public sealed class RunJobRequest        // POST /v1/jobs/{name}:run
{ public string? Variables; }

public sealed class WaitOperationRequest // POST /v1/operations/{name}:wait
{ public TimeSpan? Timeout; }
```

`:cancel` takes an `EmptyResourceRequest`. `Variables` is a JSON string deserialized by
`JobVariableSerializer` before the scheduler call.

## Error mapping

The Resource HTTP transport's `UseExceptionHandler` covers every scheduling endpoint. A
`SchemataException` writes `Response.StatusCode = ex.Code` with body
`ex.CreateErrorResponse(context.TraceIdentifier)`; any other exception wraps as a 500
`SchemataException(ErrorCodes.Internal)`. `WaitOperationHandler` responds 200 with the current
`Operation` — immediately when the execution is already terminal (succeeded, failed, or cancelled),
otherwise after polling up to its 30-second cap. A missing execution surfaces as 404
`NotFoundException` from the method pipeline's entity load.

## Reflection and metadata

The MVC route table is the HTTP surface description; there is no separate reflection endpoint.
`OPTIONS` is not synthesized.

## Extension points

- Register an `[ResourceMethod]`-style advisor against `SchemataJob` or the `Operation` projection
  to intercept `:run` / `:cancel` / `:wait`.
- Subclass `RunJobHandler`, `CancelOperationHandler`, or `WaitOperationHandler` and replace the
  registration before `MapHttp()` to change verb behavior.
- Add a hand-written controller for `SchemataJob` to bypass synthesized routes; the feature
  provider skips synthesis when a controller with the matching `Plural` is already present.

## Caveats

- The transport is unary. `:wait` is a server-side long-poll bounded by `WaitOperationRequest.Timeout`;
  use gRPC for streaming progress.
- A persistence provider (EF Core or LinqToDB) is required: `SchemataJob` and `SchemataJobExecution`
  are persisted entities. Without one, `:run` succeeds in memory but `/v1/operations/{name}` cannot
  return the row.

## See also

- [Scheduling Overview](overview.md)
- [gRPC Transport](grpc.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
- [Resource HTTP Transport](../resource/http-transport.md)
