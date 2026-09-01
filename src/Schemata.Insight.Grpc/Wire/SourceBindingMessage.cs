using ProtoBuf;

namespace Schemata.Insight.Grpc.Wire;

/// <summary>Binds a registered source name to a request-unique alias.</summary>
[ProtoContract]
public sealed class SourceBindingMessage
{
    [ProtoMember(1)] public string Alias { get; set; } = string.Empty;

    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
}