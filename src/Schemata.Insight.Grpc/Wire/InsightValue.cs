using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Insight.Grpc;

/// <summary>
///     A dynamic value at the gRPC edge, mirroring <c>google.protobuf.Value</c>. Exactly one typed
///     slot is set (or <see cref="NullValue" />), so dictionary rows serialize without a compile-time
///     schema.
/// </summary>
[ProtoContract]
public sealed class InsightValue
{
    [ProtoMember(1)] public string? StringValue { get; set; }

    [ProtoMember(2)] public double? NumberValue { get; set; }

    [ProtoMember(3)] public long? IntValue { get; set; }

    [ProtoMember(4)] public bool? BoolValue { get; set; }

    [ProtoMember(5)] public InsightStruct? StructValue { get; set; }

    [ProtoMember(6)] public List<InsightValue>? ListValue { get; set; }

    [ProtoMember(7)] public bool NullValue { get; set; }
}

/// <summary>A nested object of dynamic values, mirroring <c>google.protobuf.Struct</c>.</summary>
[ProtoContract]
public sealed class InsightStruct
{
    [ProtoMember(1)] public Dictionary<string, InsightValue> Fields { get; set; } = new();
}
