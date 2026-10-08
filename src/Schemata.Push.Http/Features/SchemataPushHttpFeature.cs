using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Push.Foundation.Features;
using Schemata.Transport.Http.Features;

namespace Schemata.Push.Http.Features;

[DependsOn<SchemataPushFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataPushHttpFeature : FeatureBase
{
    public override int Priority => SchemataPushFeature.DefaultPriority + 100_000;

    public override void ConfigureServices(IServiceCollection services, SchemataOptions schemata,
        Configurators configurators, IConfiguration configuration, IWebHostEnvironment environment)
        => services.AddSchemataPushHttp();
}
