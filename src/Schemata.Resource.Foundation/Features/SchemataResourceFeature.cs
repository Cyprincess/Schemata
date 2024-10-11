using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Advices;

namespace Schemata.Resource.Foundation.Features;

[DependsOn<SchemataRoutingFeature>]
[DependsOn("SchemataMappingFeature")]
[DependsOn("SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase
{
    public override int Priority => 360_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvice<,>), typeof(AdviceCreateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceEditRequestAdvice<,>), typeof(AdviceEditRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceEditAdvice<,>), typeof(AdviceEditFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvice<>), typeof(AdviceDeleteFreshness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvice<,>), typeof(AdviceResponseFreshness<,>)));
    }
}
