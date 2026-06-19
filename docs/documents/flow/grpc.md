# Flow gRPC Transport

`Schemata.Flow.Grpc` exposes process execution over gRPC. It registers `SchemataProcess` and
`SchemataProcessTransition` as Schemata resources — so process operations ride the standard Resource
pipeline as AIP-136 custom methods — and maps a small code-first service that lists registered
process definitions. `MapGrpc()` activates `SchemataFlowGrpcFeature` (priority
`SchemataFlowFeature.DefaultPriority + 200_000` = `480_200_000`).

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Grpc` | `Features/SchemataFlowGrpcFeature.cs`, `Services/IProcessDefinitionService.cs`, `Services/ProcessDefinitionService.cs`, `FlowProtoTypeContributor.cs`, `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `StartProcessHandler.cs`, `CompleteActivityHandler.cs`, `CorrelateMessageHandler.cs`, `ThrowSignalHandler.cs`, `TerminateProcessHandler.cs` |
| `Schemata.Flow.Foundation` | `ProcessRuntime.cs`, `ProcessRegistry.cs` |

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
`[DependsOn<SchemataGrpcResourceFeature>]`; the resource gRPC transport supplies the code-first gRPC
stack, the exception-mapping interceptor, server reflection, and the shared `RuntimeTypeModel`.

## Feature registration

`SchemataFlowGrpcFeature.ConfigureServices`:

1. Registers the five resource method handlers as scoped services.
2. Registers `SchemataProcess` and `SchemataProcessTransition` as resources on the gRPC endpoint —
   the same registration as the HTTP feature.
3. Registers `ProcessDefinitionService` as scoped.
4. Registers `FlowProtoTypeContributor` as a singleton `IProtoTypeContributor`, contributing
   `ProcessDefinitionInfo` to the shared protobuf model.

`ConfigureEndpoints` maps the definitions service via `endpoints.MapGrpcService<ProcessDefinitionService>()`.

`SchemataProcess` is registered with `Operations.Get` and `Operations.List` plus five custom methods:

```csharp
resource.Operations = [Operations.Get, Operations.List];
resource.Methods = [
    new("start",     typeof(StartProcessHandler),    ResourceMethodScope.Collection),
    new("complete",  typeof(CompleteActivityHandler)),                 // Instance
    new("correlate", typeof(CorrelateMessageHandler)),                 // Instance
    new("signal",    typeof(ThrowSignalHandler),     ResourceMethodScope.Collection),
    new("terminate", typeof(TerminateProcessHandler)),                 // Instance
];
```

`SchemataProcessTransition` is registered read-only (`Get`, `List`).

## Service synthesis

The Resource gRPC transport synthesizes a `ProcessService` from the `SchemataProcess` registration
with `ListProcesses` and `GetProcess` RPCs. `SchemataProcessTransition` produces a separate
`ProcessTransitionService` with `ListProcessTransitions` and `GetProcessTransition`. The closed
`ResourceService<,,,>` types are mapped via `endpoints.MapGrpcService`; the same
`ResourceOperationHandler` runs under both HTTP and gRPC.

## Routing and method mapping

The Resource gRPC transport synthesizes one RPC per custom method on the `ProcessService`.
Per the AIP-136 binding, each RPC is named `{PascalVerb}{Singular}` (the singular comes from the
resource's `[DisplayName("Process")]`):

| Method | Handler | Runtime call |
| --- | --- | --- |
| `start` | `StartProcessHandler` | `StartProcessInstanceAsync` |
| `complete` | `CompleteActivityHandler` | `CompleteActivityAsync` |
| `correlate` | `CorrelateMessageHandler` | `CorrelateMessageAsync` |
| `signal` | `ThrowSignalHandler` | `ThrowSignalAsync` |
| `terminate` | `TerminateProcessHandler` | `TerminateProcessInstanceAsync` |

Each handler implements `IResourceMethodHandler<SchemataProcess, TRequest, TResponse>`; the gRPC and
HTTP transports invoke the same handler types over the same `InvokeAsync(name, request, entity,
principal, ct)` signature. The principal comes from the gRPC call's `HttpContext.User`.

### Read RPCs

`Get` and `List` come from the resource registration, producing the standard read RPCs on the
`ProcessService` and `ProcessTransitionService`.

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

The method carries `[Operation]` from `ProtoBuf.Grpc.Configuration`. The implementation reads the
registry, projecting each registered name into a `ProcessDefinitionInfo` with
`CanonicalName = "definitions/{name}"`, `DisplayName`, and `Description` from the definition.

## Request and response wire format

`FlowProtoTypeContributor` registers `ProcessDefinitionInfo` and the custom-method request and
response types with the shared `RuntimeTypeModel`. Wire names follow `ResourceWireNameRules`
(`Name` dropped, `CanonicalName` → `name`) and then snake_case via Humanizer `Underscore()`.
Payloads serialize with the same field names as the HTTP JSON.

## Error mapping

`ExceptionMappingInterceptor` (registered by `SchemataTransportGrpcFeature`) wraps every flow RPC.
A `SchemataException` becomes a `Google.Rpc.Status` mapped through `RpcStatusBuilder.MapFromCanonical`
(`not_found` → `NotFound`, `failed_precondition` → `FailedPrecondition`, default `Internal`). Error
details pack into `google.protobuf.Any` payloads and ride the `grpc-status-details-bin` trailer.

## Reflection and metadata

`SchemataTransportGrpcFeature` maps `ReflectionServiceImpl` and `ReflectionV1ServiceImpl` for the
application; `ResourceGrpcServiceDescriptorContributor` contributes the synthesized `ProcessService`
and `ProcessTransitionService`. `FlowProtoTypeContributor` contributes `ProcessDefinitionInfo`.
Reflection-capable clients (e.g., `grpcurl`) see the full schema.

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
