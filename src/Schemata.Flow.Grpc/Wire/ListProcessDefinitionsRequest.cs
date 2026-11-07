using ProtoBuf;
using Schemata.Core.Linking;

namespace Schemata.Flow.Grpc.Wire;

[ProtoContract]
public sealed class ListProcessDefinitionsRequest : ILinkRequest
{
    [ProtoMember(1, Name = "definition_name")] public string? DefinitionName { get; set; }
    [ProtoMember(2, Name = "definition_version")] public string? DefinitionVersion { get; set; }

    string? ILinkRequest.GetInput(string name) => name switch {
        "definition_name" => DefinitionName,
        "definition_version" => DefinitionVersion,
        _ => null,
    };
}
