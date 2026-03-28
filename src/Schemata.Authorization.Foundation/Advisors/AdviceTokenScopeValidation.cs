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

public static class AdviceTokenScopeValidation
{
    public const int DefaultOrder = AdviceDeviceCodePolling.DefaultOrder + 10_000_000;
}

public sealed class AdviceTokenScopeValidation<TApp, TScope>(
    IApplicationManager<TApp> apps,
    IScopeManager<TScope>     scopes
) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
    where TScope : SchemataScope
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceTokenScopeValidation.DefaultOrder;

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
