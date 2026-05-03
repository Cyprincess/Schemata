using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceTokenScopeValidation{TApp, TScope}" />.</summary>
public static class AdviceTokenScopeValidation
{
    public const int DefaultOrder = AdviceDeviceCodePolling.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates that all scopes in a token request exist and are permitted for the application,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <seealso cref="AdviceAuthorizeScopeValidation{TApp, TScope}" />
public sealed class AdviceTokenScopeValidation<TApp, TScope>(
    IApplicationManager<TApp> apps,
    IScopeManager<TScope>     scopes
) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
    where TScope : SchemataScope
{
    #region ITokenRequestAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceTokenScopeValidation.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(request.Scope)) {
            return AdviseResult.Continue;
        }

        var requested = ScopeParser.Parse(request.Scope);
        if (requested.Count == 0) {
            return AdviseResult.Continue;
        }

        foreach (var s in requested) {
            var scope = await scopes.FindByNameAsync(s, ct);

            if (string.IsNullOrWhiteSpace(scope?.Name)) {
                throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
            }

            if (!await apps.HasPermissionAsync(application, PermissionPrefixes.Scope + s, ct)) {
                throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.GetResourceString(SchemataResources.ST4006));
            }
        }

        return AdviseResult.Continue;
    }

    #endregion
}
