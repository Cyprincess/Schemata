using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Resolvers;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataTenancyBuilder{TTenant}" /> to register tenant resolvers.
/// </summary>
public static class SchemataTenancyBuilderExtensions
{
    /// <summary>Registers the <c>x-tenant-id</c> HTTP header resolver.</summary>
    public static SchemataTenancyBuilder<TTenant> UseHeaderResolver<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder
    )
        where TTenant : SchemataTenant {
        builder.Services.TryAddScoped<ITenantResolver, RequestHeaderResolver>();

        return builder;
    }

    /// <summary>Registers the request <c>Host</c> header resolver, matching against tenant host names.</summary>
    public static SchemataTenancyBuilder<TTenant> UseHostResolver<TTenant>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        builder.Services.TryAddScoped<ITenantResolver, RequestHostResolver<TTenant>>();

        return builder;
    }

    /// <summary>Registers the <c>{Tenant}</c> route parameter resolver.</summary>
    public static SchemataTenancyBuilder<TTenant> UsePathResolver<TTenant>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        builder.Services.TryAddScoped<ITenantResolver, RequestPathResolver>();

        return builder;
    }

    /// <summary>Registers the authenticated principal <c>Tenant</c> claim resolver.</summary>
    public static SchemataTenancyBuilder<TTenant> UsePrincipalResolver<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder
    )
        where TTenant : SchemataTenant {
        builder.Services.TryAddScoped<ITenantResolver, RequestPrincipalResolver>();

        return builder;
    }

    /// <summary>Registers the <c>Tenant</c> query string parameter resolver.</summary>
    public static SchemataTenancyBuilder<TTenant> UseQueryResolver<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder
    )
        where TTenant : SchemataTenant {
        builder.Services.TryAddScoped<ITenantResolver, RequestQueryResolver>();

        return builder;
    }
}
