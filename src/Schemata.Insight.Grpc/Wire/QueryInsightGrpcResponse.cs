using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>The federated read query result at the gRPC edge.</summary>
[ProtoContract]
public sealed class QueryInsightGrpcResponse
{
    [ProtoMember(1)] public List<InsightStruct> Rows { get; set; } = new();

    [ProtoMember(2)] public List<FieldDescriptorMessage> Schema { get; set; } = new();

    [ProtoMember(3)] public string? NextPageToken { get; set; }

    [ProtoMember(4)] public int? TotalSize { get; set; }

    [ProtoMember(5)] public List<string> Unreachable { get; set; } = new();
}
