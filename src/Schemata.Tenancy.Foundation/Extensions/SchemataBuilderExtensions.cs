using Schemata.Core;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataBuilder" /> to enable multi-tenancy.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds multi-tenancy using the default tenant and tenant-host entity types.
    /// </summary>
    public static SchemataTenancyBuilder<SchemataTenant> UseTenancy(this SchemataBuilder builder) {
        return builder.UseTenancy<SchemataTenant, SchemataTenantHost>();
    }

    /// <summary>
    ///     Adds multi-tenancy using a custom tenant entity type and the default tenant-host entity
    ///     type with the default manager.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseTenancy<TTenant>(this SchemataBuilder builder)
        where TTenant : SchemataTenant, new() {
        return builder.UseTenancy<TTenant, SchemataTenantHost>();
    }

    /// <summary>
    ///     Adds multi-tenancy using custom tenant and tenant-host entity types with the default
    ///     manager.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseTenancy<TTenant, TTenantHost>(this SchemataBuilder builder)
        where TTenant : SchemataTenant, new()
        where TTenantHost : SchemataTenantHost, new() {
        return builder.UseTenancy<SchemataTenantManager<TTenant, TTenantHost>, TTenant, TTenantHost>();
    }

    /// <summary>
    ///     Adds multi-tenancy using custom manager, tenant, and tenant-host entity types.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseTenancy<TManager, TTenant, TTenantHost>(this SchemataBuilder builder)
        where TManager : class, ITenantManager<TTenant>
        where TTenant : SchemataTenant, new()
        where TTenantHost : SchemataTenantHost, new() {
        builder.AddFeature<SchemataTenancyFeature<TManager, TTenant, TTenantHost>>();
        builder.AddFeature<SchemataTenantPrincipalFeature<TTenant>>();
        builder.AddFeature<SchemataTenantExecutionFeature>();

        return new(builder.Services);
    }
}
