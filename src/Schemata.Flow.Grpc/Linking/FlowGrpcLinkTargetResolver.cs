using System.Collections.Generic;
using System.Linq;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Routing;
using Schemata.Core.Linking;
using Schemata.Flow.Grpc.Services;
using Schemata.Flow.Grpc.Wire;
using Schemata.Flow.Skeleton;
using Schemata.Transport.Grpc.Linking;

namespace Schemata.Flow.Grpc.Linking;

public sealed class FlowGrpcLinkTargetResolver(IEnumerable<EndpointDataSource> sources) : IGrpcLinkTargetResolver
{
    public GrpcLinkTarget? Resolve(SemanticTarget target) {
        if (target is not DomainOperationTarget { Operation: FlowConstants.LinkOperations.Definitions } operation
            || Method() is not { } method) return null;
        return new(method, new Dictionary<string, string?> {
            ["definition_name"] = Input(operation, "definitionName"),
            ["definition_version"] = Input(operation, "definitionVersion"),
        });
    }

    public SemanticTarget? GetTarget(string method, object request) => request is ListProcessDefinitionsRequest definitions
        && Method() == method ? new DomainOperationTarget(FlowConstants.LinkOperations.Definitions,
            new Dictionary<string, string?> { ["definitionName"] = definitions.DefinitionName, ["definitionVersion"] = definitions.DefinitionVersion }) : null;

    private string? Method() => sources.SelectMany(source => source.Endpoints)
        .Select(endpoint => endpoint.Metadata.GetMetadata<GrpcMethodMetadata>())
        .FirstOrDefault(metadata => metadata is { ServiceType: var service } && service == typeof(ProcessDefinitionService)
            && metadata.Method.FullName == FlowGrpcMethods.Definitions.FullName)?.Method.FullName;

    private static string? Input(DomainOperationTarget operation, string key) =>
        operation.Parameters is { } inputs && inputs.TryGetValue(key, out var value) ? value : null;
}
