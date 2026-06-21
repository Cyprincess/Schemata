using System.Collections.Generic;
using ProtoBuf;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Grpc;

/// <summary>Describes one response field; nested objects carry child descriptors.</summary>
[ProtoContract]
public sealed class FieldDescriptorMessage
{
    [ProtoMember(1)] public string Name { get; set; } = string.Empty;

    [ProtoMember(2)] public FieldType Type { get; set; }

    [ProtoMember(3)] public string? SourceAlias { get; set; }

    [ProtoMember(4)] public bool IsList { get; set; }

    [ProtoMember(5)] public List<FieldDescriptorMessage> Children { get; set; } = new();
}

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
