using System.Collections.Generic;
using Schemata.Core.Linking;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Resource.Http.Linking;

public sealed class ResourceLinkContributor(IHttpContextAccessor accessor) : ILinkContributor
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (accessor.HttpContext?.RequestServices is not { } services) return [];
        return services.GetRequiredService<ResourceLinkSemantics>().GetLinks(context);
    }
}
