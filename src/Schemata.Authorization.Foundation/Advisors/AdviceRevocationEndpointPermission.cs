using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceRevocationEndpointPermission
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceRevocationEndpointPermission<TApp, TToken>(IApplicationManager<TApp> manager) : IRevocationAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IRevocationAdvisor<TApp,TToken> Members

    public int Order => AdviceRevocationEndpointPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        RevokeRequest     request,
        TToken            token,
        CancellationToken ct = default
    ) {
        if (!await manager.HasPermissionAsync(application, PermissionPrefixes.Endpoint + "revocation", ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.GetResourceString(SchemataResources.ST4007), 403);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
