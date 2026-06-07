# Flow gRPC Transport

`Schemata.Flow.Grpc` exposes `IProcessRuntime` as a code-first gRPC service. Calling `UseFlowGrpc()` on `SchemataBuilder` registers `SchemataFlowGrpcFeature` (Priority `SchemataFlowFeature.DefaultPriority + 200_000` = 480,200,000), which maps `ProcessService` via `endpoints.MapGrpcService<ProcessService>()`.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Flow.Grpc` | `Features/SchemataFlowGrpcFeature.cs` |
| `Schemata.Flow.Grpc` | `Services/IProcessService.cs`, `Services/ProcessService.cs` |
| `Schemata.Flow.Grpc` | `FlowProtoTypeContributor.cs` |
| `Schemata.Flow.Grpc` | `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Flow.Foundation` | `ProcessRuntime.cs`, `ProcessRegistry.cs` |

## Setup

```csharp
builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseFlow(flow => flow.Use<OrderProcess>())
          .UseFlowGrpc();
});
```

`UseFlowGrpc()` adds `SchemataFlowGrpcFeature`. The feature declares `[DependsOn<SchemataFlowFeature>]` and `[DependsOn<SchemataTransportGrpcFeature>]`; the transport feature supplies `AddCodeFirstGrpc`, the `ExceptionMappingInterceptor`, gRPC server reflection, and `RuntimeTypeModel` configuration shared with `Schemata.Resource.Grpc`.

## `SchemataFlowGrpcFeature`

`ConfigureServices` registers:

- `ProcessService` as a scoped service.
- `FlowProtoTypeContributor` as a singleton `IProtoTypeContributor`, which feeds the flow message types (`SchemataProcess`, `ProcessInstance`, the request DTOs) into the shared `RuntimeTypeModel` configured by `SchemataTransportGrpcFeature`.

`ConfigureEndpoints` calls `endpoints.MapGrpcService<ProcessService>()`.

## `IProcessService` contract

```csharp
public interface IProcessService
{
    [Operation] ValueTask<SchemataProcess> StartProcessInstanceAsync(
        StartProcessInstanceRequest request, CallContext context = default);

    [Operation] ValueTask<ProcessInstance> CompleteActivityAsync(
        CompleteActivityRequest request, CallContext context = default);

    [Operation] ValueTask<ProcessInstance> CorrelateMessageAsync(
        CorrelateMessageRequest request, CallContext context = default);

    [Operation] ValueTask ThrowSignalAsync(
        ThrowSignalRequest request, CallContext context = default);

    [Operation] ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        TerminateProcessInstanceRequest request, CallContext context = default);

    [Operation] ValueTask<SchemataProcess?> GetProcessInstanceAsync(
        GetProcessInstanceRequest request, CallContext context = default);

    [Operation] ValueTask<ListProcessInstancesResponse> ListProcessInstancesAsync(
        ListProcessInstancesRequest request, CallContext context = default);

    [Operation] ValueTask<SchemataProcessTransition?> GetProcessInstanceTransitionAsync(
        GetProcessInstanceTransitionRequest request, CallContext context = default);

    [Operation] ValueTask<ListProcessInstanceTransitionsResponse> ListProcessInstanceTransitionsAsync(
        ListProcessInstanceTransitionsRequest request, CallContext context = default);

    [Operation] ValueTask<ListProcessDefinitionsResponse> ListProcessDefinitionsAsync(
        ListProcessDefinitionsRequest request, CallContext context = default);
}
```

Each method carries `[Operation]` from `ProtoBuf.Grpc.Configuration`. The interface intentionally has no `[Service]` attribute because the registration path is explicit `MapGrpcService<ProcessService>()` in `ConfigureEndpoints`, not protobuf-net's attribute-driven auto-discovery.

## `ProcessService`

`ProcessService` implements `IProcessService` and delegates to `ProcessRuntime`. It pulls the `ClaimsPrincipal` from `IHttpContextAccessor.HttpContext?.User`; request DTOs carry `Variables` and `Payload` as JSON strings that `VariableSerializer.Deserialize` rehydrates before the runtime call.

## Extension points

- Implement `IProcessService` with a custom class and register it instead of `ProcessService` to add authorisation, response shaping, or extra validation.
- Add a gRPC interceptor via `services.AddGrpc(o => o.Interceptors.Add<MyInterceptor>())`.

## Caveats

- `IHttpContextAccessor` is registered by `SchemataTransportGrpcFeature`. Running `ProcessService` outside that feature requires registering the accessor manually.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [HTTP Transport](http.md)
- [Resource gRPC Transport](../resource/grpc-transport.md)
