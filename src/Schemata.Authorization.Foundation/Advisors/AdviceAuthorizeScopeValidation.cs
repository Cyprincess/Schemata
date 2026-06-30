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

/// <summary>Order constants for <see cref="AdviceAuthorizeScopeValidation{TApp}" />.</summary>
public static class AdviceAuthorizeScopeValidation
{
    /// <summary>The default advisor ordering value.</summary>
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
/// <seealso cref="AdviceRequestScopeValidation{TApp}" />
public sealed class AdviceAuthorizeScopeValidation<TApp>(
    IApplicationManager<TApp> apps
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeScopeValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var requested = ScopeParser.Parse(authz.Request?.Scope);

        // OIDC Core §3.1.2.1: scope REQUIRED, MUST contain "openid" when response_type includes id_token.
        if (!string.IsNullOrWhiteSpace(authz.Request?.ResponseType)
         && authz.Request.ResponseType.Contains(ResponseTypes.IdToken)
         && !requested.Contains(Scopes.OpenId)) {
            throw new OAuthException(
                OAuthErrors.InvalidScope,
                SchemataResources.GetResourceString(SchemataResources.INVALID_SCOPE)
            ) {
                RedirectUri  = authz.Request?.RedirectUri,
                State        = authz.Request?.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        if (requested.Count == 0) {
            return AdviseResult.Continue;
        }

        foreach (var s in requested) {
            await PermissionAdvice.RequireAsync(apps, authz.Application, PermissionPrefixes.Scope + s, ct,
                error: OAuthErrors.InvalidScope, resource: SchemataResources.INVALID_SCOPE,
                configure: exception => {
                    exception.RedirectUri  = authz.Request?.RedirectUri;
                    exception.State        = authz.Request?.State;
                    exception.ResponseMode = authz.ResponseMode;
                });
        }

        return AdviseResult.Continue;
    }

    #endregion
}
