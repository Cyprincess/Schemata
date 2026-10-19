using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Scheduling.Handlers;
using Schemata.Flow.Scheduling.Runtime;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Flow.Scheduling.Features;

/// <summary>Bridges BPMN timer catches to the persistent scheduler.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataSchedulingFeature>]
public sealed class SchemataFlowSchedulingFeature<TJob, TExecution> : FeatureBase
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
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
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowCatchHandler, FlowTimerCatchHandler<TJob, TExecution>>());
        services.AddScheduledJob<FlowTimerJob<TJob>>();
    }
}
