using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Push.Foundation.Features;
using Schemata.Push.Skeleton.Entities;
using Schemata.Transport.Http.Features;

namespace Schemata.Push.Http.Features;

/// <summary>
///     Exposes the Push subscription and control endpoints for the host-selected subscription
///     entity.
/// </summary>
/// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
/// <remarks>
///     The type parameter carries the closed feature selection and ordering; the endpoints themselves
///     are entity-agnostic.
/// </remarks>
[DependsOn(typeof(SchemataPushFeature<>))]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataPushHttpFeature<TPushSubscription> : FeatureBase
    where TPushSubscription : SchemataPushSubscription, new()
{
    public override int Priority => SchemataPushFeature.DefaultPriority + 100_000;

    public override void ConfigureServices(IServiceCollection services, SchemataOptions schemata,
        Configurators configurators, IConfiguration configuration, IWebHostEnvironment environment)
        => services.AddSchemataPushHttp();
}
