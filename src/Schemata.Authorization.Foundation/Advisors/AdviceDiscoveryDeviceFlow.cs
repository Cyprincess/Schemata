using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the Device Authorization Grant metadata to the discovery document: <c>device_authorization_endpoint</c>
///     and <c>urn:ietf:params:oauth:grant-type:device_code</c>,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-4">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §4: Discovery Metadata
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryCodeFlow" />
public sealed class AdviceDiscoveryDeviceFlow : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryCodeFlow.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document                             ??= new();
        discovery.Document.GrantTypesSupported         ??= [];
        discovery.Document.DeviceAuthorizationEndpoint =   $"{issuer}{Endpoints.Device}";
        discovery.Document.GrantTypesSupported.Add(GrantTypes.DeviceCode);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
