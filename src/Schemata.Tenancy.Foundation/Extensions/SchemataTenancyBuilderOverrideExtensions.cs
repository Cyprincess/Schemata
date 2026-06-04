using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Registers per-tenant DI overrides via <see cref="SchemataTenancyOptions" />.
/// </summary>
public static class SchemataTenancyBuilderOverrideExtensions
{
    /// <summary>
    ///     Applies <paramref name="configure" /> to the root <see cref="IServiceCollection" />
    ///     so the registrations flow into every tenant container via the standard copy step
    ///     in <see cref="SchemataTenancyOptions" /> tenant container construction.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> ForAll<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder,
        Action<IServiceCollection>           configure
    )
        where TTenant : SchemataTenant {
        if (configure is null) {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(builder.Services);

        return builder;
    }

    /// <summary>
    ///     Registers an override applied only when the current tenant identifier exactly matches
    ///     <paramref name="tenantId" />; runs after <see cref="ForAll{TTenant}" /> overrides.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> ForTenant<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder,
        string                               tenantId,
        Action<IServiceCollection>           configure
    )
        where TTenant : SchemataTenant {
        if (string.IsNullOrWhiteSpace(tenantId)) {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        if (configure is null) {
            throw new ArgumentNullException(nameof(configure));
        }

        builder.Services.Configure<SchemataTenancyOptions>(o => {
            if (!o.TenantOverrides.TryGetValue(tenantId, out var list)) {
                list                        = [];
                o.TenantOverrides[tenantId] = list;
            }

            list.Add(configure);
        });

        return builder;
    }

    /// <summary>
    ///     Registers a dynamic override invoked for every tenant container with the tenant identifier
    ///     and the root service provider; runs last, after <see cref="ForAll{TTenant}" /> and
    ///     the tenant-specific overloads.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> ForTenant<TTenant>(
        this SchemataTenancyBuilder<TTenant>                 builder,
        Action<string, IServiceCollection, IServiceProvider> configure
    )
        where TTenant : SchemataTenant {
        if (configure is null) {
            throw new ArgumentNullException(nameof(configure));
        }

        builder.Services.Configure<SchemataTenancyOptions>(o => o.DynamicOverrides.Add(configure));

        return builder;
    }
}
