using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Middlewares;

public sealed class SchemataTenancyMiddleware<TTenant>(RequestDelegate next) where TTenant : SchemataTenant
{
    public async Task Invoke(HttpContext http, SchemataTenantContextAccessor<TTenant> accessor, ITenantServiceScopeFactory<TTenant> scopes) {
        await accessor.InitializeAsync(TenantResolutionStage.Request, http.RequestAborted);
        var original = http.RequestServices;
        var prior = http.Features.Get<ITenantRequestBinding>();
        var binding = new TenantRequestBinding<TTenant>(accessor, scopes);
        using var frame = TenantContext.Enter(new(accessor.Tenant?.Uid));
        await using var owned = binding;
        await binding.OpenAsync(http);
        http.Features.Set<ITenantRequestBinding>(binding);
        try {
            await next(http);
        } finally {
            http.RequestServices = original;
            http.Features.Set(prior);
        }
    }
}

internal sealed class TenantRequestBinding<TTenant>(SchemataTenantContextAccessor<TTenant> accessor, ITenantServiceScopeFactory<TTenant> scopes)
    : ITenantRequestBinding, IAsyncDisposable where TTenant : SchemataTenant
{
    private readonly List<(TenantIdentity Identity, AsyncServiceScope Scope)> _scopes = [];
    public TenantIdentity Identity { get; private set; } = new(accessor.Tenant?.Uid);

    internal async Task OpenAsync(HttpContext http) {
        var scope = await scopes.CreateAsync(Identity, http.RequestAborted);
        _scopes.Add((Identity, scope));
        http.RequestServices = scope.ServiceProvider;
    }

    public async Task BindPrincipalAsync(HttpContext http) {
        await accessor.InitializeAsync(TenantResolutionStage.Principal, http.RequestAborted);
        var selected = new TenantIdentity(accessor.Tenant?.Uid);
        if (selected == Identity) return;
        Identity = selected;
        await OpenAsync(http);
    }

    public async ValueTask DisposeAsync() {
        List<Exception>? failures = null;
        for (var i = _scopes.Count - 1; i >= 0; i--) {
            using var frame = TenantContext.Enter(_scopes[i].Identity);
            try { await _scopes[i].Scope.DisposeAsync(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException(failures);
    }
}

public sealed class SchemataTenantPrincipalMiddleware<TTenant>(RequestDelegate next) where TTenant : SchemataTenant
{
    public async Task Invoke(HttpContext http) {
        var binding = http.Features.Get<ITenantRequestBinding>()!;
        await binding.BindPrincipalAsync(http);
        using var frame = TenantContext.Enter(binding.Identity);
        await next(http);
    }
}
