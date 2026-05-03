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

/// <summary>Order constants for <see cref="AdviceDeviceCodeExchangeValidation{TApp, TToken}" />.</summary>
public static class AdviceDeviceCodeExchangeValidation
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Validates a device code token at the token endpoint before exchange,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.4">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.4: Device Access Token Request
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.5">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.5: Device Access Token Response
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Returns <c>authorization_pending</c> if the user has not yet authorised the device,
///     <c>access_denied</c> if the user denied, and <c>expired_token</c> if the device code
///     has expired. The token must be in <c>Authorized</c> status with a subject to proceed.
/// </remarks>
public sealed class AdviceDeviceCodeExchangeValidation<TApp, TToken> : IDeviceCodeExchangeAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IDeviceCodeExchangeAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceDeviceCodeExchangeValidation.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext                           ctx,
        DeviceCodeExchangeContext<TApp, TToken> exchange,
        CancellationToken                       ct = default
    ) {
        if (exchange.Token?.Type != TokenTypes.DeviceCode) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.Token.ApplicationName != exchange.Application?.Name) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.Token.ExpireTime.HasValue && exchange.Token.ExpireTime.Value <= DateTime.UtcNow) {
            throw new OAuthException(OAuthErrors.ExpiredToken, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        switch (exchange.Token.Status) {
            case TokenStatuses.Denied:
                throw new OAuthException(OAuthErrors.AccessDenied, SchemataResources.GetResourceString(SchemataResources.ST4008));
            case TokenStatuses.Valid:
                throw new OAuthException(OAuthErrors.AuthorizationPending, SchemataResources.GetResourceString(SchemataResources.ST4012));
        }

        if (exchange.Token.Status != TokenStatuses.Authorized) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (string.IsNullOrWhiteSpace(exchange.Token.Subject)) {
            throw new OAuthException(OAuthErrors.AuthorizationPending, SchemataResources.GetResourceString(SchemataResources.ST4012));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
