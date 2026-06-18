using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

/// <summary>
///     Consolidated tenancy middleware: resolves the tenant through
///     <see cref="ITenantContextInitializer{TTenant}" /> and replaces the request
///     <see cref="IServiceProvidersFeature" /> with the tenant-scoped provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenancyMiddleware<TTenant>
    where TTenant : SchemataTenant
{
    private readonly RequestDelegate _next;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenancyMiddleware{TTenant}" /> class.
    /// </summary>
    public SchemataTenancyMiddleware(RequestDelegate next) { _next = next; }

    /// <summary>Initializes the tenant context and swaps the request services for the scoped duration.</summary>
    public async Task Invoke(
        HttpContext                         http,
        ITenantContextInitializer<TTenant>  initializer,
        ITenantServiceScopeFactory<TTenant> factory
    ) {
        await initializer.InitializeAsync(http.RequestAborted);

        var original = http.Features.Get<IServiceProvidersFeature>()!;
        http.Features.Set<IServiceProvidersFeature>(new RequestServicesFeature(http, factory));

        try {
            await _next.Invoke(http);
        } finally {
            http.Features.Set(original);
        }
    }
}
