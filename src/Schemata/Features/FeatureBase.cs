using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public abstract class FeatureBase : ISimpleFeature
{
    public virtual int Order => Priority;

    public virtual int Priority => int.MaxValue;

    public virtual void ConfigureServices(IServiceCollection services, IConfiguration conf, IWebHostEnvironment env) { }

    public virtual void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) { }
}
