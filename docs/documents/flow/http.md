# Flow HTTP Transport

`Schemata.Flow.Http` exposes `IProcessRuntime` as a REST surface. Calling `UseFlowHttp()` on `SchemataBuilder` registers `SchemataFlowHttpFeature` (Priority `SchemataFlowFeature.DefaultPriority + 100_000` = 480,100,000), which adds the assembly containing `ProcessController` as an `ApplicationPart` so MVC picks it up.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Flow.Http` | `Features/SchemataFlowHttpFeature.cs` |
| `Schemata.Flow.Http` | `Controllers/ProcessController.cs` |
| `Schemata.Flow.Http` | `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Flow.Foundation` | `ProcessRuntime.cs`, `ProcessRegistry.cs` |

## Setup

```csharp
builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseControllers();
    schema.UseFlow(flow => flow.Use<OrderProcess>())
          .UseFlowHttp();
});
```

`UseFlowHttp()` adds `SchemataFlowHttpFeature`. The feature declares `[DependsOn<SchemataFlowFeature>]` and `[DependsOn<SchemataTransportHttpFeature>]`, so the flow runtime and the shared HTTP transport plumbing (canonical-name wire rewrites, `ETag` projection, JSON traits) are auto-pulled when either is missing.

## `SchemataFlowHttpFeature`

`ConfigureServices` is empty: the feature exists for ordering, dependency resolution, and as the marker type passed to `SchemataBuilder.AddSchemataApplicationPart<SchemataFlowHttpFeature>()` inside `UseFlowHttp`. The extension method adds the assembly containing `SchemataFlowHttpFeature` (which is the assembly that also contains `ProcessController`) as an `ApplicationPart` so MVC discovers the controller. This bypasses the blanket `Schemata.*` assembly-part stripping performed by `SchemataControllersFeature`.

## `ProcessController`

`ProcessController` is mounted at `~/processes` and projects `IProcessRuntime` onto five custom methods plus four read endpoints.

| HTTP method | Route | Action |
|---|---|---|
| `POST` | `/processes` | Start a new process instance |
| `POST` | `/processes/{name}:complete` | Complete the current activity and auto-advance |
| `POST` | `/processes/{name}:correlate` | Correlate a named message to the instance |
| `POST` | `/processes/:throw` | Broadcast a signal to all waiting instances |
| `POST` | `/processes/{name}:terminate` | Terminate a process instance |
| `GET` | `/processes` | List process instances |
| `GET` | `/processes/{name}` | Get a single instance by name segment |
| `GET` | `/processes/{name}/transitions` | List transition history for an instance |
| `GET` | `/processes/{name}/transitions/{transition}` | Get a single transition record |
| `GET` | `/processes/:definitions` | List registered process definition names |

The colon-prefixed paths follow the AIP-136 custom-method convention.

The controller resolves the canonical name by prepending `"processes/"` to the `{name}` route segment, then forwards to `ProcessRuntime`. `HttpContext.User` is passed as the `ClaimsPrincipal`.

## Request bodies

```csharp
// POST /processes
public sealed class StartProcessInstanceRequest
{
    public string  DefinitionName { get; set; }
    public string? DisplayName    { get; set; }
    public string? Description    { get; set; }
    public string? Variables      { get; set; }  // JSON
}

// POST /processes/{name}:complete
public sealed class CompleteActivityRequest
{
    public string? Variables { get; set; }       // JSON, merged into instance
}

// POST /processes/{name}:correlate
public sealed class CorrelateMessageRequest
{
    public string  MessageName { get; set; }
    public string? Payload     { get; set; }     // JSON
}

// POST /processes/:throw
public sealed class ThrowSignalRequest
{
    public string  SignalName { get; set; }
    public string? Payload    { get; set; }      // JSON
}
```

## Extension points

- Subclass `ProcessController` and override individual actions to add authorisation, response shaping, or extra validation.
- Subclass and reach a richer `ProcessRuntime` overload directly — `ProcessRuntime.StartProcessInstanceAsync` accepts `displayName` and `description` arguments that the `IProcessRuntime` interface does not expose, and the default `Start` action uses them.

## Caveats

- `ProcessController` is typed against the concrete `ProcessRuntime`, not `IProcessRuntime`, because it uses the extended `StartProcessInstanceAsync` overload. Subclasses that take `IProcessRuntime` lose that overload.
- `SchemataExtensionPart<T>` exposes the assembly containing `T`. Adding more controllers in the same assembly will surface them as well.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [gRPC Transport](grpc.md)
- [Resource HTTP Transport](../resource/http-transport.md)
