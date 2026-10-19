using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceTokenAuthorizationDetails{TApp}" />.</summary>
public static class AdviceTokenAuthorizationDetails
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceCodeExchangePkce.DefaultOrder + 10_000_000;
}

/// <summary>
///     Applies RFC 9396
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-6.1">
///         §6.1 grant-specific authorization details
///     </seealso>
///     on the authorization code and refresh token exchanges: the request's
///     <c>authorization_details</c> narrows the grant the presented credential carries, and the
///     resulting actual set is written to the explicit exchange payload for the handler.
///     An omitted parameter adopts the grant's current
///     set, so refresh lineage retains the prior actual narrowing rather than the original
///     authorization request.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Registered only by the rich authorization requests feature; without it the bound
///     parameter stays inert. The advisor consumes the exchange contexts after credential
///     validation, so the original grant is authoritative — the code payload's persisted
///     details on code exchange, the refresh token's own <c>authorization_details</c> claim on
///     refresh. The persisted authorization record is never rewritten.
/// </remarks>
public sealed class AdviceTokenAuthorizationDetails<TApp>(
    AuthorizationDetailsService details
) : ICodeExchangeAdvisor<TApp>, IRefreshTokenAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ICodeExchangeAdvisor<TApp> Members

    public int Order => AdviceTokenAuthorizationDetails.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        CodeExchangeContext<TApp> exchange,
        CancellationToken         ct = default
    ) {
        exchange.AuthorizationDetails = Narrow(exchange.Payload?.AuthorizationDetails, exchange.Request?.AuthorizationDetails, ct);
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    #region IRefreshTokenAdvisor<TApp> Members

    public Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        RefreshTokenContext<TApp> exchange,
        CancellationToken         ct = default
    ) {
        exchange.AuthorizationDetails = Narrow(ReadGrant(exchange.Principal), exchange.Request?.AuthorizationDetails, ct);
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    private string? Narrow(string? granted, string? requested, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(granted) && string.IsNullOrWhiteSpace(requested)) return null;
        return details.Narrow(granted, requested, ct).ToJsonString();
    }

    // JWT validation splits a JSON-array claim into one claim per element; reassemble the grant
    // set from either shape (see AdviceIntrospectionAuthorizationDetails for the same dual shape).
    private static string? ReadGrant(ClaimsPrincipal? principal) {
        var claims = principal?.FindAll(Claims.AuthorizationDetails).ToList();
        if (claims is not { Count: > 0 }) {
            return null;
        }

        if (claims.Count == 1 && claims[0].Value.TrimStart().StartsWith('[')) {
            return claims[0].Value;
        }

        return "[" + string.Join(",", claims.Select(c => c.Value)) + "]";
    }
}
