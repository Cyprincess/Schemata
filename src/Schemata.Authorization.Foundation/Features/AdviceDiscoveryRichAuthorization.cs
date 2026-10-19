using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Publishes <c>authorization_details_types_supported</c> from the registered type descriptors, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-10">
///     RFC 9396: OAuth 2.0 Rich Authorization
///     Requests §10: Metadata
/// </seealso>
///     .
/// </summary>
public sealed class AdviceDiscoveryRichAuthorization(IEnumerable<IAuthorizationDetailTypeDescriptor> descriptors) : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryJwtBearerGrant.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, DiscoveryContext discovery, CancellationToken ct = default) {
        var types = descriptors.Select(d => d.Type).Distinct().ToList();
        if (types.Count > 0) {
            discovery.Document                                    ??= new();
            discovery.Document.AuthorizationDetailsTypesSupported =   types;
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}