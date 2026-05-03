using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRefreshTokenValidation{TApp, TToken}" />.</summary>
public static class AdviceRefreshTokenValidation
{
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
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <seealso cref="AdviceCodeExchangeValidation{TApp, TToken}" />
public sealed class AdviceRefreshTokenValidation<TApp, TToken> : IRefreshTokenAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IRefreshTokenAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceRefreshTokenValidation.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        RefreshTokenContext<TApp, TToken> exchange,
        CancellationToken                 ct = default
    ) {
        if (exchange.Token?.Type != TokenTypes.RefreshToken) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.Token.ApplicationName != exchange.Application?.Name) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.Token.ExpireTime.HasValue && exchange.Token.ExpireTime.Value <= DateTime.UtcNow) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.Token.Status != TokenStatuses.Valid) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (string.IsNullOrWhiteSpace(exchange.Token.Subject)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
