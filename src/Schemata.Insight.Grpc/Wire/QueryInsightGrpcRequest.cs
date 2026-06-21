using System.Collections.Generic;
using ProtoBuf;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Grpc;

/// <summary>An expression slot: source text plus an optional language override.</summary>
[ProtoContract]
public sealed class InsightExpressionMessage
{
    [ProtoMember(1)] public string Source { get; set; } = string.Empty;

    [ProtoMember(2)] public string? Language { get; set; }
}

/// <summary>Binds a registered source name to a request-unique alias.</summary>
[ProtoContract]
public sealed class SourceBindingMessage
{
    [ProtoMember(1)] public string Alias { get; set; } = string.Empty;

    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
}

/// <summary>A cross-source join.</summary>
[ProtoContract]
public sealed class JoinSpecMessage
{
    [ProtoMember(1)] public string Left { get; set; } = string.Empty;

    [ProtoMember(2)] public string Right { get; set; } = string.Empty;

    [ProtoMember(3)] public JoinKind Kind { get; set; }

    [ProtoMember(4)] public InsightExpressionMessage On { get; set; } = new();
}

/// <summary>A computed field within a compute transformation.</summary>
[ProtoContract]
public sealed class ComputedFieldMessage
{
    [ProtoMember(1)] public InsightExpressionMessage Expression { get; set; } = new();

    [ProtoMember(2)] public string Alias { get; set; } = string.Empty;
}

/// <summary>An aggregation within a group-by.</summary>
[ProtoContract]
public sealed class AggregationMessage
{
    [ProtoMember(1)] public string Field { get; set; } = string.Empty;

    [ProtoMember(2)] public AggregationFunction Function { get; set; }

    [ProtoMember(3)] public string Alias { get; set; } = string.Empty;
}

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

/// <summary>A nested projection item: a field, a computed expression, or a nested sub-selection.</summary>
[ProtoContract]
public sealed class SelectionMessage
{
    [ProtoMember(1)] public string? Field { get; set; }

    [ProtoMember(2)] public string? Alias { get; set; }

    [ProtoMember(3)] public InsightExpressionMessage? Expression { get; set; }

    [ProtoMember(4)] public List<SelectionMessage>? Selections { get; set; }

    [ProtoMember(5)] public List<TransformationMessage>? Transformations { get; set; }
}

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
