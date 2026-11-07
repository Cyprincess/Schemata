using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Scim.Handlers;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scim.Services;
using Schemata.Transport.Http.Features;

namespace Schemata.Authorization.Scim.Features;

/// <summary>Registers Application provisioning with the shared SCIM protocol.</summary>
[DependsOn(typeof(SchemataAuthorizationFeature<,,,,,>))]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataAuthorizationScimFeature<TApp> : FeatureBase where TApp : SchemataApplication, new()
{
    public const int DefaultPriority = SchemataAuthorizationOrders.Feature + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataScim();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScimResourceHandler, ScimApplicationHandler<TApp>>());
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) => app.UseSchemataScimErrors();
}
