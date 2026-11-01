using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Push.Foundation.Builders;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Features;

/// <summary>
///     Marks the Push subsystem as installed. The persisted subscription type is chosen by
///     <see cref="SchemataPushFeature{TPushSubscription}" />; this marker only carries the feature
///     ordering contract the bridge features depend on.
/// </summary>
public sealed class SchemataPushFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Push feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 120_000_000;

    public override int Priority => DefaultPriority;
}

/// <summary>
///     Registers the push service, subscription manager, and the request handlers closed on the
///     selected subscription entity. Transports are contributed through
///     <see cref="SchemataPushBuilder.AddTransport{TTransport}" />. Subscription endpoints are
///     exposed by whatever resource transport (HTTP/gRPC) the host activates.
/// </summary>
/// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
[DependsOn<SchemataPushFeature>]
public sealed class SchemataPushFeature<TPushSubscription> : FeatureBase
    where TPushSubscription : SchemataPushSubscription, new()
{
    public override int Priority => SchemataPushFeature.DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataPush<TPushSubscription>();
    }
}
