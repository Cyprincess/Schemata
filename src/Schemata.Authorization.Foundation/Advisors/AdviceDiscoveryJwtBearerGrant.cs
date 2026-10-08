using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>urn:ietf:params:oauth:grant-type:jwt-bearer</c> grant type to the discovery
///     document, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-3.1">RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0 Client Authentication and Authorization Grants §3.1: Authorization Grant Processing</seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryClientCredentials" />
public sealed class AdviceDiscoveryJwtBearerGrant : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryAcrValues.DefaultOrder + 10_000_000;

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                     ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        discovery.Document.GrantTypesSupported.Add(GrantTypes.JwtBearer);

        return Task.FromResult(AdviseResult.Continue);
    }
}
