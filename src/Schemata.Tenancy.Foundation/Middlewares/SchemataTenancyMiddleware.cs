using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

/// <summary>
///     Consolidated tenancy middleware: resolves the tenant, initializes
///     <see cref="ITenantContextAccessor{TTenant,TKey}" />, and replaces the request
///     <see cref="IServiceProvidersFeature" /> with the tenant-scoped provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public class SchemataTenancyMiddleware<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly RequestDelegate _next;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenancyMiddleware{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenancyMiddleware(RequestDelegate next) { _next = next; }

    /// <summary>Initializes the tenant context and swaps the request services for the scoped duration.</summary>
    public async Task Invoke(
        HttpContext                               http,
        ITenantContextAccessor<TTenant, TKey>     accessor,
        ITenantServiceScopeFactory<TTenant, TKey> factory
    ) {
        await accessor.InitializeAsync(http.RequestAborted);

        var original = http.Features.Get<IServiceProvidersFeature>()!;
        http.Features.Set<IServiceProvidersFeature>(new RequestServicesFeature(http, factory));

        try {
            await _next.Invoke(http);
        } finally {
            http.Features.Set(original);
        }
    }
}
