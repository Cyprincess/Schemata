using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataTenancyBuilder<SchemataTenant<Guid>, Guid> UseTenancy(
        this SchemataBuilder                               builder,
        Action<IServiceCollection, SchemataTenant<Guid>?>? configure = null) {
        return UseTenancy<SchemataTenant<Guid>, Guid>(builder, configure);
    }

    public static SchemataTenancyBuilder<TTenant, TKey> UseTenancy<TTenant, TKey>(
        this SchemataBuilder                  builder,
        Action<IServiceCollection, TTenant?>? configure = null)
        where TTenant : SchemataTenant<TKey>
        where TKey : struct, IEquatable<TKey> {
        return UseTenancy<SchemataTenantManager<TTenant, TKey>, TTenant, TKey>(builder, configure);
    }

    public static SchemataTenancyBuilder<TTenant, TKey> UseTenancy<TManager, TTenant, TKey>(
        this SchemataBuilder                  builder,
        Action<IServiceCollection, TTenant?>? configure = null)
        where TManager : class, ITenantManager<TTenant, TKey>
        where TTenant : SchemataTenant<TKey>
        where TKey : struct, IEquatable<TKey> {
        configure ??= (_, _) => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataTenancyFeature<TManager, TTenant, TKey>>();

        return new(builder);
    }
}
