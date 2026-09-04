using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRefreshTokenValidation{TApp}" />.</summary>
public static class AdviceRefreshTokenValidation
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Validates a refresh token at token exchange: checks type, application, expiry, status, and subject,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §6: Refreshing an Access Token
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
public sealed class AdviceRefreshTokenValidation<TApp>(TimeProvider? time = null) : IRefreshTokenAdvisor<TApp>
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IRefreshTokenAdvisor<TApp> Members

    public int Order => AdviceRefreshTokenValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        RefreshTokenContext<TApp> exchange,
        CancellationToken                 ct = default
    ) {
        if (exchange.Token?.Type != TokenTypes.RefreshToken) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.Token.Application != exchange.Application?.CanonicalName) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.Token.ExpireTime.HasValue && exchange.Token.ExpireTime.Value <= _time.GetUtcNow().UtcDateTime) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.Token.Status != TokenStatuses.Valid) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (string.IsNullOrWhiteSpace(exchange.Token.Parent)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
