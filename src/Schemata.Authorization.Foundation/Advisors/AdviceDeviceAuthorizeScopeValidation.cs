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

public static class AdviceDeviceAuthorizeScopeValidation
{
    public const int DefaultOrder = AdviceDeviceAuthorizeGrantPermission.DefaultOrder + 10_000_000;
}

public sealed class AdviceDeviceAuthorizeScopeValidation<TApp, TScope>(
    IApplicationManager<TApp> apps,
    IScopeManager<TScope>     scopes
) : IDeviceAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
    where TScope : SchemataScope
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
            throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
        }

        foreach (var s in requested) {
            var scope = await scopes.FindByNameAsync(s, ct);

            if (string.IsNullOrWhiteSpace(scope?.Name)) {
                throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
            }

            if (!await apps.HasPermissionAsync(application, PermissionPrefixes.Scope + s, ct)) {
                throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
            }
        }

        return AdviseResult.Continue;
    }

    #endregion
}
