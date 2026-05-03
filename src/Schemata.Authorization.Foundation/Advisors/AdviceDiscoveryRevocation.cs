using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>revocation_endpoint</c> to the discovery document,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html#section-3">
///         RFC 7009: OAuth 2.0 Token Revocation §3:
///         Implementation Note
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryIntrospection" />
public sealed class AdviceDiscoveryRevocation : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryIntrospection.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document                    ??= new();
        discovery.Document.RevocationEndpoint =   $"{issuer}{Endpoints.Revoke}";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
