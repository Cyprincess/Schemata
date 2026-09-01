using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation.Features;
using Schemata.Actor.Scheduling.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scheduling.Foundation.Features;

namespace Schemata.Actor.Scheduling.Features;

/// <summary>
///     Registers the <see cref="IActorReminders" /> implementation and its backing
///     <see cref="ActorReminderJob" />, satisfying <c>Schemata.Actor.Foundation</c>'s
///     <c>IActorContext.ScheduleAsync</c> once this bridge is installed.
/// </summary>
[DependsOn<SchemataActorFeature>]
[DependsOn<SchemataSchedulingFeature>]
public sealed class SchemataActorSchedulingFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Actor.Scheduling feature.</summary>
    public const int DefaultPriority = SchemataActorFeature.DefaultPriority + 200_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddSingleton<IActorReminders, ActorReminders>();
        services.AddScheduledJob<ActorReminderJob>();
    }
}
