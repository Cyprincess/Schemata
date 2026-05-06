using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRevocationTokenValidation{TApp, TToken}" />.</summary>
public static class AdviceRevocationTokenValidation
{
    public const int DefaultOrder = AdviceRevocationEndpointPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates that the token to revoke belongs to the requesting application, is revocable (access token or
///     refresh token), and is not already revoked, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html#section-2.2">
///         RFC 7009: OAuth 2.0 Token Revocation
///         §2.2: Revocation Response
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Only access tokens and refresh tokens are revocable. Rejecting an
///     already-revoked or foreign application's token with <see cref="AdviseResult.Block" /> results in
///     an indistinguishable response.
/// </remarks>
/// <seealso cref="AdviceRevocationEndpointPermission{TApp, TToken}" />
public sealed class AdviceRevocationTokenValidation<TApp, TToken> : IRevocationAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IRevocationAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
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
