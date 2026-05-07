using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Provides access to the current tenant context within a request scope.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     The accessor is initialized by middleware early in the request pipeline.
///     After initialization, <see cref="Tenant" /> contains the resolved tenant for the current request.
/// </remarks>
public interface ITenantContextAccessor<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>Gets the resolved tenant for the current request, or <see langword="null" /> if not yet initialized.</summary>
    TTenant? Tenant { get; }

    /// <summary>Resolves and initializes the tenant context using the registered <see cref="ITenantResolver" />.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Initializes the tenant context with an explicit tenant instance.</summary>
    Task InitializeAsync(TTenant tenant, CancellationToken ct);

    /// <summary>Gets the root (non-tenant-scoped) service provider.</summary>
    Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct);
}
