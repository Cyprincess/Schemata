using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Creates service scopes from the tenant-isolated service provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     When a tenant is resolved, scopes are created from the tenant's isolated container.
///     When no tenant is resolved, scopes fall back to the root provider.
/// </remarks>
public interface ITenantServiceScopeFactory<TTenant> : IServiceScopeFactory
    where TTenant : SchemataTenant;
