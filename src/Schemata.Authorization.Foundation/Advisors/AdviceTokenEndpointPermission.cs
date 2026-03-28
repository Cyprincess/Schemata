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

public static class AdviceTokenEndpointPermission
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceTokenEndpointPermission<TApp>(IApplicationManager<TApp> manager) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceTokenEndpointPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        if (!await manager.HasPermissionAsync(application, PermissionPrefixes.Endpoint + "token", ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.GetResourceString(SchemataResources.ST4007), 403);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
