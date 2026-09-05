using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRefreshTokenDpop{TApp}" />.</summary>
public static class AdviceRefreshTokenDpop
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceRefreshTokenValidation.DefaultOrder - 1_000;
}

/// <summary>
///     Enforces proof-of-possession on refresh requests for DPoP-bound refresh tokens, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5">
///     RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///     (DPoP) §5: DPoP Access Token Request
/// </seealso>
///     . A refresh token minted with a <c>cnf.jkt</c> binding must be presented with a proof
///     signed by that same key on every use: the presented thumbprint — published on the ambient
///     context by <see cref="AdviceRequestDpop{TApp}" /> — must equal the bound key, with
///     <c>invalid_dpop_proof</c> otherwise. Unbound tokens pass through.
/// </summary>
/// <remarks>
///     Ordered ahead of every other refresh token advisor so key possession is decided before any
///     other refresh validation observes the request.
/// </remarks>
public sealed class AdviceRefreshTokenDpop<TApp> : IRefreshTokenAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IRefreshTokenAdvisor<TApp> Members

    public int Order => AdviceRefreshTokenDpop.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                 ctx,
        RefreshTokenContext<TApp>     exchange,
        CancellationToken             ct = default
    ) {
        var principal = exchange.Principal;
        if (principal is null) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT)
            );
        }

        var jkt = DPopProofValidator.ReadBoundThumbprint(principal);
        if (!string.IsNullOrWhiteSpace(jkt)) {
            var bound = ctx.TryGet<DpopBinding>(out var binding) ? binding?.Jkt : null;
            if (bound != jkt) {
                throw new OAuthException(
                    OAuthErrors.InvalidDpopProof,
                    SchemataResources.GetResourceString(SchemataResources.DPOP_REFRESH_PROOF_REQUIRED)
                );
            }
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
