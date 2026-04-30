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

/// <summary>Order constants for <see cref="AdvicePairwiseProjection{TApp}" />.</summary>
public static class AdvicePairwiseProjection
{
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Projects the <c>sub</c> claim to a pairwise identifier when the application uses a pairwise subject type,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
///         OpenID Connect Core 1.0 §8: Subject
///         Identifier Types
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Pairwise subject identifiers prevent correlation of users across different clients.
///     The projection is delegated to <see cref="ISubjectIdentifierService" /> and applied only when
///     the sectored identifier or subject type requires it.
/// </remarks>
/// <seealso cref="AdviceAudienceClaims" />
public sealed class AdvicePairwiseProjection<TApp>(
    IApplicationManager<TApp> apps,
    ISubjectIdentifierService subjectService
) : IClaimsAdvisor
    where TApp : SchemataApplication
{
    #region IClaimsAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdvicePairwiseProjection.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        var sub    = claims.FirstOrDefault(c => c.Type == Claims.Subject)?.Value;
        var client = claims.FirstOrDefault(c => c.Type == Claims.ClientId)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(client)) {
            return AdviseResult.Continue;
        }

        var app = await apps.FindByClientIdAsync(client, ct);
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
