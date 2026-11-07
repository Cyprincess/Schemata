using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Core.Linking;
using Schemata.Push.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Push.Foundation.Linking;

public sealed class PushLinkContributor : ILinkContributor
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (context.Result is not IPushLinkResult response || context.Target is not DomainOperationTarget target
         || target.Operation != PushConstants.Routes.Subscriptions) yield break;
        var current = context.Request?.GetInput("provider");
        target = target with { Parameters = current is { Length: > 0 }
            ? new Dictionary<string, string?> { ["provider"] = current } : null };
        yield return new(LinkRelations.Self, target);
        if (current is { Length: > 0 }) {
            yield return new(LinkRelations.Alternate, target with { Parameters = null }, "all providers");
            yield break;
        }
        foreach (var provider in response.GetLinkProviders().Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal))
            yield return new(LinkRelations.Alternate, target with { Parameters = new Dictionary<string, string?> { ["provider"] = provider } }, provider);
    }
}
