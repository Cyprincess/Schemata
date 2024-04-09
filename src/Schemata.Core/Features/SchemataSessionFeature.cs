using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[DependsOn<SchemataCookiePolicyFeature>]
[Information("Session may requires Cookie Policy feature, it will be added automatically.", Level = LogLevel.Debug)]
public sealed class SchemataSessionFeature<T> : FeatureBase
    where T : class, ISessionStore
{
    public override int Priority => 170_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<SessionOptions>();
        services.TryAddTransient<ISessionStore, T>();
        services.AddSession(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseSession();
    }
}
