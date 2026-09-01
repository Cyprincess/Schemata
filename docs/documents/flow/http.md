# Flow HTTP Transport

`Schemata.Flow.Http` exposes process execution over HTTP. Its `MapHttp()` extension activates `SchemataFlowHttpFeature`; the feature's dependencies provide the shared Resource HTTP transport. Process verbs use `ResourceMethodRequest` envelopes, so dispatcher-wrap authentication and coarse authorization run before their Resource handler stages.

## Where the code lives

| Package                    | Key files                                                                                                                                                                                                                                                                                        |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Flow.Http`       | `Features/SchemataFlowHttpFeature.cs`, `Controllers/ProcessDefinitionsController.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                                                                                                |
| `Schemata.Flow.Foundation` | `StartProcessHandler.cs`, `FlowStartProcessHandler.cs`, `CompleteActivityHandler.cs`, `CorrelateMessageHandler.cs`, `ThrowSignalHandler.cs`, `TerminateProcessHandler.cs`, `CancelTokenHandler.cs`, `FlowResourceRegistration.cs`, `FlowRunner.cs`, `ProcessRegistry.cs`, `Commands/ListProcessDefinitionsQuery.cs`, `Handlers/DefaultListProcessDefinitionsHandler.cs` |
| `Schemata.Flow.Skeleton`   | `Models/StartProcessInstanceRequest.cs`, `Models/CompleteActivityRequest.cs`, `Models/CorrelateMessageRequest.cs`, `Models/ThrowSignalRequest.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessToken.cs`, `Entities/SchemataProcessTransition.cs`, `Models/ProcessDefinitionInfo.cs` |

## Activation

`MapHttp()` chains off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseControllers();
    schema.UseFlow()
          .MapHttp()
          .Use<OrderProcess>();
});
```

`SchemataFlowHttpFeature` declares `[DependsOn<SchemataFlowFeature>]` and
`[DependsOn<SchemataHttpResourceFeature>]`, so the flow runtime and the HTTP resource transport
(canonical-name wire rewrites, ETag projection, JSON traits) are pulled in when missing.

## Feature registration

`SchemataFlowHttpFeature.ConfigureServices`:

1. Adds the assembly containing the feature as an MVC `ApplicationPart` (via
   `AddSchemataApplicationPart<SchemataFlowHttpFeature>()`) so `ProcessDefinitionsController` is
   discovered. This bypasses the blanket `Schemata.*` assembly-part stripping.
2. Registers seven scoped services.
3. Registers three resources (`SchemataProcess`, `SchemataProcessToken`, `SchemataProcessTransition`)
   on the HTTP endpoint.

`FlowResourceRegistration.RegisterHandlers` registers `FlowSourceLoader`, `FlowStartProcessHandler`,
`CompleteActivityHandler`, `CorrelateMessageHandler`, `ThrowSignalHandler`,
`TerminateProcessHandler`, and `CancelTokenHandler`. The same Foundation type also holds the typed
operation and `ResourceMethodAttribute` facts consumed by both transport features, so HTTP and gRPC
use an identical handler set without a reflection-based `RegisterMethods` path.

`SchemataProcess` carries `Operations.Get`, `Operations.List`, and five custom methods:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation;

resource.Operations = [Operations.Get, Operations.List];
resource.Methods = [
    new("start",     typeof(FlowStartProcessHandler), ResourceMethodScope.Collection),
    new("complete",  typeof(CompleteActivityHandler)),
    new("correlate", typeof(CorrelateMessageHandler)),
    new("signal",    typeof(ThrowSignalHandler), ResourceMethodScope.Collection),
    new("terminate", typeof(TerminateProcessHandler)),
];
```

`SchemataProcessToken` carries `Operations.Get`, `Operations.List`, and the `cancel` custom method. `SchemataProcessTransition` is registered read-only (`Get`, `List`).


## Routing and method mapping

### Process operations

The custom methods follow the AIP-136 colon convention. Collection-scoped verbs bind to the
collection; instance-scoped verbs bind to `{name}`:

| Method      | HTTP                                   | Handler                       | Runtime call                  |
| ----------- | -------------------------------------- | ----------------------------- | ----------------------------- |
| `start`     | `POST ~/v1/processes:start`            | `FlowStartProcessHandler`     | `FlowRunner.StartAsync`       |
| `complete`  | `POST ~/v1/processes/{name}:complete`  | `CompleteActivityHandler`     | `FlowRunner.CompleteAsync`    |
| `correlate` | `POST ~/v1/processes/{name}:correlate` | `CorrelateMessageHandler` | `FlowRunner.CorrelateAsync`   |
| `signal`    | `POST ~/v1/processes:signal`           | `ThrowSignalHandler`      | `FlowRunner.ThrowSignalAsync` |
| `terminate` | `POST ~/v1/processes/{name}:terminate` | `TerminateProcessHandler`     | `FlowRunner.TerminateAsync`   |

Each handler implements `IRequestHandler<TRequest,TResponse>` for its dedicated wire request. `ResourceMethodOperationHandler` constructs the method envelope with the route target and `HttpContext.User`, then sends it through `IRequestDispatcher`. The Flow handler translates the inner request into the runner command.

`FlowStartProcessHandler` delegates source loading to `FlowSourceLoader`: the loader resolves the
optional `Source` canonical name through `IResourceTypeResolver`, checks the resolved type against
`IProcessRegistry.SourceTypes`, loads the entity through the registered `IRepository<TSource>`,
and calls `FlowRunner.StartAsync` with the typed source. When `Source` is empty, the handler calls
the no-source `StartAsync` overload.

### Token operations

| Method   | HTTP                                               | Handler              | Runtime call                  |
| -------- | -------------------------------------------------- | -------------------- | ----------------------------- |
| `cancel` | `POST ~/v1/processes/{name}/tokens/{token}:cancel` | `CancelTokenHandler` | `FlowRunner.CancelTokenAsync` |

`CancelTokenHandler` accepts `CancelTokenResourceRequest`, whose canonical name comes from the
route, and returns the post-cancel snapshot.

### Read operations

The `Get` and `List` operations come from the resource registration, not a hand-written controller:

| HTTP                                                 | Action                         |
| ---------------------------------------------------- | ------------------------------ |
| `GET ~/v1/processes`                                 | List process instances         |
| `GET ~/v1/processes/{name}`                          | Get one instance               |
| `GET ~/v1/processes/{name}/tokens`                   | List an instance's tokens      |
| `GET ~/v1/processes/{name}/tokens/{token}`           | Get one token                  |
| `GET ~/v1/processes/{name}/transitions`              | List an instance's transitions |
| `GET ~/v1/processes/{name}/transitions/{transition}` | Get one transition             |

### Definitions endpoint

`ProcessDefinitionsController` is the only hand-written controller. Mounted at
`~/v1/processes:definitions`, its single `GET` dispatches a
`ListProcessDefinitionsQuery` through `IQueryDispatcher` and returns the result unchanged. The
internal `DefaultListProcessDefinitionsHandler` reads the registry and projects the rows. Each row carries
`CanonicalName`, the four `IDescriptive` label fields, `messages` (declared message names with
their labels), and the definition graph:

- `elements` — `name`, shape `kind`, `scope` (enclosing sub-process, absent at the top level),
  event `position`, `trigger` and `trigger_kind` (the event definition's name and shape),
  `attached_to`, `interrupting`, `is_terminate`, `triggered_by_event`, `loop`, plus label fields.
- `flows` — `source` / `target` element names, `is_default`, `is_conditional`, plus label fields.
  The guard expression itself never crosses the wire.

Together the fields carry enough structure to rebuild the BPMN diagram from one list call.
The gRPC path exposes the same rows.

### Which messages a token accepts

The server does not project this. A client derives it from the definition graph it already
holds: look up the element named by the token's `state_name` / `waiting_at_name`, and when its
`kind` is `EventBasedGateway` follow `flows` from that name to the targets whose `position` is
`IntermediateCatch` and whose `trigger_kind` is `Message`, collecting their `trigger`; a token
waiting directly on such a catch accepts that one message. The graph says nothing about
authorization — intersect it with your own permission model.

## Request and response wire format

The Resource HTTP transport's `SchemataJsonTraits` applies to the flow resources: `Name` is
dropped, `CanonicalName` serializes as `name`, snake_case is applied to remaining properties.
Custom-method requests live in `Schemata.Flow.Skeleton.Models`:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

public sealed class StartProcessInstanceRequest : ICanonicalName, IRequestIdentification
{
    public string  DefinitionName { get; set; } = null!;
    public string? DisplayName    { get; set; }
    public string? Description    { get; set; }
    public string? Source         { get; set; }
    public string? Name           { get; set; }   // ICanonicalName
    public string? CanonicalName  { get; set; }   // ICanonicalName
    public string? RequestId      { get; set; }   // IRequestIdentification
}

public sealed class CompleteActivityRequest : ICanonicalName
{
    public string? Token         { get; set; }
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}

public sealed class CorrelateMessageRequest : ICanonicalName
{
    public string  MessageName   { get; set; } = null!;
    public string? Payload       { get; set; }
    public string? Token         { get; set; }
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}

public sealed class ThrowSignalRequest : ICanonicalName, IRequestIdentification
{
    public string  SignalName    { get; set; } = null!;
    public string? Payload       { get; set; }
    public string? Token         { get; set; }
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? RequestId     { get; set; }
}
```

`Source` is a canonical name bound to the started process. `Payload` carries the message or signal
body as JSON and is deserialized before the runtime call: messages use the process registration's
`MessagePayloadTypes`; signals resolve the payload type across every registered process and reject
a signal name that maps to more than one payload type.
`:terminate` and `:cancel` take no body.

## Error mapping

The Resource HTTP transport's `UseExceptionHandler` covers every flow endpoint. Runtime errors
surface as the canonical AIP error model: `NotFoundException` for missing instances, definitions,
or tokens; `FailedPreconditionException` for state-machine violations; `InvalidArgumentException`
for malformed `Payload` JSON or when a signal name has multiple payload types registered across
processes; and `Internal` for unmapped exceptions.

## Reflection and metadata

The MVC route table is the HTTP surface description. `ProcessDefinitionsController` provides a
runtime catalog of registered process definitions; there is no separate reflection endpoint.

## Extension points

- Register a closed `IRequestPipelineAdvisor<...>` for envelope-wide Flow method behavior, or use Resource method-stage advisors for behavior requiring the loaded target.
- `[Anonymous]` on the resource operation bypasses authentication and coarse authorization for that operation. Entitlement filtering remains active.

## Caveats

- Process execution is resource-driven, not controller-driven. The only controller is the
  definitions lister; the verbs and read endpoints are synthesized by the Resource transport.
- The definition graph describes the declared process. Boundary and event-sub-process
  subscriptions armed while a token is active are visible as elements but are not implied by the
  token's current wait.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [gRPC Transport](grpc.md)
- [Custom Methods](../resource/custom-methods.md)
