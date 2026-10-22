using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Advertises the <c>implicit</c> grant when an implicit or hybrid degradation flow is
///     installed. The flow features opt in explicitly; the default discovery document never
///     advertises it.
/// </summary>
public sealed class AdviceDiscoveryImplicitGrant : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value (after the code-flow metadata).</summary>
    public const int DefaultOrder = AdviceDiscoveryCodeFlow.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        if (!discovery.Document.GrantTypesSupported.Contains(GrantTypes.Implicit)) {
            discovery.Document.GrantTypesSupported.Add(GrantTypes.Implicit);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
