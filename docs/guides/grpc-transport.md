# gRPC Transport

Expose the `Student` resource over gRPC alongside its HTTP endpoints, using code-first protobuf-net serialization.
This is a transport branch: it follows [Authorization](authorization.md) in the full sequence, but it
only requires the HTTP Student resource from [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Resource.Grpc`. Composing packages by hand:

```shell
dotnet add package --prerelease Schemata.Resource.Grpc
```

## Enable gRPC transport

`MapGrpc()` is an extension on `SchemataResourceBuilder` that activates the gRPC transport and returns the
same builder. Replace the final HTTP-only resource registration with the merged transport registration:

```csharp
schema.UseResource()
      .MapHttp()
      .MapGrpc()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

Keep options already enabled by earlier guides ahead of `MapHttp()` — for example, `UseAip()`,
`UseOrdering()`, and, if you completed Access Control, `WithAuthorization()`. If you skipped Object
Mapping, use the established shorthand instead:

```csharp
schema.UseResource()
      .MapHttp()
      .MapGrpc()
      .Use<Student>();
```

All four types must implement `ICanonicalName`. The first registration is the continuation of the
full guide chain; the second is the Getting Started branch.

## Add protobuf-net attributes

Code-first gRPC needs protobuf-net field numbers on the serialized types. Add `[ProtoContract]` and
`[ProtoMember]` to `Student`:

```csharp
using ProtoBuf;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;

[ProtoContract]
[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IConcurrency
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
    [ProtoMember(10)]
    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }
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
var client  = channel.CreateGrpcService<IResourceService<Student, StudentRequest, StudentDetail, StudentSummary>>();
var result  = await client.ListAsync(new ListRequest());
```

## Next steps

- [Multi-Tenancy](multi-tenancy.md) — tenant resolvers cover both HTTP and gRPC requests
- [Authorization](authorization.md) — issue bearer tokens that authenticate both transports
- [Event Bus](event-bus.md) — publish domain events from either transport

## See also

- [gRPC Transport](../documents/resource/grpc-transport.md) — service synthesis, naming, and error mapping
