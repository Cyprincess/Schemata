using System;
using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Skeleton;
using Schemata.Tenancy.Caching;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods composing per-tenant cache outlets on the tenancy builder.</summary>
public static class TenantCacheExtensions
{
    /// <summary>
    ///     Composes per-tenant cache registration through the dynamic tenant overrides. Each tenant
    ///     container receives the outlet declared by <paramref name="configure" /> under its
    ///     verified tenant identifier; the identifier never comes from the ambient tenant context.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseCache<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder,
        Action<TenantCacheBuilder<TTenant>> configure
    ) where TTenant : SchemataTenant {
        builder.Services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((identifier, services, _) => {
            if (!Guid.TryParse(identifier, out var uid) || uid == Guid.Empty) {
                throw new InvalidOperationException($"Tenant caches require a non-empty verified tenant identifier; tenant '{identifier}' has none.");
            }

            configure(new(services, uid));
        }));
        return builder;
    }

    /// <summary>
    ///     Registers the closed tenant <see cref="IDistributedCache" /> for the Session
    ///     closure: a <see cref="SessionCache" /> over this builder's outlet, so native
    ///     session entries share the tenant namespace while staying framed away from business
    ///     cache keys. The outlet mode must be declared first — a builder without
    ///     <see cref="TenantCacheBuilder{TTenant}.UseShared" /> or
    ///     <see cref="TenantCacheBuilder{TTenant}.UseDedicated" /> has no outlet to bind.
    /// </summary>
    /// <exception cref="InvalidOperationException">No outlet mode was declared first.</exception>
    public static TenantCacheBuilder<TTenant> UseSessionCache<TTenant>(this TenantCacheBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(ICacheProvider))) {
            throw new InvalidOperationException(
                $"Tenant session cache for '{builder.TenantUid:N}' requires a declared cache outlet; call UseShared or UseDedicated before UseSessionCache.");
        }

        builder.Services.TryAddSingleton<IDistributedCache>(sp => new SessionCache(
            sp.GetRequiredService<ICacheProvider>(),
            sp));
        return builder;
    }
}
