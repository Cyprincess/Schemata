using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Projects the authentication evidence in the trusted <see cref="AuthorizationGrantContext" />
///     onto the assembled claim set. The advisor tags <c>acr</c>, <c>amr</c>, and <c>auth_time</c>
///     for access and identity tokens; it never resolves a provider or reconstructs a continuation
///     from JWT claims.
/// </summary>
/// <remarks>
///     Every member is optional. <c>amr</c> uses a JSON array, and <c>auth_time</c> uses an integer
///     JSON value. An empty persisted context removes bare transport copies and emits no evidence.
/// </remarks>
public sealed class AdviceClaimsAuthenticationContext : IClaimsAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceClaimsAudience.DefaultOrder + 10_000_000;

    #region IClaimsAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, Schemata.Authorization.Skeleton.Models.AuthorizationClaimContext issuance, CancellationToken ct = default) {
        AuthenticationContextExtensions.Apply(claims, issuance.Grant?.Authentication, destinations: true);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
