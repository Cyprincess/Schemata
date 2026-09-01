using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>A computed field within a compute transformation.</summary>
[ProtoContract]
public sealed class ComputedFieldMessage
{
    [ProtoMember(1)] public InsightExpressionMessage Expression { get; set; } = new();

    [ProtoMember(2)] public string Alias { get; set; } = string.Empty;
}