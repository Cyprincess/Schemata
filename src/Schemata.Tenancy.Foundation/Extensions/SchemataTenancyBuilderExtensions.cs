using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Resolvers;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataTenancyBuilderExtensions
{
    public static SchemataTenancyBuilder<TTenant, TKey> UseHeaderResolver<TTenant, TKey>(
        this SchemataTenancyBuilder<TTenant, TKey> builder) where TTenant : SchemataTenant<TKey>
                                                            where TKey : struct, IEquatable<TKey>, IParsable<TKey>
    {
        builder.Services.TryAddScoped<ITenantResolver<TKey>, RequestHeaderResolver<TKey>>();

        return builder;
    }

    public static SchemataTenancyBuilder<TTenant, TKey> UseHostResolver<TTenant, TKey>(
        this SchemataTenancyBuilder<TTenant, TKey> builder) where TTenant : SchemataTenant<TKey>
                                                            where TKey : struct, IEquatable<TKey> {
        builder.Services.TryAddScoped<ITenantResolver<TKey>, RequestHostResolver<TTenant, TKey>>();

        return builder;
    }

    public static SchemataTenancyBuilder<TTenant, TKey> UsePathResolver<TTenant, TKey>(
        this SchemataTenancyBuilder<TTenant, TKey> builder) where TTenant : SchemataTenant<TKey>
                                                            where TKey : struct, IEquatable<TKey>, IParsable<TKey>
    {
        builder.Services.TryAddScoped<ITenantResolver<TKey>, RequestPathResolver<TKey>>();

        return builder;
    }

    public static SchemataTenancyBuilder<TTenant, TKey> UsePrincipalResolver<TTenant, TKey>(
        this SchemataTenancyBuilder<TTenant, TKey> builder) where TTenant : SchemataTenant<TKey>
                                                            where TKey : struct, IEquatable<TKey>, IParsable<TKey>
    {
        builder.Services.TryAddScoped<ITenantResolver<TKey>, RequestPrincipalResolver<TKey>>();

        return builder;
    }

    public static SchemataTenancyBuilder<TTenant, TKey> UseQueryResolver<TTenant, TKey>(
        this SchemataTenancyBuilder<TTenant, TKey> builder) where TTenant : SchemataTenant<TKey>
                                                            where TKey : struct, IEquatable<TKey>, IParsable<TKey>
    {
        builder.Services.TryAddScoped<ITenantResolver<TKey>, RequestQueryResolver<TKey>>();

        return builder;
    }
}
