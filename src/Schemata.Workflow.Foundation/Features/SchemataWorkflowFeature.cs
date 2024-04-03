using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Workflow.Foundation.Features;

[DependsOn<SchemataControllersFeature>]
[Information("Workflow depends on Controllers feature, it will be added automatically.", Level = LogLevel.Debug)]
public class SchemataWorkflowFeature : FeatureBase
{
    public override int Priority => 330_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) { }
}
