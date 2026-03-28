using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Extensions;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceEmailClaimDestination : IDestinationAdvisor
{
    public const int DefaultOrder = AdviceProfileClaimDestination.DefaultOrder + 10_000_000;

    #region IDestinationAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        Claim             claim,
        HashSet<string>   destinations,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        switch (claim.Type) {
            case Claims.Email:
            case Claims.EmailVerified:
                destinations.Add(ClaimDestinations.AccessToken);

                if (!principal.HasScope(Scopes.Email)) {
                    return Task.FromResult(AdviseResult.Handle);
                }

                destinations.Add(ClaimDestinations.IdentityToken);
                destinations.Add(ClaimDestinations.UserInfo);

                return Task.FromResult(AdviseResult.Handle);
            default:
                return Task.FromResult(AdviseResult.Continue);
        }
    }

    #endregion
}
