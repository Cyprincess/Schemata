# Flow gRPC Transport

`Schemata.Flow.Grpc` exposes process execution over gRPC. It registers `SchemataProcess`,
`SchemataProcessToken`, and `SchemataProcessTransition` as Schemata resources. Process operations
ride the standard Resource pipeline as AIP-136 custom methods, and the package maps a small code-first
service that lists registered process definitions. `MapGrpc()` activates `SchemataFlowGrpcFeature`
(priority `SchemataFlowFeature.DefaultPriority + 200_000` = `480_200_000`).

## Where the code lives

| Package                    | Key files                                                                                                                                                                                                                                                                                        |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Flow.Grpc`       | `Features/SchemataFlowGrpcFeature.cs`, `Services/IProcessDefinitionService.cs`, `Services/ProcessDefinitionService.cs`, `FlowProtoTypeContributor.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                               |
| `Schemata.Flow.Foundation` | `StartProcessHandler.cs`, `FlowStartProcessHandler.cs`, `CompleteActivityHandler.cs`, `CorrelateMessageHandler.cs`, `ThrowSignalHandler.cs`, `FlowPayloadHandlers.cs`, `TerminateProcessHandler.cs`, `CancelTokenHandler.cs`, `FlowResourceRegistration.cs`, `FlowRunner.cs`, `ProcessRegistry.cs`, `ProcessDefinitionQueryService.cs` |
| `Schemata.Flow.Skeleton`   | `Models/StartProcessInstanceRequest.cs`, `Models/CompleteActivityRequest.cs`, `Models/CorrelateMessageRequest.cs`, `Models/ThrowSignalRequest.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessToken.cs`, `Entities/SchemataProcessTransition.cs`, `Models/ProcessDefinitionInfo.cs` |

## Activation

`MapGrpc()` chains off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseFlow()
          .MapGrpc()
          .Use<OrderProcess>();
});
```

`SchemataFlowGrpcFeature` declares `[DependsOn<SchemataFlowFeature>]` and
`[DependsOn<SchemataGrpcResourceFeature>]`; the resource gRPC transport supplies the code-first
gRPC stack, the exception-mapping interceptor, server reflection, and the shared `RuntimeTypeModel`.

## Feature registration

`SchemataFlowGrpcFeature.ConfigureServices`:

1. Registers seven scoped services.
2. Registers three resources (`SchemataProcess`, `SchemataProcessToken`,
   `SchemataProcessTransition`) on the gRPC endpoint, the same registration as the HTTP feature.
3. Registers `ProcessDefinitionService` as scoped.
4. Registers `FlowProtoTypeContributor` as a singleton `IProtoTypeContributor`.

`ConfigureEndpoints` maps the definitions service via `endpoints.MapGrpcService<ProcessDefinitionService>()`.

`RegisterHandlers` registers `FlowSourceLoader`, `FlowStartProcessHandler`,
`CompleteActivityHandler`, `FlowCorrelateMessageHandler`, `FlowThrowSignalHandler`,
`TerminateProcessHandler`, and `CancelTokenHandler`. All seven live in `Schemata.Flow.Foundation`:
`FlowSourceLoader` and `FlowStartProcessHandler` are public, while `FlowCorrelateMessageHandler`
and `FlowThrowSignalHandler` are internal. Both transports call the same internal
`FlowResourceRegistration.RegisterHandlers` / `RegisterMethods`, so the gRPC and HTTP features wire
an identical handler set.

`SchemataProcess` carries `Operations.Get`, `Operations.List`, and five custom methods:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation;

resource.Operations = [Operations.Get, Operations.List];
resource.Methods = [
    new("start",     typeof(FlowStartProcessHandler),    ResourceMethodScope.Collection),
    new("complete",  typeof(CompleteActivityHandler)),
    new("correlate", typeof(FlowCorrelateMessageHandler)),
    new("signal",    typeof(FlowThrowSignalHandler),     ResourceMethodScope.Collection),
    new("terminate", typeof(TerminateProcessHandler)),
];
```

`SchemataProcessToken` carries `Operations.Get`, `Operations.List`, and one custom method:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Flow.Foundation;

resource.Operations = [Operations.Get, Operations.List];
resource.Methods    = [new("cancel", typeof(CancelTokenHandler))];
```

`SchemataProcessTransition` is registered read-only (`Get`, `List`).

## Service synthesis

The Resource gRPC transport synthesizes one `ResourceService<,,,>` per registered resource. The
service name comes from `GrpcResourceNaming.ServiceName` and the resource's `[DisplayName]`
singular: `SchemataProcess` (`[DisplayName("Process")]`) becomes `ProcessService`,
`SchemataProcessToken` (`[DisplayName("Token")]`) becomes `TokenService`, and
`SchemataProcessTransition` (`[DisplayName("Transition")]`) becomes `TransitionService`. The
synthesized services are mapped via `endpoints.MapGrpcService`; the same
`ResourceOperationHandler` runs under both HTTP and gRPC.

### Read RPCs

`Get` and `List` come from the resource registration, producing the standard read RPCs on each
service:

| Service             | List RPC          | Get RPC         |
| ------------------- | ----------------- | --------------- |
| `ProcessService`    | `ListProcesses`   | `GetProcess`    |
| `TokenService`      | `ListTokens`      | `GetToken`      |
| `TransitionService` | `ListTransitions` | `GetTransition` |

## Routing and method mapping

The Resource gRPC transport synthesizes one RPC per custom method on the parent service. Per
AIP-136, each RPC is named `{PascalVerb}{Singular}` (the singular comes from the resource's
`[DisplayName]`):

| Method      | Handler                       | Runtime call                  |
| ----------- | ----------------------------- | ----------------------------- |
| `start`     | `FlowStartProcessHandler`     | `FlowRunner.StartAsync`       |
| `complete`  | `CompleteActivityHandler`     | `FlowRunner.CompleteAsync`    |
| `correlate` | `FlowCorrelateMessageHandler` | `FlowRunner.CorrelateAsync`   |
| `signal`    | `FlowThrowSignalHandler`      | `FlowRunner.ThrowSignalAsync` |
| `terminate` | `TerminateProcessHandler`     | `FlowRunner.TerminateAsync`   |

`start` and `signal` are collection-scoped on `ProcessService`; `complete`, `correlate`, and
`terminate` are instance-scoped on `ProcessService`.

### Token RPCs

| Method   | Handler              | Runtime call                  |
| -------- | -------------------- | ----------------------------- |
| `cancel` | `CancelTokenHandler` | `FlowRunner.CancelTokenAsync` |

`cancel` is instance-scoped on `TokenService`.

Each handler implements `IResourceMethodHandler<TSummary, TRequest, TResponse>`; the gRPC and
HTTP transports invoke the same handler types over the same `InvokeAsync(name, request, entity,
principal, ct)` signature. The principal comes from the gRPC call's `HttpContext.User`.

`FlowStartProcessHandler` delegates source loading to `FlowSourceLoader`: the loader resolves the
optional `Source` canonical name through `IResourceTypeResolver`, checks the resolved type against
`IProcessRegistry.SourceTypes`, loads the entity through the registered `IRepository<TSource>`,
and calls `FlowRunner.StartAsync` with the typed source. When `Source` is empty, the handler calls
the no-source `StartAsync` overload.

### Definitions service

`ProcessDefinitionService` implements `IProcessDefinitionService`, the only hand-written service:

```csharp
public interface IProcessDefinitionService
{
    [Operation]
    ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request, CallContext context = default);
}
```

The method carries `[Operation]` from `ProtoBuf.Grpc.Configuration`. The implementation passes the
registry-backed `ProcessDefinitionQueryService.ListProcessDefinitions()` results through
unchanged; each entry has `CanonicalName = "definitions/{name}"`, plus `DisplayName` and
`Description` from the source `ProcessDefinition`.

## Request and response wire format

`FlowProtoTypeContributor` registers four summary types with the shared `RuntimeTypeModel`:
`ProcessDefinitionInfo`, `SchemataProcess`, `SchemataProcessToken`, and `SchemataProcessTransition`.
Wire names follow `ResourceWireNameRules` (`Name` dropped, `CanonicalName` → `name`) and then
`Humanizer.Underscore()` (snake_case). Custom-method request bodies ride the same wire shape as
the HTTP JSON: `StartProcessInstanceRequest` (`DefinitionName` / `DisplayName` / `Description` /
`Source` plus `ICanonicalName` + `IRequestIdentification`), `CompleteActivityRequest` (`Token`
plus `ICanonicalName`), `CorrelateMessageRequest` (`MessageName` / `Payload` / `Token` plus
`ICanonicalName`), `ThrowSignalRequest` (`SignalName` / `Payload` / `Token` plus `ICanonicalName`

- `IRequestIdentification`).

## Error mapping

`ExceptionMappingInterceptor` (registered by `SchemataTransportGrpcFeature`) wraps every flow RPC.
A `SchemataException` becomes a `Google.Rpc.Status` mapped through `RpcStatusBuilder.MapFromCanonical`
(`not_found` → `NotFound`, `failed_precondition` → `FailedPrecondition`, `invalid_argument` →
`InvalidArgument`, default `Internal`). Error details pack into `google.protobuf.Any` payloads and
ride the `grpc-status-details-bin` trailer.

## Reflection and metadata

`SchemataTransportGrpcFeature` maps `ReflectionServiceImpl` and `ReflectionV1ServiceImpl` for the
application; `ResourceGrpcServiceDescriptorContributor` contributes the synthesized
`ProcessService`, `TokenService`, and `TransitionService` descriptors. `FlowProtoTypeContributor`
contributes the four Flow summary types. Reflection-capable clients (e.g., `grpcurl`) see the
full schema.

## Extension points

- Add `[ResourceMethod]` advisors or replace a handler to change a verb's behavior; the methods ride
  the same Resource advisor pipeline as any custom method.
- Replace `ProcessDefinitionService` with a custom `IProcessDefinitionService` implementation.

## Caveats

- Process execution is resource-driven, not service-driven. The only hand-written gRPC service is the
  definitions lister; the verb and read RPCs are synthesized by the Resource transport.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [HTTP Transport](http.md)
- [Custom Methods](../resource/custom-methods.md)
