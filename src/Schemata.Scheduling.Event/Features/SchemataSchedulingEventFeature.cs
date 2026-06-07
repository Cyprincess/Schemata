using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Event.Foundation.Features;
using Schemata.Event.Foundation.Internal;
using Schemata.Scheduling.Event.Events;
using Schemata.Scheduling.Event.Internal;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Event.Features;

/// <summary>Bridges scheduler lifecycle observers to the event bus.</summary>
[DependsOn<SchemataSchedulingFeature>]
[DependsOn<SchemataEventFeature>]
public sealed class SchemataSchedulingEventFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Scheduling.Event feature.</summary>
    public const int DefaultPriority = SchemataSchedulingFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobLifecycleObserver, EventPublishingJobLifecycleObserver>());

        // Wire names are kebab-case resource-style so they survive renames and cross-service deploys.
        services.Configure<EventTypeRegistryConfiguration>(options => {
            options.Registrations.Add((typeof(JobTriggered), "schemata/scheduling/job-triggered"));
            options.Registrations.Add((typeof(JobCompleted), "schemata/scheduling/job-completed"));
            options.Registrations.Add((typeof(JobFailed), "schemata/scheduling/job-failed"));
        });
    }
}
