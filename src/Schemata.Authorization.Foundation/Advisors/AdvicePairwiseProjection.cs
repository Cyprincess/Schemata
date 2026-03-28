using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdvicePairwiseProjection
{
    public const int DefaultOrder = Orders.Max;
}

public sealed class AdvicePairwiseProjection<TApp>(
    IApplicationManager<TApp> apps,
    ISubjectIdentifierService subjectService
) : IClaimsAdvisor
    where TApp : SchemataApplication
{
    #region IClaimsAdvisor Members

    public int Order => AdvicePairwiseProjection.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        var sub    = claims.FirstOrDefault(c => c.Type == Claims.Subject)?.Value;
        var client = claims.FirstOrDefault(c => c.Type == Claims.ClientId)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(client)) {
            return AdviseResult.Continue;
        }

        var app = await apps.FindByCanonicalNameAsync(client, ct);
        if (app is null) {
            return AdviseResult.Continue;
        }

        var projected = subjectService.Resolve(sub, app);
        if (projected == sub) {
            return AdviseResult.Continue;
        }

        for (var i = 0; i < claims.Count; i++) {
            if (claims[i].Type == Claims.Subject) {
                claims[i] = new(Claims.Subject, projected);
            }
        }

        return AdviseResult.Continue;
    }

    #endregion
}
