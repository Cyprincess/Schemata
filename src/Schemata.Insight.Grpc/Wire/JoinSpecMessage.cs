using ProtoBuf;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>A cross-source join.</summary>
[ProtoContract]
public sealed class JoinSpecMessage
{
    [ProtoMember(1)] public string Left { get; set; } = string.Empty;

    [ProtoMember(2)] public string Right { get; set; } = string.Empty;

    [ProtoMember(3)] public JoinKind Kind { get; set; }

    [ProtoMember(4)] public InsightExpressionMessage On { get; set; } = new();
}