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
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceCodeExchangeValidation{TApp}" />.</summary>
public static class AdviceCodeExchangeValidation
{
    /// <summary>The default advisor ordering value.</summary>
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
/// <remarks>
///     Cross-checks the code against its stored payload: the code must be of type <c>authorization_code</c>,
///     belong to the authenticated application, and not be expired or revoked. Application references use the
///     application's canonical name because persisted tokens store canonical references.
/// </remarks>
public sealed class AdviceCodeExchangeValidation<TApp>(
    ITokenStore<SchemataToken>   tokens,
    TimeProvider?         time = null
) : ICodeExchangeAdvisor<TApp>
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region ICodeExchangeAdvisor<TApp> Members

    public int Order => AdviceCodeExchangeValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        CodeExchangeContext<TApp> exchange,
        CancellationToken                 ct = default
    ) {
        if (exchange.CodeToken?.Type != TokenTypes.AuthorizationCode) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.CodeToken.Application != exchange.Application?.CanonicalName) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.CodeToken.ExpireTime.HasValue && exchange.CodeToken.ExpireTime.Value <= _time.GetUtcNow().UtcDateTime) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.CodeToken.Status != TokenStatuses.Valid) {
            // A redeemed code presented again is a replay: revoke every token derived
            // from the same authorization grant before rejecting the exchange.
            // See RFC 6749 §4.1.2 and §10.5.
            if (exchange.CodeToken.Status == TokenStatuses.Redeemed
             && !string.IsNullOrWhiteSpace(exchange.CodeToken.Authorization)) {
                await tokens.RevokeByAuthorizationAsync(exchange.CodeToken.Authorization, ct);
            }

            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }
        if (string.IsNullOrWhiteSpace(exchange.Payload?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.Application?.ClientId != exchange.Payload.ClientId) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        if (exchange.Request?.RedirectUri != exchange.Payload.RedirectUri) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        return AdviseResult.Continue;
    }

    #endregion
}
