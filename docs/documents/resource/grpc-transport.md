# gRPC Transport

The gRPC transport exposes resources as code-first gRPC services using protobuf-net (ProtoBuf.Grpc). Calling `MapGrpc()` on `SchemataResourceBuilder` registers `SchemataGrpcResourceFeature` (Priority `SchemataResourceFeature.DefaultPriority + 200_000` = 490,200,000), which synthesizes a `ResourceService<TEntity, TRequest, TDetail, TSummary>` for each registered resource and maps it as a gRPC endpoint at application startup.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Grpc` | `SchemataGrpcResourceBuilder.cs` |
| `Schemata.Resource.Grpc` | `Features/SchemataGrpcResourceFeature.cs` |
| `Schemata.Resource.Grpc` | `Extensions/SchemataResourceBuilderExtensions.cs` |
| `Schemata.Resource.Grpc` | `Extensions/SchemataGrpcResourceBuilderExtensions.cs` |
| `Schemata.Resource.Grpc` | `IResourceService.cs` |
| `Schemata.Resource.Grpc` | `ResourceService.cs` |
| `Schemata.Resource.Grpc` | `ResourceServiceBinder.cs` |
| `Schemata.Resource.Grpc` | `ResourceServiceMethodProvider.cs` |
| `Schemata.Resource.Grpc` | `ResourceCustomMethod.cs` |
| `Schemata.Resource.Grpc` | `ResourceMethodNaming.cs` |

## Setup

```csharp
builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseControllers();
    schema.UseResource()
          .MapGrpc()
          .Use<Student>();
});
```

`MapGrpc()` is an extension on `SchemataResourceBuilder` that adds `SchemataGrpcResourceFeature` and returns a `SchemataGrpcResourceBuilder`. Subsequent `Use<...>()` calls on the gRPC builder register resources tagged with `GrpcResourceAttribute.Name`.

## `SchemataGrpcResourceFeature`

`SchemataGrpcResourceFeature` depends on `SchemataResourceFeature` and `SchemataTransportGrpcFeature` via `[DependsOn<T>]`. The transport feature supplies `AddCodeFirstGrpc`, the `ExceptionMappingInterceptor`, gRPC server reflection, and `RuntimeTypeModel.Default` configuration shared with `Schemata.Flow.Grpc`.

`ConfigureServices` registers:

- `ResourceService<,,,>` as an open-generic scoped service.
- A singleton `ResourceBinderConfiguration` that holds the `RuntimeTypeModel` (built from `SchemataResourceOptions`) and a `BinderConfiguration` produced from `ResourceServiceBinder`.
- `ResourceServiceMethodProvider<TService>` as an open-generic `IServiceMethodProvider<TService>` singleton — the hook gRPC ASP.NET Core uses to discover service methods.
- `ResourceGrpcServiceDescriptorContributor` as `IGrpcServiceDescriptorContributor`, contributing the resource service descriptors into the shared gRPC reflection set.

`ConfigureEndpoints` iterates `SchemataResourceOptions.Resources` and, for each resource whose `Endpoints` list includes `GrpcResourceAttribute.Name` (or is `null`), calls:

```csharp
var service = typeof(ResourceService<,,,>)
    .MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!);
MapGrpcService(endpoints, service);
```

`MapGrpcService` is resolved via reflection on `GrpcEndpointRouteBuilderExtensions` to avoid a hard compile-time dependency on `Grpc.AspNetCore.Server`.

## `IResourceService<TEntity, TRequest, TDetail, TSummary>`

The service contract defines five operations:

```csharp
public interface IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    [Operation] ValueTask<ListResultBase<TSummary>> ListAsync(ListRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail?> DeleteAsync(DeleteRequest request, CallContext context = default);
}
```

`IResourceService<,,,>` carries `[Operation]` on each method but no `[Service]` attribute: the service name is derived from the entity's `ResourceNameDescriptor` at runtime, so binding goes through `ResourceServiceBinder` and `ResourceServiceMethodProvider` and bypasses protobuf-net's attribute-driven discovery.

## `ResourceServiceBinder`

`ResourceServiceBinder` extends ProtoBuf.Grpc's `ServiceBinder` to override service and operation naming for `IResourceService<,,,>` contracts:

- **Service name**: `{package}.{Singular}Service` when a package is configured, or `{Singular}Service` otherwise. A `Book` entity with package `"library"` gets service name `"library.BookService"`.
- **Operation names**: `List{Plural}` for list (e.g., `"ListBooks"`), `{Verb}{Singular}` for all others (e.g., `"GetBook"`, `"CreateBook"`, `"UpdateBook"`, `"DeleteBook"`).

## `ResourceServiceMethodProvider<TService>`

`ResourceServiceMethodProvider<TService>` implements `IServiceMethodProvider<TService>`. When `TService` is a closed `ResourceService<,,,>`, it uses reflection to call `RegisterAll<TEntity, TRequest, TDetail, TSummary>`, which registers five unary gRPC methods via `context.AddUnaryMethod`. Each method:

1. Casts the service instance to `IResourceService<TEntity, TRequest, TDetail, TSummary>`.
2. Calls the corresponding method on the interface.
3. Uses protobuf-net marshallers built from the `RuntimeTypeModel` for serialization.

The `Delete` RPC response type depends on the entity: `ISoftDelete` entities respond with the updated resource detail per AIP-164, while hard-deletable entities respond with `google.protobuf.Empty` through a custom `EmptyMarshaller`. The reflection descriptors in `FileDescriptorBridge` mirror the same split.

## Custom methods

Each `[ResourceMethod(verb, handler, scope)]` on the entity adds one unary RPC to the resource's existing service. `ResourceCustomMethod.Register` runs inside `ResourceServiceMethodProvider` for each closed `ResourceService<,,,>`, reads `SchemataResourceOptions.Methods` by entity, and calls `context.AddUnaryMethod` with:

- **Service name**: identical to the CRUD service (`{package}.{Singular}Service` or `{Singular}Service`). All verbs share the resource's existing service; no per-verb service is created.
- **RPC name**: `ResourceMethodNaming.GetRpcName(verb, singular)` = `{PascalVerb}{Singular}` (`run` + `Job` -> `RunJob`).

The marshallers are built from the shared `RuntimeTypeModel`, so request and response types are serialized with the same conventions as CRUD payloads. The unary handler resolves the matching `IResourceMethodHandler<TEntity, TRequest, TResponse>` through DI and dispatches via `ResourceMethodOperationHandler<TEntity, TRequest, TResponse>`. A null pipeline result surfaces as `NoContentException`, which `ExceptionMappingInterceptor` translates to gRPC `NotFound`.

See [Custom Methods](custom-methods.md) for the verb-scoped advisor pipeline and HTTP route equivalence through `google.api.http`.

## gRPC server reflection

`SchemataTransportGrpcFeature` maps `ReflectionServiceImpl` (v1alpha) and `ReflectionV1ServiceImpl` (v1) once for the whole application and merges descriptors from every `IGrpcServiceDescriptorContributor`. `ResourceGrpcServiceDescriptorContributor` contributes the closed `ResourceService<,,,>` types built from `SchemataResourceOptions.Resources`; `FlowProtoTypeContributor` (in `Schemata.Flow.Grpc`) does the same for `ProcessService`. grpcurl, Postman, and other reflection-capable clients see the full schema.

## Extension points

- Subclass `ResourceService<TEntity, TRequest, TDetail, TSummary>` and override individual methods. Register the subclass explicitly via `services.AddScoped<ResourceService<...>, MyService>()`.
- Implement `IResourceService<TEntity, TRequest, TDetail, TSummary>` directly for full control. Register it as a scoped service and map it via `endpoints.MapGrpcService<MyService>()`.
- Add `[ResourcePackage("myapi")]` to the entity type to control the gRPC service name prefix.

## Caveats

- `MapGrpcService` is resolved via reflection on `GrpcEndpointRouteBuilderExtensions` so `Schemata.Resource.Grpc` does not need a hard compile-time dependency on `Grpc.AspNetCore.Server`. If that method's signature changes in a future gRPC ASP.NET Core release, the call returns `null` and registration silently no-ops.
- `ExceptionMappingInterceptor` (registered by `SchemataTransportGrpcFeature`) translates `SchemataException` subtypes to the matching gRPC `StatusCode`. Without it, every exception surfaces as `INTERNAL`.

## See also

- [Resource Overview](overview.md)
- [Custom Methods](custom-methods.md)
- [HTTP Transport](http-transport.md)
- [Resource Naming](resource-naming.md)
- [Advice Pipeline](../core/advice-pipeline.md)
