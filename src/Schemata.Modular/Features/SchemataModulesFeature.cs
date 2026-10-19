using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Modular.Features;

/// <summary>
///     Feature that commits one module selection and runs its lifecycle phases exactly once.
/// </summary>
/// <typeparam name="TProvider">The module provider type.</typeparam>
/// <typeparam name="TRunner">The module runner type.</typeparam>
/// <remarks>
///     The <see cref="ConfigureServices" /> phase commits the selection — recording the runner
///     instance, provider, and configured module snapshot — and registers that runner instance as
///     the <see cref="IModulesRunner" /> singleton. Repeat registrations are idempotent: discovery
///     and <see cref="IModulesRunner.ConfigureServices" /> do not run again. The later phases
///     resolve the committed runner from the application services, so the instance configured
///     during <see cref="ConfigureServices" /> is the same owner that runs them.
/// </remarks>
public sealed class SchemataModulesFeature<TProvider, TRunner> : FeatureBase
    where TProvider : class, IModulesProvider
    where TRunner : class, IModulesRunner
{
    /// <summary>
    ///     Default priority for the modular feature in the extension feature range.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 140_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var runner = schemata.Get<TRunner>(ModularConstants.PendingRunnerKey);
        services.AddSchemataModules<TProvider, TRunner>(schemata, configuration, environment, runner);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.ApplicationServices.GetRequiredService<IModulesRunner>().ConfigureApplication(app, configuration, environment);
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        app.ApplicationServices.GetRequiredService<IModulesRunner>().ConfigureEndpoints(app, endpoints, configuration, environment);
    }
}
