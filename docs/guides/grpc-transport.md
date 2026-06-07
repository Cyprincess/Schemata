# gRPC Transport

Expose the `Student` resource over gRPC alongside the existing HTTP endpoints using code-first protobuf-net serialization. This guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Resource.Grpc`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Resource.Grpc
```

## Enable gRPC transport

`MapGrpc()` is an extension method on `SchemataResourceBuilder` that returns a `SchemataGrpcResourceBuilder`. `SchemataGrpcResourceFeature` runs at `Order = Priority = 490_200_000`.

In `Program.cs`, chain `.MapGrpc().Use<Student>()` alongside `.MapHttp()`:

```csharp
var resource = schema.UseResource();

resource.MapHttp()
        .Use<Student>();

resource.MapGrpc()
        .Use<Student>();
```

`Use<Student>()` on `SchemataGrpcResourceBuilder` is shorthand for `Use<Student, Student, Student, Student>()`. To use separate DTOs, pass all four type parameters:

```csharp
resource.MapGrpc()
        .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

All four types must implement `ICanonicalName`.

## Add protobuf-net attributes

Code-first gRPC requires protobuf-net serialization attributes on your types. Add `[ProtoContract]` and `[ProtoMember]` to `Student`:

```csharp
using ProtoBuf;
using Schemata.Abstractions.Entities;

[ProtoContract]
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    [ProtoMember(1)] public Guid      Uid          { get; set; }
    [ProtoMember(2)] public string?   Name         { get; set; }
    [ProtoMember(3)] public string?   CanonicalName { get; set; }
    [ProtoMember(4)] public string?   FullName     { get; set; }
    [ProtoMember(5)] public int       Age          { get; set; }
    [ProtoMember(6)] public DateTime? CreateTime   { get; set; }
    [ProtoMember(7)] public DateTime? UpdateTime   { get; set; }
    [ProtoMember(8)] public DateTime? DeleteTime   { get; set; }
    [ProtoMember(9)] public DateTime? PurgeTime    { get; set; }
}
```

## How it works

`SchemataGrpcResourceFeature` synthesizes an open-generic `ResourceService<TEntity, TRequest, TDetail, TSummary>` for each registered resource and maps it via `endpoints.MapGrpcService<...>()`. The service delegates to the same `ResourceOperationHandler` used by HTTP, so all advisors (authorization, validation, timestamps, soft-delete) apply identically to both transports.

gRPC reflection is enabled automatically when at least one gRPC resource is registered, allowing tools like `grpcurl` to discover services.

## Verify

```shell
dotnet run
```

```shell
# List available services
grpcurl -plaintext localhost:5000 list

# List students via gRPC
grpcurl -plaintext -d '{}' \
    localhost:5000 IResourceService_Student_Student_Student_Student/ListAsync
```

From a .NET client:

```csharp
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using Schemata.Resource.Grpc;

var channel = GrpcChannel.ForAddress("http://localhost:5000");
var client  = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>();
var result  = await client.ListAsync(new ListRequest());
```

## See also

- [Authorization](authorization.md) — previous in the series: OAuth 2.0 / OpenID Connect server
- [Multi-Tenancy](multi-tenancy.md) — next in the series: tenant resolution and data isolation
- [gRPC Transport](../documents/resource/grpc-transport.md) — `MapGrpc`, service synthesis, exception mapping
