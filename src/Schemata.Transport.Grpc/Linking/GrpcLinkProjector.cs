using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Linking;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Schemata.Common.Linking;

namespace Schemata.Transport.Grpc.Linking;

public sealed class GrpcLinkProjector(IEnumerable<IGrpcLinkTargetResolver> resolvers)
{
    public LinkContext CreateContext(string method, object request, object? result, ResourceRegistration? resource) {
        var target = resolvers.Select(resolver => resolver.GetTarget(method, request)).FirstOrDefault(value => value is not null);
        return new(target, result, resource, request as ILinkRequest ?? (request is ListRequest list ? new ListInput(list) : null));
    }

    public WebLink? Project(SemanticLink link, LinkContext context) {
        var target = resolvers.Select(resolver => resolver.Resolve(link.Target)).FirstOrDefault(value => value is not null);
        if (target is null) return null;
        var projected = new WebLink(target.Method, link.Relation, Title: link.Title,
            Extensions: target.Inputs.Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty));
        return LinkValue.IsRepresentable(projected) ? projected : null;
    }

    private sealed class ListInput(ListRequest request) : ILinkRequest
    {
        public string? GetInput(string name) => name switch {
            "page_token" => request.PageToken,
            "parent" => request.Parent,
            "filter" => request.Filter,
            _ => null,
        };
    }
}
