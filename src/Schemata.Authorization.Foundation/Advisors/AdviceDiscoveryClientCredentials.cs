using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>client_credentials</c> grant type to the discovery document,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.4">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.4: Client Credentials Grant
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryDeviceFlow" />
public sealed class AdviceDiscoveryClientCredentials : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryDeviceFlow.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                     ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        discovery.Document.GrantTypesSupported.Add(GrantTypes.ClientCredentials);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
