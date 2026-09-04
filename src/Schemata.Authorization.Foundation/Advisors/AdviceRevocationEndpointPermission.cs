using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRevocationEndpointPermission{TApp}" />.</summary>
public static class AdviceRevocationEndpointPermission
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Checks that the application has the <c>endpoint:revocation</c> permission to access the revocation endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html#section-3">
///         RFC 7009: OAuth 2.0 Token Revocation §3:
///         Implementation Note
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
public sealed class AdviceRevocationEndpointPermission<TApp>(IApplicationManager<TApp> manager) : IRevocationAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IRevocationAdvisor<TApp> Members

    public int Order => AdviceRevocationEndpointPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        RevokeRequest     request,
        SchemataToken            token,
        CancellationToken ct = default
    ) {
        await PermissionAdvice.RequireAsync(manager, application, PermissionPrefixes.Endpoint + Endpoints.Revoke, ct, code: 403);

        return AdviseResult.Continue;
    }

    #endregion
}
