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

public static class AdviceRefreshTokenValidation
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceRefreshTokenValidation<TApp, TToken> : IRefreshTokenAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IRefreshTokenAdvisor<TApp,TToken> Members

    public int Order => AdviceRefreshTokenValidation.DefaultOrder;

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
