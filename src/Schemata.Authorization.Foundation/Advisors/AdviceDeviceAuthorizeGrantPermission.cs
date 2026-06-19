using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceDeviceAuthorizeGrantPermission{TApp}" />.</summary>
public static class AdviceDeviceAuthorizeGrantPermission
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDeviceEndpointPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Checks that the application has the <c>grant_type:urn:ietf:params:oauth:grant-type:device_code</c> permission
///     for the device authorization endpoint, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.1">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.1: Device Authorization Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceDeviceEndpointPermission{TApp}" />
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
        await PermissionAdvice.RequireAsync(apps, application, PermissionPrefixes.GrantType + GrantTypes.DeviceCode, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
