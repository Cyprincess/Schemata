using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Base class for features providing default no-op implementations of the <see cref="ISimpleFeature" /> methods.
/// </summary>
public abstract class FeatureBase : ISimpleFeature
{
    #region ISimpleFeature Members

    /// <inheritdoc />
    public virtual int Order => Priority;

    /// <inheritdoc />
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
