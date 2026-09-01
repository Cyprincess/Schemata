using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>A nested object of dynamic values, mirroring <c>google.protobuf.Struct</c>.</summary>
[ProtoContract]
public sealed class InsightStruct
{
    [ProtoMember(1)] public Dictionary<string, InsightValue> Fields { get; set; } = new();
}