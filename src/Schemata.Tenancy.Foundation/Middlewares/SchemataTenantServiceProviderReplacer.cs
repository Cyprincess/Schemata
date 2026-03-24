using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

/// <summary>
///     Middleware that replaces the request service provider with the tenant-isolated provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     After the tenant context is initialized, this middleware swaps the
///     <see cref="IServiceProvidersFeature" /> so that all downstream middleware and
///     controllers resolve services from the tenant-scoped container.
/// </remarks>
public class SchemataTenantServiceProviderReplacer<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly RequestDelegate _next;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceProviderReplacer{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantServiceProviderReplacer(RequestDelegate next) { _next = next; }

    /// <summary>Replaces the request services with the tenant-scoped provider for the duration of the request.</summary>
    public async Task Invoke(HttpContext http, ITenantServiceScopeFactory<TTenant, TKey> factory) {
        var feature = http.Features.Get<IServiceProvidersFeature>()!;

        http.Features.Set<IServiceProvidersFeature>(new RequestServicesFeature(http, factory));

        try {
            await _next.Invoke(http);
        } finally {
            http.Features.Set(feature);
        }
    }
}
