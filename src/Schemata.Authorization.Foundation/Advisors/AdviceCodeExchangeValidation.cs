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

/// <summary>Order constants for <see cref="AdviceCodeExchangeValidation{TApp, TToken}" />.</summary>
public static class AdviceCodeExchangeValidation
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Validates the authorization code token before exchange, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Cross-checks the code against its stored payload: the code must be of type <c>authorization_code</c>,
///     belong to the authenticated application, and not be expired or revoked. The application's ClientId is
///     used for matching because <c>request.ClientId</c> is absent for <c>client_secret_basic</c> authentication
///     (RFC 6749 §2.3.1).
/// </remarks>
/// <seealso cref="AdviceCodeExchangePkce{TApp, TToken}" />
public sealed class AdviceCodeExchangeValidation<TApp, TToken> : ICodeExchangeAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region ICodeExchangeAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceCodeExchangeValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        CodeExchangeContext<TApp, TToken> exchange,
        CancellationToken                 ct = default
    ) {
        if (exchange.CodeToken?.Type != TokenTypes.AuthorizationCode) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        if (exchange.CodeToken.ApplicationName != exchange.Application?.Name) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        if (exchange.CodeToken.ExpireTime.HasValue && exchange.CodeToken.ExpireTime.Value <= DateTime.UtcNow) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        if (exchange.CodeToken.Status != TokenStatuses.Valid) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        if (string.IsNullOrWhiteSpace(exchange.Payload?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        if (exchange.Application?.ClientId != exchange.Payload.ClientId) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        if (exchange.Request?.RedirectUri != exchange.Payload.RedirectUri) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
