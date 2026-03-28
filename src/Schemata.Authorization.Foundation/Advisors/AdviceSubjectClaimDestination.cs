using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceSubjectClaimDestination : IDestinationAdvisor
{
    public const int DefaultOrder = Orders.Base;

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
            case Claims.Subject:
                destinations.Add(ClaimDestinations.AccessToken);
                destinations.Add(ClaimDestinations.IdentityToken);
                destinations.Add(ClaimDestinations.UserInfo);
                return Task.FromResult(AdviseResult.Handle);
            case Claims.ClientId:
                destinations.Add(ClaimDestinations.AccessToken);
                return Task.FromResult(AdviseResult.Handle);
            case Claims.Audience:
                destinations.Add(ClaimDestinations.AccessToken);
                destinations.Add(ClaimDestinations.IdentityToken);
                return Task.FromResult(AdviseResult.Handle);
            default:
                return Task.FromResult(AdviseResult.Continue);
        }
    }

    #endregion
}
