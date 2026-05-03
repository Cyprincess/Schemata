using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceDiscoveryTokenExchange{TApp}" />.</summary>
public static class AdviceDiscoveryTokenExchange
{
    public const int DefaultOrder = AdviceDiscoveryRefreshToken.DefaultOrder + 10_000_000;
}

/// <summary>
///     Adds the <c>urn:ietf:params:oauth:grant-type:token-exchange</c> grant type to the discovery document if any
///     token exchange handlers are registered,
///     per <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceDiscoveryRefreshToken" />
public sealed class AdviceDiscoveryTokenExchange<TApp>(IEnumerable<ITokenExchangeHandler<TApp>> handlers) : IDiscoveryAdvisor
    where TApp : SchemataApplication
{
    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceDiscoveryTokenExchange.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        if (!handlers.Any()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        discovery.Document                     ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        discovery.Document.GrantTypesSupported.Add(GrantTypes.TokenExchange);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
