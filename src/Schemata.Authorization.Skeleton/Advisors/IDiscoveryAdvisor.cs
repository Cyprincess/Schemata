using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors for the discovery endpoint pipeline.
///     Registered advisors may populate or modify the <see cref="DiscoveryContext" /> before
///     the discovery document is serialized.
/// </summary>
public interface IDiscoveryAdvisor : IAdvisor<DiscoveryContext>;
