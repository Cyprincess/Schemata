using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Creates isolated <see cref="System.IServiceProvider" /> instances scoped to a specific tenant.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     <para>
///         Each tenant gets its own DI container built from the root service collection.
///         Tenant-specific and dynamic overrides registered through
///         <see cref="SchemataTenancyOptions" /> are applied on container build.
///         Service providers are cached per tenant via <see cref="ITenantProviderCache" />.
///     </para>
///     <para>
    ///         The returned <see cref="ITenantProviderLease" /> pins the cached provider while in use;
    ///         callers must dispose the lease when the tenant scope it backs is disposed.
///     </para>
/// </remarks>
public interface ITenantServiceProviderFactory<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>Acquires a lease over the per-tenant service provider for the tenant in the given accessor.</summary>
    ITenantProviderLease CreateServiceProvider(ITenantContextAccessor<TTenant> accessor);
}
