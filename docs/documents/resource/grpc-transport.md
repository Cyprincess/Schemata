# gRPC Transport

The gRPC transport exposes resources as code-first gRPC services using protobuf-net (ProtoBuf.Grpc). `MapGrpc()`
on `SchemataResourceBuilder` adds `SchemataGrpcResourceFeature`, which synthesizes a
`ResourceService<TEntity, TRequest, TDetail, TSummary>` for each registered resource and maps it as a gRPC
endpoint.

## Where the code lives

| Package                   | Key files                                                                                                                                                      |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Resource.Grpc`  | `Features/SchemataGrpcResourceFeature.cs`, `Extensions/SchemataResourceBuilderExtensions.cs`                                                                   |
| `Schemata.Resource.Grpc`  | `IResourceService.cs`, `ResourceService.cs`, `ResourceServiceBinder.cs`, `ResourceServiceMethodProvider.cs`                                                    |
| `Schemata.Resource.Grpc`  | `ResourceCustomMethod.cs`, `Internal/GrpcResourceNaming.cs`, `ResourceMethodNaming.cs`, `FileDescriptorBridge.cs`                                              |
| `Schemata.Transport.Grpc` | `Proto/SchemataTransportGrpcFeature.cs`, `Proto/SchemataProtoModelConfigurator.cs`, `Proto/RpcStatusBuilder.cs`, `Interceptors/ExceptionMappingInterceptor.cs` |

## Activation

```csharp
schema.UseResource()
      .MapGrpc()
      .Use<Student>();
```

`MapGrpc()` adds `SchemataGrpcResourceFeature` (`DefaultPriority = SchemataResourceFeature.DefaultPriority +
200_000`) and returns the same `SchemataResourceBuilder`, so registrations and transports chain without an
intermediate builder. A plain `Use<...>()` exposes the resource on every active transport; to restrict it to
gRPC, pass a selector — `Use<Student>(r => r.MapGrpc())` — which tags the resource with
`GrpcResourceAttribute.Name` (`"gRPC"`).

`SchemataGrpcResourceFeature` declares `[DependsOn]` on `SchemataResourceFeature` and `SchemataTransportGrpcFeature`.
The transport feature (`DefaultPriority = Orders.Extension + 20_000_000`) calls `AddCodeFirstGrpc`, registers
`ExceptionMappingInterceptor`, configures `RuntimeTypeModel.Default`, and maps the reflection services.
`ConfigureServices` registers the open-generic `ResourceService<,,,>` as scoped, a singleton
`ResourceBinderConfiguration` (the `RuntimeTypeModel` plus a `BinderConfiguration` from `ResourceServiceBinder`),
the open-generic `ResourceServiceMethodProvider<>` as `IServiceMethodProvider<>`, and
`ResourceGrpcServiceDescriptorContributor` as `IGrpcServiceDescriptorContributor`.

## Service synthesis

`IResourceService<TEntity, TRequest, TDetail, TSummary>` defines five operations:

```csharp
public interface IResourceService<TEntity, TRequest, TDetail, TSummary>
{
    [Operation] ValueTask<ListResultBase<TSummary>> ListAsync(ListRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail>  GetAsync(GetRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail>  CreateAsync(TRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail>  UpdateAsync(TRequest request, CallContext context = default);
    [Operation] ValueTask<TDetail?> DeleteAsync(DeleteRequest request, CallContext context = default);
}
```

`ResourceService<,,,>` implements it by extracting the user from `IHttpContextAccessor` and delegating to the
shared `ResourceOperationHandler`, so the same advisors apply to both transports. The interface carries
`[Operation]` but no `[Service]` attribute; the service and method names come from the entity's
`ResourceNameDescriptor` through `ResourceServiceBinder`. `ConfigureEndpoints` maps each closed
`ResourceService<,,,>` via `MapGrpcService`.

## Routing and method mapping

`ResourceServiceBinder` and `GrpcResourceNaming` name the service and its methods:

- **Service name**: `{Package}.{Singular}Service` when a package is set, otherwise `{Singular}Service` (a `Book`
  in package `library` becomes `library.BookService`).
- **Method names**: `List{Plural}` for List; `{Operation}{Singular}` for the rest — `GetBook`, `CreateBook`,
  `UpdateBook`, `DeleteBook`.

`ResourceServiceMethodProvider<TService>` registers the standard methods through `context.AddUnaryMethod`,
filtered by the `Operations` whitelist. The Delete response type depends on the entity: an `ISoftDelete` entity
responds with the updated detail per AIP-164, a hard-deletable entity with `google.protobuf.Empty`.

### Custom methods

`ResourceCustomMethod.Register` runs inside `ResourceServiceMethodProvider` and adds one unary RPC per declared
method to the resource's existing service. The RPC name is
`GrpcResourceNaming.CustomMethodName(descriptor, verb)` = `{PascalVerb}{Singular}` (`run` + `Job` → `RunJob`). The
unary handler resolves the `IResourceMethodHandler<TEntity, TRequest, TResponse>` from DI and dispatches through
`ResourceMethodOperationHandler`. See [Custom Methods](custom-methods.md).

## Request and response wire format

`SchemataProtoModelConfigurator` adds each request, detail, summary, and `ListResultBase<TSummary>` type to the
`RuntimeTypeModel`. For each writable property it resolves the wire name through
`ResourceWireNameRules.ResolveWireName` — the same `ResourceWireNameRules` aliases as HTTP (`Name` dropped,
`CanonicalName` → `name`, `EntityTag` → `etag`, `Entities` → plural) — then applies snake_case via Humanizer
`Underscore()`. `GrpcMarshallers.Create<T>` builds marshallers over the model, so payloads serialize with the
same field names as the HTTP JSON.

## Error mapping

`ExceptionMappingInterceptor` (registered by `SchemataTransportGrpcFeature`) wraps every unary call. It re-throws
an existing `RpcException`, converts a `SchemataException` through `RpcStatusBuilder.Build`, and wraps any other
exception as a 500 `SchemataException(ErrorCodes.Internal)`. `RpcStatusBuilder` builds a `Google.Rpc.Status`:
`MapFromCanonical` maps the canonical error code to a gRPC `StatusCode` (`not_found` → `NotFound`,
`invalid_argument` → `InvalidArgument`, `failed_precondition` → `FailedPrecondition`, …, default `Internal`), and
each error detail is packed into a `google.protobuf.Any`. The status is attached to the response as the
`grpc-status-details-bin` trailer.

## Reflection and metadata

`SchemataTransportGrpcFeature` maps `ReflectionServiceImpl` (v1alpha) and `ReflectionV1ServiceImpl` (v1) once for
the application and merges descriptors from every `IGrpcServiceDescriptorContributor`.
`ResourceGrpcServiceDescriptorContributor` contributes the closed `ResourceService<,,,>` types;
`FileDescriptorBridge.BuildServiceDescriptors` builds a `proto3` file descriptor per resource (named
`{singular}_service.proto`) with the standard and custom RPCs. Reflection-capable clients such as `grpcurl` see
the full schema.

## Extension points

- Subclass `ResourceService<TEntity, TRequest, TDetail, TSummary>` and override methods; register the subclass as
  scoped.
- Implement `IResourceService<TEntity, TRequest, TDetail, TSummary>` directly and map it with
  `endpoints.MapGrpcService<MyService>()`.
- `[ResourcePackage("myapi")]` sets the gRPC service-name prefix.

## Caveats

- `ExceptionMappingInterceptor` is required for status mapping; without it every exception surfaces as
  `INTERNAL`.
- `WithAuthorization(scheme)` applies the authentication scheme to the gRPC endpoints; the authorization decision
  happens in the advisor pipeline.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [Custom Methods](custom-methods.md)
- [Resource Naming](resource-naming.md)
