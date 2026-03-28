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

public static class AdviceCodeExchangeValidation
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceCodeExchangeValidation<TApp, TToken> : ICodeExchangeAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region ICodeExchangeAdvisor<TApp,TToken> Members

    public int Order => AdviceCodeExchangeValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        CodeExchangeContext<TApp, TToken> exchange,
        CancellationToken                 ct = default
    ) {
        if (exchange.CodeToken?.Type != TokenTypes.AuthorizationCode) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.CodeToken.ApplicationName != exchange.Application?.Name) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.CodeToken.ExpireTime.HasValue && exchange.CodeToken.ExpireTime.Value <= DateTime.UtcNow) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.CodeToken.Status != TokenStatuses.Valid) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (string.IsNullOrWhiteSpace(exchange.Payload?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        // Use the authenticated application's ClientId — request.ClientId is absent for client_secret_basic.
        if (exchange.Application?.ClientId != exchange.Payload.ClientId) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        if (exchange.Request?.RedirectUri != exchange.Payload.RedirectUri) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
