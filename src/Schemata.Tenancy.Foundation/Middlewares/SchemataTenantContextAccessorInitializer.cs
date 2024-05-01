using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

public class SchemataTenantContextAccessorInitializer<TTenant, TKey>(RequestDelegate next)
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    public async Task Invoke(HttpContext context, ITenantContextAccessor<TTenant, TKey> tenant) {
        await tenant.InitializeAsync(context.RequestAborted);

        await next.Invoke(context);
    }
}
