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

/// <summary>Order constants for <see cref="AdviceTokenGrantPermission{TApp}" />.</summary>
public static class AdviceTokenGrantPermission
{
    public const int DefaultOrder = AdviceTokenEndpointPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Checks that the application has the requested grant type permission at the token endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceTokenEndpointPermission{TApp}" />
/// <seealso cref="AdviceDeviceCodePolling{TApp}" />
public sealed class AdviceTokenGrantPermission<TApp>(IApplicationManager<TApp> manager) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceTokenGrantPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        var grant = request.GrantType;

        if (!await manager.HasPermissionAsync(application, PermissionPrefixes.GrantType + grant, ct)) {
            throw new OAuthException(
                OAuthErrors.UnauthorizedClient,
                SchemataResources.GetResourceString(SchemataResources.ST4007)
            );
        }

        return AdviseResult.Continue;
    }

    #endregion
}
