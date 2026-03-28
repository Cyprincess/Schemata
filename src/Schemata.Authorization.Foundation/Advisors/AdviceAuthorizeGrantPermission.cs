using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceAuthorizeGrantPermission
{
    public const int DefaultOrder = AdviceAuthorizeResponseMode.DefaultOrder + 10_000_000;
}

public sealed class AdviceAuthorizeGrantPermission<TApp>(IApplicationManager<TApp> manager) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeGrantPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (!await manager.HasPermissionAsync(authz.Application, PermissionPrefixes.GrantType + GrantTypes.AuthorizationCode, ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.GetResourceString(SchemataResources.ST4007)) {
                RedirectUri  = authz.Request?.RedirectUri,
                State        = authz.Request?.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        return AdviseResult.Continue;
    }

    #endregion
}
