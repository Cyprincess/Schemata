using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeScopeValidation{TApp, TScope}" />.</summary>
public static class AdviceAuthorizeScopeValidation
{
    public const int DefaultOrder = AdviceAuthorizeGrantPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates that all requested scopes exist and are permitted for the application at the authorize endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.2.1: Error Response
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <seealso cref="AdviceTokenScopeValidation{TApp, TScope}" />
public sealed class AdviceAuthorizeScopeValidation<TApp, TScope>(
    IApplicationManager<TApp> apps,
    IScopeManager<TScope>     scopes
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
    where TScope : SchemataScope
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizeScopeValidation.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (string.IsNullOrWhiteSpace(authz.Request?.Scope)) {
            return AdviseResult.Continue;
        }

        var requested = ScopeParser.Parse(authz.Request.Scope);
        if (requested.Count == 0) {
            return AdviseResult.Continue;
        }

        foreach (var s in requested) {
            var scope = await scopes.FindByNameAsync(s, ct);

            if (string.IsNullOrWhiteSpace(scope?.Name)) {
                throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
            }

            if (!await apps.HasPermissionAsync(authz.Application, PermissionPrefixes.Scope + s, ct)) {
                throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
            }
        }

        return AdviseResult.Continue;
    }

    #endregion
}
