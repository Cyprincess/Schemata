using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>The federated read query request at the gRPC edge.</summary>
[ProtoContract]
public sealed class QueryInsightGrpcRequest
{
    [ProtoMember(1)] public List<SourceBindingMessage> Sources { get; set; } = new();

    [ProtoMember(2)] public List<JoinSpecMessage> Joins { get; set; } = new();

    [ProtoMember(3)] public List<TransformationMessage> Transformations { get; set; } = new();

    [ProtoMember(4)] public List<SelectionMessage> Selections { get; set; } = new();

    [ProtoMember(5)] public int? PageSize { get; set; }

    [ProtoMember(6)] public int? Skip { get; set; }

    [ProtoMember(7)] public string? PageToken { get; set; }

    [ProtoMember(8)] public string? Language { get; set; }
}
