using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

/// <summary>
///     Middleware that initializes the tenant context accessor early in the request pipeline.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public class SchemataTenantContextAccessorInitializer<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly RequestDelegate _next;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantContextAccessorInitializer{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantContextAccessorInitializer(RequestDelegate next) { _next = next; }

    /// <summary>Resolves the tenant and passes the request to the next middleware.</summary>
    public async Task Invoke(HttpContext http, ITenantContextAccessor<TTenant, TKey> tenant) {
        await tenant.InitializeAsync(http.RequestAborted);

        await _next.Invoke(http);
    }
}
