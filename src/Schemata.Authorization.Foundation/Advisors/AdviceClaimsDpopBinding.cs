using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Mints the <c>cnf.jkt</c> confirmation claim for a DPoP-bound token, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-6.1">
///         RFC 9449: OAuth 2.0 Demonstrating Proof of
///         Possession (DPoP) §6.1: JWK Thumbprint Confirmation Method
///     </seealso>
///     , when a <see cref="DpopBinding" /> is present on the ambient
///     <see cref="AdviceContext" />. Registered by the DPoP flow feature; without the
///     feature no binding is ever published and this advisor is absent, so unbound Bearer
///     issuance is unaffected.
/// </summary>
public sealed class AdviceClaimsDpopBinding : IClaimsAdvisor
{
    /// <summary>The default advisor ordering value; audience first, then the confirmation.</summary>
    public const int DefaultOrder = AdviceClaimsAudience.DefaultOrder + 7_000_000;

    /// <summary>The default advisor ordering value.</summary>
    public int Order => DefaultOrder;

    #region IClaimsAdvisor Members

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        if (!ctx.TryGet<DpopBinding>(out var binding) || binding is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var cnf = new Claim(Claims.Cnf, $"{{\"jkt\":\"{binding.Jkt}\"}}", JsonClaimValueTypes.Json);
        cnf.Properties[ClaimDestinations.AccessToken] = Parameters.Token;
        claims.Add(cnf);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
