using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>Runs the registered session feature through the tenant-owned state branch selector.</summary>
public sealed class TenantSessionFeature<T> : FeatureBase
    where T : class, ISessionStore
{
    private readonly SchemataSessionFeature<T> _inner;

    public TenantSessionFeature(SchemataSessionFeature<T> inner) {
        _inner = inner;
    }

    public override int Order => _inner.Order;

    public override int Priority => _inner.Priority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        _inner.ConfigureServices(services, schemata, configurators, configuration, environment);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseTenantStatePipeline(TenantStatePipeline.Session);
    }
}
