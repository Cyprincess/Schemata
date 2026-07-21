# Flow HTTP Transport

`Schemata.Flow.Http` exposes process execution over HTTP. It registers `SchemataProcess`,
`SchemataProcessToken`, and `SchemataProcessTransition` as Schemata resources. Process operations
ride the standard Resource pipeline as AIP-136 custom methods, and the package adds a small
controller that lists registered process definitions. `MapHttp()` activates
`SchemataFlowHttpFeature` (priority
`SchemataFlowFeature.DefaultPriority + 100_000` = `480_100_000`).

## Where the code lives

| Package                    | Key files                                                                                                                                                                                                                                                                                        |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Flow.Http`       | `Features/SchemataFlowHttpFeature.cs`, `Controllers/ProcessDefinitionsController.cs`, `Internal/FlowHttpPayloadHandlers.cs`, `Internal/FlowHttpStartProcessHandler.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                |
| `Schemata.Flow.Foundation` | `StartProcessHandler.cs`, `CompleteActivityHandler.cs`, `CorrelateMessageHandler.cs`, `ThrowSignalHandler.cs`, `TerminateProcessHandler.cs`, `CancelTokenHandler.cs`, `FlowRunner.cs`, `ProcessRegistry.cs`, `ProcessDefinitionQueryService.cs`                                                  |
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

`RegisterHandlers` registers `FlowHttpSourceLoader`, `FlowHttpStartProcessHandler`,
`CompleteActivityHandler`, `FlowHttpCorrelateMessageHandler`, `FlowHttpThrowSignalHandler`,
`TerminateProcessHandler`, and `CancelTokenHandler`. The Flow-prefixed types live in
`Schemata.Flow.Http.Internal`; the three unprefixed handlers (`CompleteActivityHandler`,
`TerminateProcessHandler`, `CancelTokenHandler`) live in `Schemata.Flow.Foundation` and are reused
unchanged across transports.

`SchemataProcess` carries `Operations.Get`, `Operations.List`, and five custom methods:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation;
using Schemata.Flow.Http.Internal;

resource.Operations = [Operations.Get, Operations.List];
resource.Methods = [
    new("start",     typeof(FlowHttpStartProcessHandler),    ResourceMethodScope.Collection),
    new("complete",  typeof(CompleteActivityHandler)),
    new("correlate", typeof(FlowHttpCorrelateMessageHandler)),
    new("signal",    typeof(FlowHttpThrowSignalHandler),     ResourceMethodScope.Collection),
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

## Routing and method mapping

### Process operations

The custom methods follow the AIP-136 colon convention. Collection-scoped verbs bind to the
collection; instance-scoped verbs bind to `{name}`:

| Method      | HTTP                                   | Handler                           | Runtime call                  |
| ----------- | -------------------------------------- | --------------------------------- | ----------------------------- |
| `start`     | `POST ~/v1/processes:start`            | `FlowHttpStartProcessHandler`     | `FlowRunner.StartAsync`       |
| `complete`  | `POST ~/v1/processes/{name}:complete`  | `CompleteActivityHandler`         | `FlowRunner.CompleteAsync`    |
| `correlate` | `POST ~/v1/processes/{name}:correlate` | `FlowHttpCorrelateMessageHandler` | `FlowRunner.CorrelateAsync`   |
| `signal`    | `POST ~/v1/processes:signal`           | `FlowHttpThrowSignalHandler`      | `FlowRunner.ThrowSignalAsync` |
| `terminate` | `POST ~/v1/processes/{name}:terminate` | `TerminateProcessHandler`         | `FlowRunner.TerminateAsync`   |

Each handler implements `IResourceMethodHandler<SchemataProcess, TRequest, TResponse>`. Its
`InvokeAsync(name, request, entity, principal, ct)` receives the resolved entity (null for
collection-scoped verbs), resolves source or payload types through the registry, and calls the
runner. `principal` is the request's `ClaimsPrincipal`; authorization runs earlier in the resource
advisor pipeline when the host enables `WithAuthorization` on the resource builder.

`FlowHttpStartProcessHandler` resolves the optional `Source` canonical name through
`IResourceTypeResolver` and `IProcessRegistry.SourceTypes`, loads the entity through the registered
`IRepository<TSource>`, and calls `FlowRunner.StartAsync` with the typed source. When `Source` is
empty, it calls the no-source `StartAsync` overload.

### Token operations

| Method   | HTTP                                               | Handler              | Runtime call                  |
| -------- | -------------------------------------------------- | -------------------- | ----------------------------- |
| `cancel` | `POST ~/v1/processes/{name}/tokens/{token}:cancel` | `CancelTokenHandler` | `FlowRunner.CancelTokenAsync` |

`CancelTokenHandler` takes an empty request body (`EmptyResourceRequest`) and returns the
post-cancel snapshot.

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
`~/v1/processes:definitions`, its single `GET` lists registered definition names through
`ProcessDefinitionQueryService`. The controller re-projects each query result into a new
`ProcessDefinitionInfo` with `CanonicalName` populated and `DisplayName` / `Description` left
unset, so the HTTP response carries the canonical name only. The gRPC path passes the query
service's output through unchanged and exposes all three fields.

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
body and is deserialized through the registered payload type map (`reg.MessagePayloadTypes` for
messages, the matching `reg.SignalPayloadTypes[SignalName]` for signals) before the runtime call.
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

- Add `[ResourceMethod]` advisors or replace a handler to change a verb's behavior; the methods ride
  the same Resource advisor pipeline as any custom method.
- Add `[Anonymous(Operations.Method)]` on `SchemataProcess` in your own derived registration to
  bypass authorization for the verbs.

## Caveats

- Process execution is resource-driven, not controller-driven. The only controller is the
  definitions lister; the verbs and read endpoints are synthesized by the Resource transport.
- The HTTP definitions endpoint returns `ProcessDefinitionInfo` rows with only `CanonicalName`
  populated; the gRPC endpoint exposes `DisplayName` and `Description` as well.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [gRPC Transport](grpc.md)
- [Custom Methods](../resource/custom-methods.md)
