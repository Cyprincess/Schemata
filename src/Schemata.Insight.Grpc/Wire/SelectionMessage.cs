using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

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