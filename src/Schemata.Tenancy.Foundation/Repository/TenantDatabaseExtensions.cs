using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Schemata.Tenancy.Foundation.Repository;

/// <summary>
///     Per-tenant dedicated-database registration: the tenant repository reads and writes its
///     own native source, and every join across repositories and units of work is validated
///     against the physical target and the fixed tenant binding.
/// </summary>
public static class TenantDatabaseExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="TRepository" /> as the tenant-side repository for
    ///     <typeparamref name="TEntity" />, then invokes <paramref name="configureSource" /> to
    ///     configure the native factory, options, and unit of work for this tenant's database.
    ///     The callback runs once at tenant registration; context creation never queries the
    ///     tenant catalog and no <c>ITenant</c> trait is required.
    /// </summary>
    /// <remarks>
    ///     <paramref name="configureSource" /> must register the source through a provider
    ///     <c>AddRepositorySource</c> entry; when it does not, the missing local registration
    ///     receipt fails the tenant instead of silently falling back to the host source.
    ///     <c>TenantRepositoryBuilder&lt;TTenant&gt;.TenantUid</c> is available for selecting
    ///     the tenant's connection configuration.
    /// </remarks>
    public static TenantRepositoryBuilder<TTenant> UseDatabase<TTenant, TEntity, TRepository>(
        this TenantRepositoryBuilder<TTenant> builder,
        Action<IServiceCollection>            configureSource)
        where TTenant : SchemataTenant
        where TEntity : class
        where TRepository : class, IRepository<TEntity> {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSource);

        if (typeof(SchemataTenant).IsAssignableFrom(typeof(TEntity))
         || typeof(SchemataTenantHost).IsAssignableFrom(typeof(TEntity))) {
            throw new ArgumentException("Tenant catalog entities belong to the host.", nameof(TEntity));
        }

        builder.Services.TryAddTransient<IRepository<TEntity>, TRepository>();

        configureSource(builder.Services);

        if (!builder.Services.Any(d => d.ServiceType == typeof(RepositorySourceRegistration))) {
            throw new InvalidOperationException(
                $"Tenant '{builder.TenantUid}' has no repository source configured for {typeof(TEntity)}; "
              + "register the native factory through AddRepositorySource in the configureSource callback instead of relying on host fallback.");
        }

        return builder;
    }
}
