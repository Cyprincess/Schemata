using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

public class SchemataTenantContextAccessorInitializer<TTenant, TKey> where TTenant : SchemataTenant<TKey>
                                                                     where TKey : struct, IEquatable<TKey>
{
    private readonly RequestDelegate _next;

    public SchemataTenantContextAccessorInitializer(RequestDelegate next) {
        _next = next;
    }

    public async Task Invoke(HttpContext http, ITenantContextAccessor<TTenant, TKey> tenant) {
        await tenant.InitializeAsync(http.RequestAborted);

        await _next.Invoke(http);
    }
}
