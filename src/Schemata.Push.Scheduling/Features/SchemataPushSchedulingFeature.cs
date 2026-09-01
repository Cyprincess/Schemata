using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Features;
using Schemata.Push.Scheduling.Internal;
using Schemata.Push.Skeleton;
using Schemata.Scheduling.Foundation.Features;

namespace Schemata.Push.Scheduling.Features;

/// <summary>
///     Bridges push dispatch to the persistent scheduler. Adds <see cref="IScheduledPushService" />
///     so a send can be deferred to a durable <see cref="PushDispatchJob" /> addressable as an
///     <c>operations/{operation}</c> long-running operation. Immediate dispatch stays on the
///     broadcast <c>IPushService</c>.
/// </summary>
[DependsOn<SchemataPushFeature>]
[DependsOn<SchemataSchedulingFeature>]
public sealed class SchemataPushSchedulingFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Push.Scheduling feature.</summary>
    public const int DefaultPriority = SchemataPushFeature.DefaultPriority + 400_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddScheduledJob<PushDispatchJob>();

        services.TryAddKeyedScoped<IRequestHandler<SchedulePushRequest, Operation>, SchedulePushHandler>(
            PushConstants.Handlers.Default);
        services.TryAddScoped<IRequestHandler<SchedulePushRequest, Operation>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<SchedulePushRequest, Operation>>(
                PushConstants.Handlers.Default));

        services.TryAddScoped<IScheduledPushService, ScheduledPushService>();
    }
}