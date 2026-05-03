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

    /// <inheritdoc />
    public virtual int Order => Priority;

    /// <summary>
    ///     Lower values run earlier.
    /// </summary>
    public virtual int Priority => int.MaxValue;

    /// <inheritdoc />
    public virtual void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) { }

    /// <inheritdoc />
    public virtual void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) { }

    /// <inheritdoc />
    public virtual void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) { }

    #endregion
}
