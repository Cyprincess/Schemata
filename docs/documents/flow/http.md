# Flow HTTP Transport

`Schemata.Flow.Http` exposes process execution over HTTP. It registers `SchemataProcess` and
`SchemataProcessTransition` as Schemata resources — so process operations ride the standard Resource
pipeline as AIP-136 custom methods — and adds a small controller that lists registered process
definitions. `MapHttp()` activates `SchemataFlowHttpFeature` (priority
`SchemataFlowFeature.DefaultPriority + 100_000` = `480_100_000`).

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Http` | `Features/SchemataFlowHttpFeature.cs`, `Controllers/ProcessDefinitionsController.cs`, `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `StartProcessHandler.cs`, `CompleteActivityHandler.cs`, `CorrelateMessageHandler.cs`, `ThrowSignalHandler.cs`, `TerminateProcessHandler.cs` |
| `Schemata.Flow.Foundation` | `ProcessRuntime.cs`, `ProcessRegistry.cs` |

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
2. Registers the five resource method handlers as scoped services.
3. Registers `SchemataProcess` and `SchemataProcessTransition` as resources on the HTTP endpoint.

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

## Routing and method mapping

### Process operations

The custom methods follow the AIP-136 colon convention. Collection-scoped verbs bind to the
collection; instance-scoped verbs bind to `{name}`:

| Method | HTTP | Handler | Runtime call |
| --- | --- | --- | --- |
| `start` | `POST ~/v1/processes:start` | `StartProcessHandler` | `StartProcessInstanceAsync` |
| `complete` | `POST ~/v1/processes/{name}:complete` | `CompleteActivityHandler` | `CompleteActivityAsync` |
| `correlate` | `POST ~/v1/processes/{name}:correlate` | `CorrelateMessageHandler` | `CorrelateMessageAsync` |
| `signal` | `POST ~/v1/processes:signal` | `ThrowSignalHandler` | `ThrowSignalAsync` |
| `terminate` | `POST ~/v1/processes/{name}:terminate` | `TerminateProcessHandler` | `TerminateProcessInstanceAsync` |

Each handler implements `IResourceMethodHandler<SchemataProcess, TRequest, TResponse>`. Its
`InvokeAsync(name, request, entity, principal, ct)` receives the resolved entity (null for
collection-scoped verbs), checks access via `FlowProcessAuthorization`, deserializes the request's
JSON `Variables`/`Payload`, and calls the runtime. `principal` is the request's `ClaimsPrincipal`.

### Read operations

The `Get` and `List` operations come from the resource registration, not a hand-written controller:

| HTTP | Action |
| --- | --- |
| `GET ~/v1/processes` | List process instances |
| `GET ~/v1/processes/{name}` | Get one instance |
| `GET ~/v1/processes/{name}/transitions` | List an instance's transitions |
| `GET ~/v1/processes/{name}/transitions/{transition}` | Get one transition |

### Definitions endpoint

`ProcessDefinitionsController` is the only hand-written controller. Mounted at
`~/v1/processes:definitions`, its single `GET` lists registered definition names from
`IProcessRegistry`, projecting each into a `ProcessDefinitionInfo` with
`CanonicalName = "definitions/{name}"`.

## Request and response wire format

The Resource HTTP transport's `SchemataJsonTraits` applies to the flow resources: `Name` is
dropped, `CanonicalName` serializes as `name`, snake_case is applied to remaining properties.
Custom-method requests live in `Schemata.Flow.Skeleton.Models`:

```csharp
public sealed class StartProcessInstanceRequest   // POST :start
{ public string DefinitionName; public string? DisplayName, Description, Variables; }

public sealed class CompleteActivityRequest        // POST {name}:complete
{ public string? Variables; }

public sealed class CorrelateMessageRequest        // POST {name}:correlate
{ public string MessageName; public string? Payload; }

public sealed class ThrowSignalRequest             // POST :signal
{ public string SignalName; public string? Payload; }
```

`Variables` and `Payload` are JSON strings rehydrated by `VariableSerializer.Deserialize` before the
runtime call. `:terminate` takes no body.

## Error mapping

The Resource HTTP transport's `UseExceptionHandler` covers every flow endpoint. Runtime errors
surface as the canonical AIP error model: `NotFoundException` for missing instances or
definitions, `FailedPreconditionException` for state-machine violations, `ValidationException` for
malformed `Variables`/`Payload` JSON, and 500 `Internal` for unmapped exceptions.

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

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [gRPC Transport](grpc.md)
- [Custom Methods](../resource/custom-methods.md)
