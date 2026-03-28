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

public static class AdviceDeviceAuthorizeGrantPermission
{
    public const int DefaultOrder = AdviceDeviceEndpointPermission.DefaultOrder + 10_000_000;
}

public sealed class AdviceDeviceAuthorizeGrantPermission<TApp>(IApplicationManager<TApp> apps) : IDeviceAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IDeviceAuthorizeAdvisor<TApp> Members

    public int Order => AdviceDeviceAuthorizeGrantPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        TApp                   application,
        DeviceAuthorizeRequest request,
        CancellationToken      ct = default
    ) {
        if (!await apps.HasPermissionAsync(application, PermissionPrefixes.GrantType + GrantTypes.DeviceCode, ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.GetResourceString(SchemataResources.ST4007));
        }

        return AdviseResult.Continue;
    }

    #endregion
}
