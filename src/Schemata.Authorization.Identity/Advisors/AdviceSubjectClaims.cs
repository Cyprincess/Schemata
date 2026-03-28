using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Identity.Advisors;

public sealed class AdviceSubjectClaims(ISubjectProvider subjects) : IClaimsAdvisor
{
    public const int DefaultOrder = AdviceAudienceClaims.DefaultOrder + 10_000_000;

    #region IClaimsAdvisor Members

    public int Order => DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        var sub = claims.FirstOrDefault(c => c.Type == Claims.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(sub)) {
            return AdviseResult.Continue;
        }

        var baseline      = new HashSet<string>(claims.Select(c => c.Type));
        var subject = await subjects.GetClaimsAsync(sub, ct);

        foreach (var claim in subject) {
            if (baseline.Contains(claim.Type)) {
                continue;
            }

            claims.Add(claim);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
