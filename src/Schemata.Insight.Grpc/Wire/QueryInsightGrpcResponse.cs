using System.Collections.Generic;
using ProtoBuf;
using Schemata.Transport.Grpc.Wire;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>The federated read query result at the gRPC edge.</summary>
[ProtoContract]
public sealed class QueryInsightGrpcResponse
{
    [ProtoMember(1)] public List<DynamicStruct> Rows { get; set; } = new();

    [ProtoMember(2)] public List<DynamicFieldDescriptor<FieldType>> Schema { get; set; } = new();

    [ProtoMember(3)] public string? NextPageToken { get; set; }

    [ProtoMember(4)] public int? TotalSize { get; set; }

    [ProtoMember(5)] public List<string> Unreachable { get; set; } = new();
}
