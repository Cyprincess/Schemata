using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>refresh_token</c> grant type to the discovery document,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §6: Refreshing an Access Token
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryClientCredentials" />
public sealed class AdviceDiscoveryRefreshToken : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryClientCredentials.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                     ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        discovery.Document.GrantTypesSupported.Add(GrantTypes.RefreshToken);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
