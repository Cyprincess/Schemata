using System;
using Schemata.Core;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataBuilder" /> to enable multi-tenancy.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds multi-tenancy using the default entity type and <see cref="Guid" /> keys.
    /// </summary>
    public static SchemataTenancyBuilder<SchemataTenant> UseTenancy(this SchemataBuilder builder) {
        return builder.UseTenancy<SchemataTenant>();
    }

    /// <summary>
    ///     Adds multi-tenancy using a custom tenant entity type with the default manager.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseTenancy<TTenant>(this SchemataBuilder builder)
        where TTenant : SchemataTenant {
        return builder.UseTenancy<SchemataTenantManager<TTenant>, TTenant>();
    }

    /// <summary>
    ///     Adds multi-tenancy using custom manager and tenant entity types.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseTenancy<TManager, TTenant>(this SchemataBuilder builder)
        where TManager : class, ITenantManager<TTenant>
        where TTenant : SchemataTenant {
        builder.AddFeature<SchemataTenancyFeature<TManager, TTenant>>();

        return new(builder.Services);
    }
}
