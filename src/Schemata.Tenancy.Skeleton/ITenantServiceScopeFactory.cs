using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Creates service scopes from the tenant-isolated service provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
    ///     Resolved tenants use their isolated container. Requests with an absent tenant use the root provider.
/// </remarks>
public interface ITenantServiceScopeFactory<TTenant> : IServiceScopeFactory
    where TTenant : SchemataTenant;
