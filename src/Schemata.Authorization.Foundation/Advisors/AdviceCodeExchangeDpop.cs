using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceCodeExchangeDpop{TApp}" />.</summary>
public static class AdviceCodeExchangeDpop
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceCodeExchangeValidation.DefaultOrder - 1_000;
}

/// <summary>
///     Enforces the DPoP key committed to the authorization code at the authorize endpoint, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-10">
///     RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///     (DPoP) §10: Authorization Code Binding to a DPoP Key
/// </seealso>
///     . When the code payload carries a <c>dpop_jkt</c>, the token request must present a DPoP
///     proof whose thumbprint — published on the ambient context by
/// <see cref="AdviceRequestDpop{TApp}" /> — equals the committed key, with
/// <c>invalid_grant</c> otherwise; codes without a committed key pass through unbound.
/// </summary>
/// <remarks>
///     Ordered ahead of every other code exchange advisor so key possession is decided before any
///     other exchange validation observes the request.
/// </remarks>
public sealed class AdviceCodeExchangeDpop<TApp> : ICodeExchangeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ICodeExchangeAdvisor<TApp> Members

    public int Order => AdviceCodeExchangeDpop.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        CodeExchangeContext<TApp> exchange,
        CancellationToken         ct = default
    ) {
        if (!string.IsNullOrWhiteSpace(exchange.Payload?.DpopJkt)) {
            var bound = ctx.TryGet<DpopBinding>(out var binding) ? binding?.Jkt : null;
            if (bound != exchange.Payload.DpopJkt) {
                throw new OAuthException(
                    OAuthErrors.InvalidGrant,
                    SchemataResources.GetResourceString(SchemataResources.DPOP_JKT_MISMATCH)
                );
            }
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
