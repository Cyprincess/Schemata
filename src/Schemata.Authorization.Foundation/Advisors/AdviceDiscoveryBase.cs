using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryBase : IDiscoveryAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document               ??= new();
        discovery.Document.TokenEndpoint =   $"{issuer}{Endpoints.Token}";
        discovery.Document.JwksUri       =   $"{issuer}/.well-known/{Endpoints.Jwks}";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
