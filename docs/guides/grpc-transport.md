# gRPC Transport

This guide adds gRPC endpoints alongside the existing HTTP API for the Student CRUD app. By the end, the same `Student` resource will be accessible over both HTTP and gRPC using code-first protobuf-net serialization.

## Add the package

```shell
dotnet add package --prerelease Schemata.Resource.Grpc
```

This pulls in `protobuf-net.Grpc`, `Grpc.AspNetCore.Server`, and protobuf-net reflection support.

## Configure gRPC transport

In `Program.cs`, chain `.MapGrpc()` on the resource builder alongside `.MapHttp()`:

```csharp
var resource = schema.UseResource()
                     .WithAuthorization();

resource.MapHttp()
        .Use<Student, Student, Student, Student>();

resource.MapGrpc()
        .Use<Student, Student, Student, Student>();
```

`MapHttp()` and `MapGrpc()` are both extension methods on `SchemataResourceBuilder`. Each returns its own builder type (`SchemataHttpResourceBuilder` and `SchemataGrpcResourceBuilder` respectively) with `.Use<>()` overloads for registering resources on that transport. To expose a resource on both transports, call `.Use<>()` on each builder.

If you want a resource available on all transports without calling `.Use<>()` on each builder separately, register it directly on the `SchemataResourceBuilder` before mapping transports:

```csharp
var resource = schema.UseResource()
                     .WithAuthorization();

resource.Use<Student, Student, Student, Student>();

resource.MapHttp();
resource.MapGrpc();
```

When `Use<>()` is called on the base `SchemataResourceBuilder` with no endpoint restrictions, both HTTP and gRPC features will pick up the resource. When called on a transport-specific builder (e.g., `MapHttp().Use<>()`), the resource is restricted to that transport only.

## How it works

The `SchemataGrpcResourceFeature` configures:

1. **Code-first gRPC** via `protobuf-net.Grpc` -- no `.proto` files needed. The `IResourceService<TEntity, TRequest, TDetail, TSummary>` interface defines the gRPC service contract with `List`, `Get`, `Create`, `Update`, and `Delete` operations.

2. **ResourceService** -- a default implementation that delegates to the same `ResourceOperationHandler` used by HTTP, so all advisors (authorization, validation, timestamps, soft-delete) apply identically to both transports.

3. **gRPC Reflection** -- automatically enabled when at least one gRPC resource is registered, allowing tools like `grpcurl` to discover services.

4. **Exception mapping** -- an `ExceptionMappingInterceptor` translates Schemata exceptions to appropriate gRPC status codes.

## Add protobuf-net attributes to the entity

Code-first gRPC requires protobuf-net serialization attributes on your DTOs. Add `[ProtoContract]` and `[ProtoMember]` attributes to the `Student` entity:

```csharp
using ProtoBuf;
using Schemata.Abstractions.Entities;

[ProtoContract]
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string? Name { get; set; }

    [ProtoMember(3)]
    public string? CanonicalName { get; set; }

    [ProtoMember(4)]
    public string? FullName { get; set; }

    [ProtoMember(5)]
    public int Age { get; set; }

    [ProtoMember(6)]
    public DateTime? CreateTime { get; set; }

    [ProtoMember(7)]
    public DateTime? UpdateTime { get; set; }

    [ProtoMember(8)]
    public DateTime? DeleteTime { get; set; }

    [ProtoMember(9)]
    public DateTime? PurgeTime { get; set; }

    [ProtoMember(10)]
    public string? CreatedBy { get; set; }
}
```

## Verify

```shell
dotnet run
```

The Student resource is now accessible over both HTTP and gRPC. The HTTP endpoints remain unchanged. The gRPC service is available on the same port using HTTP/2.

Test with `grpcurl` (gRPC reflection is enabled automatically):

```shell
# List available services
grpcurl -plaintext localhost:5000 list

# List students via gRPC
grpcurl -plaintext \
     -d '{}' \
     localhost:5000 IResourceService_Student_Student_Student_Student/ListAsync
```

You can also use a .NET gRPC client with protobuf-net code-first:

```csharp
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using Schemata.Resource.Grpc;

var channel = GrpcChannel.ForAddress("http://localhost:5000");
var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>();

var result = await client.ListAsync(new ListRequest());
```

## Next steps

- [Multi-Tenancy](multi-tenancy.md) -- add tenant resolution and data isolation
- For deeper technical details, see [gRPC Transport](../documents/resource/grpc-transport.md)
