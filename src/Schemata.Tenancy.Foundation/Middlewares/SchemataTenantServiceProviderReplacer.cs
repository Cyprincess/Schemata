using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

public class SchemataTenantServiceProviderReplacer<TTenant, TKey> where TTenant : SchemataTenant<TKey>
                                                                  where TKey : struct, IEquatable<TKey>
{
    private readonly RequestDelegate _next;

    public SchemataTenantServiceProviderReplacer(RequestDelegate next) {
        _next = next;
    }

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
