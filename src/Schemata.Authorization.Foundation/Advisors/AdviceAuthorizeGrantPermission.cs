using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeGrantPermission{TApp}" />.</summary>
public static class AdviceAuthorizeGrantPermission
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeResponseMode.DefaultOrder + 10_000_000;
}

/// <summary>
///     Checks the grant permissions the requested <c>response_type</c> actually needs:
///     <c>code</c> requires <c>g:authorization_code</c>; <c>token</c> / <c>id_token</c> require
///     <c>g:implicit</c>; hybrids that combine <c>code</c> with a direct token or id_token require
///     both, per the OAuth 2.0 Multiple Response Type Encoding Practices and OIDC Core hybrid
///     grant-response mapping.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Even though the grant permission is also checked at the token endpoint, this advisor validates it at
///     the authorize endpoint so that misconfigured clients are rejected early.
/// </remarks>
/// <seealso cref="AdviceRequestGrantPermission{TApp}" />
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
        var tokens = authz.Request?.ResponseType?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         ?? [];

        var hasCode  = tokens.Contains(ResponseTypes.Code, StringComparer.Ordinal);
        var hasToken = tokens.Contains(ResponseTypes.Token, StringComparer.Ordinal)
                    || tokens.Contains(ResponseTypes.IdToken, StringComparer.Ordinal);

        if (hasCode) {
            await Permissions.RequireTrueAsync(
                await manager.HasGrantTypeAsync(authz.Application, GrantTypes.AuthorizationCode, ct));
        }

        if (hasToken) {
            await Permissions.RequireTrueAsync(
                await manager.HasGrantTypeAsync(authz.Application, GrantTypes.Implicit, ct));
        }

        return AdviseResult.Continue;
    }

    #endregion

}
