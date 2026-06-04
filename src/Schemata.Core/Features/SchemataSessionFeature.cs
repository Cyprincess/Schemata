using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Schemata.Core.Features;

/// <summary>
///     Configures session middleware with a pluggable <typeparamref name="T" />
///     session store. Depends on <see cref="SchemataCookiePolicyFeature" /> because
///     session typically requires cookies.
/// </summary>
/// <typeparam name="T">A concrete <see cref="ISessionStore" /> implementation.</typeparam>
[DependsOn<SchemataCookiePolicyFeature>]
public sealed class SchemataSessionFeature<T> : FeatureBase
    where T : class, ISessionStore
{
    public const int DefaultPriority = SchemataAuthenticationFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<SessionOptions>();
        services.TryAddTransient<ISessionStore, T>();
        services.AddSession(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseSession();
    }
}
