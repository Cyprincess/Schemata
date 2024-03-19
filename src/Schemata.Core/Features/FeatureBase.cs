using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

public abstract class FeatureBase : ISimpleFeature
{
    #region ISimpleFeature Members

    public virtual int Order => Priority;

    public virtual int Priority => int.MaxValue;

    public virtual void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) { }

    public virtual void Configure(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) { }

    #endregion
}
