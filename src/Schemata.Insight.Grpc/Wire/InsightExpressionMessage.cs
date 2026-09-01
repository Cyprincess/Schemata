using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>An expression slot: source text plus an optional language override.</summary>
[ProtoContract]
public sealed class InsightExpressionMessage
{
    [ProtoMember(1)] public string Source { get; set; } = string.Empty;

    [ProtoMember(2)] public string? Language { get; set; }
}