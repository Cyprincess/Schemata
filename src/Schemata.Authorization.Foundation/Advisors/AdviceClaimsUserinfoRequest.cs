using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceClaimsUserinfoRequest : IClaimsAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceClaimsAudience.DefaultOrder + 5_000_000;

    /// <summary>The default advisor ordering value.</summary>
    public int Order => DefaultOrder;

    #region IClaimsAdvisor Members

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        var existing = claims.FirstOrDefault(c => c.Type == Claims.UserinfoRequest);
        if (existing is not null) {
            // A refresh continuation re-presents the persisted request; keep it on the token.
            existing.Properties[ClaimDestinations.AccessToken] = Parameters.Token;
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!ctx.TryGet<ClaimsRequest>(out var request) || request.Userinfo is not { Count: > 0 } names) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var claim = new Claim(Claims.UserinfoRequest, string.Join(' ', names.Keys));
        claim.Properties[ClaimDestinations.AccessToken] = Parameters.Token;
        claims.Add(claim);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
