using System;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Creates isolated <see cref="IServiceProvider" /> instances scoped to a specific tenant.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     Each tenant gets its own DI container built from the root service collection
///     with tenant-specific overrides applied via the configure delegate.
///     Service providers are cached per tenant to avoid repeated container builds.
/// </remarks>
public interface ITenantServiceProviderFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>Creates or retrieves the cached service provider for the tenant in the given accessor.</summary>
    IServiceProvider CreateServiceProvider(ITenantContextAccessor<TTenant, TKey> accessor);
}
