using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

public class SchemataTenantServiceProviderReplacer<TTenant, TKey>(RequestDelegate next)
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    public async Task Invoke(HttpContext context, ITenantServiceScopeFactory<TTenant, TKey> factory) {
        var feature = context.Features.Get<IServiceProvidersFeature>()!;

        context.Features.Set<IServiceProvidersFeature>(new RequestServicesFeature(context, factory));

        try {
            await next.Invoke(context);
        } finally {
            context.Features.Set(feature);
        }
    }
}
