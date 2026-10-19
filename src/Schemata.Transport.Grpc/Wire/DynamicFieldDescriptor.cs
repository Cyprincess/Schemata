using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Transport.Grpc.Wire;

/// <summary>Describes one response field; nested objects carry child descriptors.</summary>
[ProtoContract]
public sealed class DynamicFieldDescriptor<TFieldType> where TFieldType : struct, Enum
{
    [ProtoMember(1)] public string Name { get; set; } = string.Empty;

    [ProtoMember(2)] public TFieldType Type { get; set; }

    [ProtoMember(3)] public string? SourceAlias { get; set; }

    [ProtoMember(4)] public bool IsList { get; set; }

    [ProtoMember(5)] public List<DynamicFieldDescriptor<TFieldType>> Children { get; set; } = new();

    [ProtoMember(6)] public DynamicFieldDescriptor<TFieldType>? Element { get; set; }
}