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

/// <summary>Order constants for <see cref="AdviceTokenEndpointPermission{TApp}" />.</summary>
public static class AdviceTokenEndpointPermission
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>Checks that the application has the <c>endpoint:token</c> permission to access the token endpoint.</summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceTokenGrantPermission{TApp}" />
public sealed class AdviceTokenEndpointPermission<TApp>(IApplicationManager<TApp> manager) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceTokenEndpointPermission.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        if (!await manager.HasPermissionAsync(application, PermissionPrefixes.Endpoint + Endpoints.Token, ct)) {
            throw new OAuthException(
                OAuthErrors.UnauthorizedClient,
                SchemataResources.GetResourceString(SchemataResources.ST4007),
                403
            );
        }

        return AdviseResult.Continue;
    }

    #endregion
}
