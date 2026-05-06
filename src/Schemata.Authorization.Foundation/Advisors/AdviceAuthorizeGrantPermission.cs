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

/// <summary>Order constants for <see cref="AdviceAuthorizeGrantPermission{TApp}" />.</summary>
public static class AdviceAuthorizeGrantPermission
{
    public const int DefaultOrder = AdviceAuthorizeResponseMode.DefaultOrder + 10_000_000;
}

/// <summary>Checks that the application has the <c>grant_type:authorization_code</c> permission at the authorize endpoint.</summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Even though the grant permission is also checked at the token endpoint, this advisor validates it at
///     the authorize endpoint so that misconfigured clients are rejected early.
/// </remarks>
/// <seealso cref="AdviceTokenGrantPermission{TApp}" />
public sealed class AdviceAuthorizeGrantPermission<TApp>(IApplicationManager<TApp> manager) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizeGrantPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (!await manager.HasPermissionAsync(authz.Application, PermissionPrefixes.GrantType + GrantTypes.AuthorizationCode, ct)) {
            throw new OAuthException(
                OAuthErrors.UnauthorizedClient,
                SchemataResources.GetResourceString(SchemataResources.ST4007)
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
