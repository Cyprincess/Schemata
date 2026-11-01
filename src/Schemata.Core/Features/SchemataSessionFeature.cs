using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core.State;

namespace Schemata.Core.Features;

/// <summary>
///     Configures session services with a pluggable <typeparamref name="T" />
///     session store and wires the session state branch. Depends on
///     <see cref="SchemataCookiePolicyFeature" /> because session typically
///     requires cookies.
/// </summary>
/// <typeparam name="T">A concrete <see cref="ISessionStore" /> implementation.</typeparam>
[DependsOn<SchemataCookiePolicyFeature>]
public sealed class SchemataSessionFeature<T> : FeatureBase
    where T : class, ISessionStore
{
    /// <summary>
    ///     Default middleware priority for session state.
    /// </summary>
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
        services.AddStatePipelineProvider();
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseStatePipeline(StatePipeline.Session);
    }
}
