using System.Collections.Generic;
using ProtoBuf;

namespace Schemata.Transport.Grpc.Wire;

[ProtoContract]
public sealed class DynamicList
{
    [ProtoMember(1)] public List<DynamicValue> Values { get; set; } = new();
}
