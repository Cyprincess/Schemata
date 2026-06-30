# Scheduling gRPC Transport

The scheduling gRPC transport exposes the scheduler's two persistent entities — `SchemataJob`
(scheduled entries) and `SchemataJobExecution` (each fire) — as Schemata resources, plus three
AIP-136 custom methods: `Run` on a job, `Cancel` and `Wait` on an execution. The transport
inherits its service synthesis, routing, protobuf-net wire format, exception interceptor, and
reflection from the Resource gRPC transport; this feature only registers the resources and their
custom-method handlers. `MapGrpc()` on `SchedulingBuilder` activates `SchemataSchedulingGrpcFeature`
(priority `SchemataSchedulingFeature.DefaultPriority + 300_000` = `470_300_000`).

## Where the code lives

| Package                        | Key files                                                                                                                                                                                                      |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Grpc`     | `Features/SchemataSchedulingGrpcFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                                                         |
| `Schemata.Scheduling.Skeleton` | `RunJobHandler.cs`, `CancelOperationHandler.cs`, `WaitOperationHandler.cs`, `RunJobRequest.cs`, `WaitOperationRequest.cs`, `OperationMapper.cs`, `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs` |

## Activation

```csharp
schema.UseScheduling()
      .MapGrpc()
      .WithJob<HelloJob>("*/5 * * * *");
```

`MapGrpc()` adds `SchemataSchedulingGrpcFeature` and returns the same `SchedulingBuilder`, so job
registrations and transports chain. The feature declares `[DependsOn<SchemataSchedulingFeature>]`
and `[DependsOn<SchemataGrpcResourceFeature>]`, pulling in the scheduler and the resource gRPC
transport (code-first gRPC stack, exception interceptor, reflection, `RuntimeTypeModel`) when
missing.

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

`SchemataJob` carries `[DisplayName("Job")]`, so the Resource gRPC transport synthesizes a
`JobService` with the standard `ListJobs`, `GetJob`, `CreateJob`, `UpdateJob`, `DeleteJob` RPCs.
`SchemataJobExecution` is projected through `Operation` (`[DisplayName("Operation")]`), producing
an `OperationService` with `ListOperations`, `GetOperation`, `DeleteOperation`. With
`[ResourcePackage]` set, the prefix `{package}.` precedes each service name.

The closed `ResourceService<,,,>` implementations are mapped via `endpoints.MapGrpcService`. The
same `ResourceOperationHandler` runs under both HTTP and gRPC, so advisors and validation behave
identically across transports.

## Routing and method mapping

The Resource gRPC transport names custom-method RPCs `{PascalVerb}{Singular}`:

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
`Underscore()`. Payloads serialize with the same field names as the HTTP JSON.

Custom-method request bodies are the same Skeleton types as the HTTP transport:
`RunJobRequest`, `EmptyResourceRequest` (for `CancelOperation`), and `WaitOperationRequest`.

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
