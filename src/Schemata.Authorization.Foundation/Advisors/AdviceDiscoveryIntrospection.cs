using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>introspection_endpoint</c> to the discovery document,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2">
///         RFC 7662: OAuth 2.0 Token Introspection §2:
///         Introspection Endpoint
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryTokenExchange" />
public sealed class AdviceDiscoveryIntrospection : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryTokenExchange.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document                       ??= new();
        discovery.Document.IntrospectionEndpoint =   $"{issuer}{Endpoints.Introspect}";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
