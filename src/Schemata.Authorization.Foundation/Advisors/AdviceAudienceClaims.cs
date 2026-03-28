using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceAudienceClaims : IClaimsAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IClaimsAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        if (claims.Any(c => c.Type == Claims.Audience)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var client = claims.FirstOrDefault(c => c.Type == Claims.ClientId)?.Value;
        if (!string.IsNullOrWhiteSpace(client)) {
            claims.Add(new(Claims.Audience, client));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
