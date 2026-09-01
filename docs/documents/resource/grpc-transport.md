# gRPC Transport

The gRPC transport exposes resources as code-first gRPC services using protobuf-net. `MapGrpc()` is the concrete Resource extension that activates `SchemataGrpcResourceFeature`; its dependencies provide the shared gRPC runtime and map each registered `ResourceService<TEntity,TRequest,TDetail,TSummary>`.

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

`MapGrpc()` activates `SchemataGrpcResourceFeature` and returns the same `SchemataResourceBuilder`. A plain `Use<...>()` exposes a resource on every active transport; `Use<Student>(r => r.MapGrpc())` restricts that resource to the gRPC endpoint.

`SchemataGrpcResourceFeature` depends on `SchemataResourceFeature` and `SchemataTransportGrpcFeature`. The shared feature registers code-first gRPC, the exception-mapping interceptor, the runtime model, and reflection.

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

`ResourceCustomMethod.Register` adds one unary RPC for each declared method. The RPC name is `{PascalVerb}{Singular}` (`run` plus `Job` becomes `RunJob`). It resolves `ResourceMethodOperationHandler<TEntity,TRequest,TResponse>`, which packages the verb, target, request, and principal in `ResourceMethodRequest<TEntity,TRequest,TResponse>` and dispatches it. The method envelope runs wrap policy before Resource method handler stages and inner handler dispatch.

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

- `WithAuthentication("scheme")` configures the builder transport scheme and registers authentication wraps. `WithAuthorization()` separately registers coarse and handler-stage authorization advisors. A `ResourceAttribute.AuthenticationScheme` overrides the builder default for its endpoint.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [Custom Methods](custom-methods.md)
- [Resource Naming](resource-naming.md)
- [AIP Interactions](aip-interactions.md)
- [AIP Business Logic](aip-business-logic.md)
