using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>One transformation; exactly one member is set.</summary>
[ProtoContract]
public sealed class TransformationMessage
{
    [ProtoMember(1)] public InsightExpressionMessage? Filter { get; set; }

    [ProtoMember(2)] public List<ComputedFieldMessage>? Compute { get; set; }

    [ProtoMember(3)] public List<string>? GroupByKeys { get; set; }

    [ProtoMember(4)] public List<AggregationMessage>? GroupByAggregations { get; set; }

    [ProtoMember(5)] public string? OrderBy { get; set; }

    [ProtoMember(6)] public int? Top { get; set; }

    [ProtoMember(7)] public int? Skip { get; set; }

    [ProtoMember(8)] public bool IsGroupBy { get; set; }
}