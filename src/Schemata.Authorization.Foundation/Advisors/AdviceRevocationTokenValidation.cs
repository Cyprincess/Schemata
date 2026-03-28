using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceRevocationTokenValidation
{
    public const int DefaultOrder = AdviceRevocationEndpointPermission.DefaultOrder + 10_000_000;
}

public sealed class AdviceRevocationTokenValidation<TApp, TToken> : IRevocationAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IRevocationAdvisor<TApp,TToken> Members

    public int Order => AdviceRevocationTokenValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        RevokeRequest     request,
        TToken            token,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(token.ApplicationName) || token.ApplicationName != application.Name) {
            return Task.FromResult(AdviseResult.Block);
        }

        if (token.Type != TokenTypes.AccessToken && token.Type != TokenTypes.RefreshToken) {
            return Task.FromResult(AdviseResult.Block);
        }

        if (token.Status == TokenStatuses.Revoked) {
            return Task.FromResult(AdviseResult.Block);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
