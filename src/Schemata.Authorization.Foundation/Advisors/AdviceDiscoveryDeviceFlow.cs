using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryDeviceFlow : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryCodeFlow.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

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
