using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.Linking;

namespace Schemata.Transport.Grpc.Linking;

public sealed class ResourceLinkContributor(IHttpContextAccessor accessor) : ILinkContributor
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (accessor.HttpContext?.RequestServices is not { } services) return [];
        return services.GetService<ResourceLinkSemantics>()?.GetLinks(context) ?? [];
    }
}
