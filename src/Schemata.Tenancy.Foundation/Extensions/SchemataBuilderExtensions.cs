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
    public static SchemataTenancyBuilder<SchemataTenant<Guid>, Guid> UseTenancy(this SchemataBuilder builder) {
        return builder.UseTenancy<SchemataTenant<Guid>, Guid>();
    }

    /// <summary>
    ///     Adds multi-tenancy using a custom tenant entity type with the default manager.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant, TKey> UseTenancy<TTenant, TKey>(this SchemataBuilder builder)
        where TTenant : SchemataTenant<TKey>
        where TKey : struct, IEquatable<TKey> {
        return builder.UseTenancy<SchemataTenantManager<TTenant, TKey>, TTenant, TKey>();
    }

    /// <summary>
    ///     Adds multi-tenancy using custom manager and tenant entity types.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant, TKey> UseTenancy<TManager, TTenant, TKey>(
        this SchemataBuilder builder
    )
        where TManager : class, ITenantManager<TTenant, TKey>
        where TTenant : SchemataTenant<TKey>
        where TKey : struct, IEquatable<TKey> {
        builder.AddFeature<SchemataTenancyFeature<TManager, TTenant, TKey>>();

        return new(builder.Services);
    }
}
