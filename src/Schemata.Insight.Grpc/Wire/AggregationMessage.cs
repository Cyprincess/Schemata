using ProtoBuf;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>An aggregation within a group-by.</summary>
[ProtoContract]
public sealed class AggregationMessage
{
    [ProtoMember(1)] public string Field { get; set; } = string.Empty;

    [ProtoMember(2)] public AggregationFunction Function { get; set; }

    [ProtoMember(3)] public string Alias { get; set; } = string.Empty;
}