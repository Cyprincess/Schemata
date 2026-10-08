using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Transport.Grpc.Wire;

/// <summary>Preserves named fields, including explicit null values and empty containers.</summary>
[ProtoContract]
public sealed class DynamicStruct
{
    [ProtoMember(1)] public Dictionary<string, DynamicValue> Fields { get; set; } = new();
}