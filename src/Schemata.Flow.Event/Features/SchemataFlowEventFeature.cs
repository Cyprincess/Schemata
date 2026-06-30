using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Event.Foundation.Features;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Events;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Event.Features;

/// <summary>Bridges BPMN message and signal catches to the event bus.</summary>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataEventFeature>]
public sealed class SchemataFlowEventFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority"/> for the Flow.Event feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 300_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor, AdviceTransitionEvent>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProcessLifecycleObserver, ProcessEventLifecycleObserver>());
        services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();

        services.Configure<EventTypeRegistryConfiguration>(options => {
            options.Registrations.Add((typeof(ProcessStartedEvent), "schemata/flow/process.started"));
            options.Registrations.Add((typeof(ProcessCompletedEvent), "schemata/flow/process.completed"));
            options.Registrations.Add((typeof(ProcessFailedEvent), "schemata/flow/process.failed"));
            options.Registrations.Add((typeof(TransitionMadeEvent), "schemata/flow/transition.made"));
        });
    }
}
