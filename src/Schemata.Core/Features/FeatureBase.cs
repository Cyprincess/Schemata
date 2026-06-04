using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     No-op base implementation of <see cref="ISimpleFeature" />. Override individual
///     lifecycle methods to add behaviour.
/// </summary>
public abstract class FeatureBase : ISimpleFeature
{
    #region ISimpleFeature Members

    public virtual int Order => Priority;

    /// <summary>
    ///     Lower values run earlier.
    /// </summary>
    public virtual int Priority => int.MaxValue;

    public virtual void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) { }

    public virtual void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) { }

    public virtual void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) { }

    #endregion
}
