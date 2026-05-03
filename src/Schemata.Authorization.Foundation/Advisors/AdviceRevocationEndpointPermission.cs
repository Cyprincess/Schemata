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

/// <summary>Order constants for <see cref="AdviceRevocationEndpointPermission{TApp, TToken}" />.</summary>
public static class AdviceRevocationEndpointPermission
{
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
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <seealso cref="AdviceRevocationTokenValidation{TApp, TToken}" />
public sealed class AdviceRevocationEndpointPermission<TApp, TToken>(IApplicationManager<TApp> manager) : IRevocationAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IRevocationAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceRevocationEndpointPermission.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        RevokeRequest     request,
        TToken            token,
        CancellationToken ct = default
    ) {
        if (!await manager.HasPermissionAsync(application, PermissionPrefixes.Endpoint + "revocation", ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.GetResourceString(SchemataResources.ST4007), 403);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
