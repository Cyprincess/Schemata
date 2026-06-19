using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Extensions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Claim destination advisor for role/permission claims used by <see cref="IPermissionMatcher" />.
///     Role claims are included in access tokens for downstream resource authorization checks.
/// </summary>
public sealed class AdviceRoleClaimDestination : IDestinationAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAddressClaimDestination.DefaultOrder + 10_000_000;

    #region IDestinationAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        Claim             claim,
        HashSet<string>   destinations,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (claim.Type != Claims.Role) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!principal.HasScope(Scopes.Role)) {
            return Task.FromResult(AdviseResult.Handle);
        }

        destinations.Add(ClaimDestinations.AccessToken);

        destinations.Add(ClaimDestinations.IdentityToken);
        destinations.Add(ClaimDestinations.UserInfo);

        return Task.FromResult(AdviseResult.Handle);
    }

    #endregion
}
