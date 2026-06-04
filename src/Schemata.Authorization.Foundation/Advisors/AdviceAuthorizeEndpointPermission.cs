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

/// <summary>Order constants for <see cref="AdviceAuthorizeEndpointPermission{TApp}" />.</summary>
public static class AdviceAuthorizeEndpointPermission
{
    public const int DefaultOrder = AdviceAuthorizeClientAndRedirect.DefaultOrder + 10_000_000;
}

/// <summary>
///     Checks that the application has the <c>endpoint:authorization</c> permission before the authorize endpoint
///     processes the request.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceTokenEndpointPermission{TApp}" />
public sealed class AdviceAuthorizeEndpointPermission<TApp>(IApplicationManager<TApp> manager) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizeEndpointPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (!await manager.HasPermissionAsync(authz.Application, PermissionPrefixes.Endpoint + Endpoints.Authorize, ct)) {
            throw new OAuthException(
                OAuthErrors.UnauthorizedClient,
                SchemataResources.GetResourceString(SchemataResources.ST4007),
                403
            ) {
                RedirectUri  = authz.Request?.RedirectUri,
                State        = authz.Request?.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        return AdviseResult.Continue;
    }

    #endregion
}
