using ProtoBuf;
using Schemata.Common;

namespace Schemata.Transport.Grpc.Wire;

/// <summary>
///     A dynamic gRPC value with one scalar or container slot. Container submessages retain
///     the distinction between empty collections and null after protobuf serialization.
/// </summary>
[ProtoContract]
public sealed class DynamicValue
{
    [ProtoMember(1)] public string? StringValue { get; set; }

    [ProtoMember(2)] public double? NumberValue { get; set; }

    [ProtoMember(3)] public long? IntValue { get; set; }

    [ProtoMember(4)] public bool? BoolValue { get; set; }

    [ProtoMember(5)] public DynamicStruct? StructValue { get; set; }

    [ProtoMember(6)] public DynamicList? ListValue { get; set; }

    [ProtoMember(7)] public bool NullValue { get; set; }

    [ProtoMember(8)] public ScalarKind? TypeLabel { get; set; }
}