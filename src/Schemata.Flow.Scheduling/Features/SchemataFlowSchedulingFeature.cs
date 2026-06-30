using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Scheduling.Internal;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Scheduling.Foundation.Features;

namespace Schemata.Flow.Scheduling.Features;

/// <summary>Bridges BPMN timer catches to the persistent scheduler.</summary>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataSchedulingFeature>]
public sealed class SchemataFlowSchedulingFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority"/> for the Flow.Scheduling feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 400_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor, AdviceTransitionTimer>());
        services.AddScheduledJob<FlowTimerJob>();
    }
}
