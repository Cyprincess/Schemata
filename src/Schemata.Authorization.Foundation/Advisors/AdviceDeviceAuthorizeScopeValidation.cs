using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceDeviceAuthorizeScopeValidation{TApp}" />.</summary>
public static class AdviceDeviceAuthorizeScopeValidation
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDeviceAuthorizeGrantPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates scopes at the device authorization endpoint, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.1">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.1: Device Authorization Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Rejects the <c>openid</c> scope explicitly because the OIDC flow uses a different interaction model.
/// </remarks>
/// <seealso cref="AdviceDeviceAuthorizeGrantPermission{TApp}" />
public sealed class AdviceDeviceAuthorizeScopeValidation<TApp>(
    IApplicationManager<TApp> apps
) : IDeviceAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IDeviceAuthorizeAdvisor<TApp> Members

    public int Order => AdviceDeviceAuthorizeScopeValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        TApp                   application,
        DeviceAuthorizeRequest request,
        CancellationToken      ct = default
    ) {
        if (string.IsNullOrWhiteSpace(request.Scope)) {
            return AdviseResult.Continue;
        }

        var requested = ScopeParser.Parse(request.Scope);
        if (requested.Count == 0) {
            return AdviseResult.Continue;
        }

        if (requested.Contains(Scopes.OpenId)) {
            throw new OAuthException(
                OAuthErrors.InvalidScope,
                SchemataResources.GetResourceString(SchemataResources.INVALID_SCOPE)
            );
        }

        foreach (var s in requested) {
            await PermissionAdvice.RequireAsync(apps, application, PermissionPrefixes.Scope + s, ct,
                OAuthErrors.InvalidScope, SchemataResources.INVALID_SCOPE);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
