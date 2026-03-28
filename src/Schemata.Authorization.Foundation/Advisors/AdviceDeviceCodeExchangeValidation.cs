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

public static class AdviceDeviceCodeExchangeValidation
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceDeviceCodeExchangeValidation<TApp, TToken> : IDeviceCodeExchangeAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IDeviceCodeExchangeAdvisor<TApp,TToken> Members

    public int Order => AdviceDeviceCodeExchangeValidation.DefaultOrder;

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
