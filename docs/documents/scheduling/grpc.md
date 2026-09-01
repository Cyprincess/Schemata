# Scheduling gRPC Transport

The Scheduling gRPC transport exposes `SchemataJob` and `SchemataJobExecution` as resources. `MapGrpc()` is the concrete Scheduling extension that activates `SchemataSchedulingGrpcFeature`; its dependencies supply Resource gRPC behavior. Resource and method requests enter dispatcher wraps, including enabled authentication and coarse authorization, before Scheduling handler stages.

## Where the code lives

| Package                        | Key files                                                                                                                                                                                                      |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Grpc`     | `Features/SchemataSchedulingGrpcFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                                                         |
| `Schemata.Scheduling.Foundation` | `RunJobHandler.cs`, `CancelOperationHandler.cs`, `WaitOperationHandler.cs`                                                                                                                                     |
| `Schemata.Scheduling.Skeleton`   | `RunJobRequest.cs`, `WaitOperationRequest.cs`, `OperationMapper.cs`, `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`                                                                             |

## Activation

```csharp
schema.UseScheduling()
      .MapGrpc()
      .WithJob<HelloJob>("*/5 * * * *");
```

`MapGrpc()` activates `SchemataSchedulingGrpcFeature` and returns the same `SchedulingBuilder`. The feature depends on Scheduling and Resource gRPC features; those dependencies provide scheduling runtime and shared gRPC transport behavior.

## Feature registration

`SchemataSchedulingGrpcFeature.ConfigureServices` performs two steps:

1. Registers the three custom-method handlers as scoped services — `RunJobHandler`,
   `CancelOperationHandler`, `WaitOperationHandler` — plus the mapper that projects
   `SchemataJobExecution` to the AIP-151 `Operation` DTO (`OperationMapper.FromExecution`).
2. Registers two resources tagged with `GrpcResourceAttribute.Name`:
   - `SchemataJob` (entity = request = detail = summary), with the `Run` custom method on
     `RunJobHandler`.
   - `SchemataJobExecution` projected through the `Operation` DTO (request = detail = summary =
     `Operation`), with `Operations = [Get, List, Delete]` and the `Cancel` / `Wait` custom
     methods.

The resource registrations route through `SchemataResourceFeature`, so all service synthesis and
method binding happen in the Resource gRPC pipeline.

## Service synthesis

`SchemataJob` carries `[CanonicalName("jobs/{job}")]`, so the Resource gRPC transport synthesizes a
`JobService` with the standard `ListJobs`, `GetJob`, `CreateJob`, `UpdateJob`, `DeleteJob` RPCs.
`SchemataJobExecution` carries `[CanonicalName("operations/{operation}")]` and is projected through
`Operation`, producing an `OperationService` with `ListOperations`, `GetOperation`,
`DeleteOperation`. With
`[ResourcePackage]` set, the prefix `{package}.` precedes each service name.

The closed `ResourceService<,,,>` implementations are mapped via `endpoints.MapGrpcService`. The
same `ResourceOperationHandler` runs under both HTTP and gRPC, so advisors and validation behave
identically across transports.

## Routing and method mapping

The Resource gRPC transport names custom-method RPCs `{PascalVerb}{Singular}` from the resource
descriptor. This is a Schemata naming convention; AIP-136 specifies a verb followed by a noun and
the colon convention for HTTP custom-method URIs.

| Service            | RPC                                                             | Handler                  |
| ------------------ | --------------------------------------------------------------- | ------------------------ |
| `JobService`       | `ListJobs` / `GetJob` / `CreateJob` / `UpdateJob` / `DeleteJob` | synthesized              |
| `JobService`       | `RunJob`                                                        | `RunJobHandler`          |
| `OperationService` | `ListOperations` / `GetOperation` / `DeleteOperation`           | synthesized              |
| `OperationService` | `CancelOperation`                                               | `CancelOperationHandler` |
| `OperationService` | `WaitOperation`                                                 | `WaitOperationHandler`   |

`Operations` on the execution resource is set to `[Get, List, Delete]`; `Create` and `Update` are
not exposed. `RunJob` returns an `Operation` representing the queued execution; the caller polls
`GetOperation` or calls `WaitOperation` for the terminal state.

## Request and response wire format

`SchemataProtoModelConfigurator` (in the resource gRPC transport) adds each request, detail,
summary, and `ListResultBase<TSummary>` to the shared `RuntimeTypeModel`. Wire names follow the
same `ResourceWireNameRules` aliases as HTTP (`Name` dropped, `CanonicalName` → `name`,
`EntityTag` → `etag`, `Entities` → plural), then go through snake_case via Humanizer
`Underscore()`. Payloads serialize with the same field names as the HTTP JSON. Scalar-keyed
`Dictionary<string, string?>` values are registered as proto3 maps. A null value is written as a
key-only entry and a proto3 reader materializes that entry as an empty string.

Custom-method request bodies are the same types as the HTTP transport:
`RunJobRequest`, `CancelOperationRequest`, and `WaitOperationRequest`. Each implements
`IRequestPrincipal`; the resource method pipeline binds the gRPC call's `HttpContext.User` before
dispatch.

## Error mapping

`ExceptionMappingInterceptor` (registered by `SchemataTransportGrpcFeature`) wraps every unary
call. A `SchemataException` becomes a `Google.Rpc.Status` mapped through
`RpcStatusBuilder.MapFromCanonical` (`not_found` → `NotFound`, `failed_precondition` →
`FailedPrecondition`, default `Internal`). Error details pack into `google.protobuf.Any` payloads
and ride the `grpc-status-details-bin` trailer. `WaitOperation` returning before the deadline
surfaces the current `Operation`; a cancelled or expunged execution surfaces as `NotFound`.

## Reflection and metadata

`SchemataTransportGrpcFeature` maps `ReflectionServiceImpl` (v1alpha) and `ReflectionV1ServiceImpl`
(v1) once for the application; `ResourceGrpcServiceDescriptorContributor` adds the closed
`ResourceService<,,,>` types into the reflection schema. `FileDescriptorBridge` builds a `proto3`
file descriptor per resource — `job_service.proto`, `operation_service.proto` — covering the
standard and custom RPCs. `grpcurl -plaintext localhost:5000 list` shows both services.

## Extension points

- Register an `[ResourceMethod]`-style advisor against `SchemataJob` or the `Operation` projection
  to intercept `RunJob` / `CancelOperation` / `WaitOperation`.
- Subclass any handler and replace the registration before `MapGrpc()` to change verb behavior.
- Implement `IResourceService<SchemataJob, ...>` directly and map it through
  `endpoints.MapGrpcService<MyService>()` to bypass synthesis.
- `[ResourcePackage("scheduler")]` on `SchemataJob` sets the gRPC service-name prefix.

## Caveats

- `ExceptionMappingInterceptor` is required for status mapping; without it every exception
  surfaces as `INTERNAL`.
- A persistence provider (EF Core or LinqToDB) is required: `SchemataJob` and `SchemataJobExecution`
  are persisted entities. Without one, `RunJob` succeeds in memory but `GetOperation` cannot return
  the row.

## See also

- [Scheduling Overview](overview.md)
- [HTTP Transport](http.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
- [Resource gRPC Transport](../resource/grpc-transport.md)
