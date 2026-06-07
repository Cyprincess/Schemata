using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Event.Foundation.Features;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton.Observers;

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
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionObserver, FlowEventTransitionObserver>());
        services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();
    }
}
