using System.Collections.Generic;
using ProtoBuf;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>Describes one response field; nested objects carry child descriptors.</summary>
[ProtoContract]
public sealed class FieldDescriptorMessage
{
    [ProtoMember(1)] public string Name { get; set; } = string.Empty;

    [ProtoMember(2)] public FieldType Type { get; set; }

    [ProtoMember(3)] public string? SourceAlias { get; set; }

    [ProtoMember(4)] public bool IsList { get; set; }

    [ProtoMember(5)] public List<FieldDescriptorMessage> Children { get; set; } = new();
}