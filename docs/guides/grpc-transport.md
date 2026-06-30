# gRPC Transport

Expose the `Student` resource over gRPC alongside its HTTP endpoints, using code-first protobuf-net serialization.
This guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Resource.Grpc`. Composing packages by hand:

```shell
dotnet add package --prerelease Schemata.Resource.Grpc
```

## Enable gRPC transport

`MapGrpc()` is an extension on `SchemataResourceBuilder` that activates the gRPC transport and returns the
same builder, so it chains alongside `.MapHttp()` and `.Use<Student>()`:

```csharp
var resource = schema.UseResource();

resource.MapHttp()
        .Use<Student>();

resource.MapGrpc()
        .Use<Student>();
```

`Use<Student>()` is shorthand for `Use<Student, Student, Student, Student>()`. To split the surfaces, pass all
four type parameters:

```csharp
resource.MapGrpc()
        .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

All four types must implement `ICanonicalName`.

## Add protobuf-net attributes

Code-first gRPC needs protobuf-net field numbers on the serialized types. Add `[ProtoContract]` and
`[ProtoMember]` to `Student`:

```csharp
using ProtoBuf;
using Schemata.Abstractions.Entities;

[ProtoContract]
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    [ProtoMember(1)] public Guid      Uid           { get; set; }
    [ProtoMember(2)] public string?   Name          { get; set; }
    [ProtoMember(3)] public string?   CanonicalName { get; set; }
    [ProtoMember(4)] public string?   FullName      { get; set; }
    [ProtoMember(5)] public int       Age           { get; set; }
    [ProtoMember(6)] public DateTime? CreateTime    { get; set; }
    [ProtoMember(7)] public DateTime? UpdateTime    { get; set; }
    [ProtoMember(8)] public DateTime? DeleteTime    { get; set; }
    [ProtoMember(9)] public DateTime? PurgeTime     { get; set; }
}
```

## How it works

The gRPC transport synthesizes a service per resource and delegates to the same operation handlers as HTTP, so
authorization, validation, timestamps, and soft-delete apply identically across both transports. The service is
named `StudentService` and its RPCs are `ListStudents`, `GetStudent`, `CreateStudent`, `UpdateStudent`, and
`DeleteStudent`. Field names match the HTTP JSON, and gRPC reflection is enabled once any gRPC resource is
registered. Service synthesis and wire naming are covered in
[gRPC Transport](../documents/resource/grpc-transport.md).

## Verify

```shell
dotnet run
```

```shell
# Discover services
grpcurl -plaintext localhost:5000 list

# List students
grpcurl -plaintext -d '{}' localhost:5000 StudentService/ListStudents
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

## Next steps

- [Multi-Tenancy](multi-tenancy.md) — tenant resolvers cover both HTTP and gRPC requests
- [Authorization](authorization.md) — issue bearer tokens that authenticate both transports
- [Event Bus](event-bus.md) — publish domain events from either transport

## See also

- [gRPC Transport](../documents/resource/grpc-transport.md) — service synthesis, naming, and error mapping
