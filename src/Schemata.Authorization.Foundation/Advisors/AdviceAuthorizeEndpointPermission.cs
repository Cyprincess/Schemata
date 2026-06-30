using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeEndpointPermission{TApp}" />.</summary>
public static class AdviceAuthorizeEndpointPermission
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeClientAndRedirect.DefaultOrder + 10_000_000;
}

/// <summary>
///     Checks that the application has the <c>endpoint:authorization</c> permission before the authorize endpoint
///     processes the request.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceRequestEndpointPermission{TApp}" />
public sealed class AdviceAuthorizeEndpointPermission<TApp>(IApplicationManager<TApp> manager) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeEndpointPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        await PermissionAdvice.RequireAsync(
            manager, authz.Application, PermissionPrefixes.Endpoint + Endpoints.Authorize, ct,
            code: 403,
            configure: exception => {
                exception.RedirectUri  = authz.Request?.RedirectUri;
                exception.State        = authz.Request?.State;
                exception.ResponseMode = authz.ResponseMode;
            });

        return AdviseResult.Continue;
    }

    #endregion
}
