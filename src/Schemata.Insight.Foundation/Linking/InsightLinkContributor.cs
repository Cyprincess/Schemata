using System.Collections.Generic;
using Microsoft.AspNetCore.DataProtection;
using Schemata.Core.Linking;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Insight.Foundation.Linking;

public sealed class InsightLinkContributor(IDataProtectionProvider protection) : ILinkContributor
{
    private readonly IDataProtector _protector = protection.CreateProtector(InsightPageToken.ProtectionPurpose);

    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (context.Result is not IInsightLinkResult response || context.Target is not DomainOperationTarget target
         || target.Operation != InsightConstants.Operations.Insight) yield break;
        target = target with { Parameters = null };
        yield return new(LinkRelations.Self, target);
        if (response.NextPageToken is { Length: > 0 } next)
            yield return new(LinkRelations.Next, target with { Parameters = Token(next) });
        if (context.Request?.GetInput(Pagination.PageToken) is { Length: > 0 } current
         && InsightPageToken.EncodePrevious(_protector, current) is { } previous)
            yield return new(LinkRelations.Prev, target with { Parameters = Token(previous) });
    }

    private static IReadOnlyDictionary<string, string?> Token(string token) => new Dictionary<string, string?> {
        [Pagination.PageToken] = token,
    };
}
