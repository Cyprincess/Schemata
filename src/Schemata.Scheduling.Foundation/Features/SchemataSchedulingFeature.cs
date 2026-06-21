using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Foundation.Observers;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Features;

/// <summary>Registers the in-memory <see cref="IScheduler" /> and the audit lifecycle observer.</summary>
public sealed class SchemataSchedulingFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Scheduling feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 70_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>();
        services.TryAddSingleton<JobExecutionDispatcher>();
        services.TryAddSingleton<IScheduler, DefaultScheduler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobLifecycleObserver, SchemataJobAuditObserver>());

        services.AddHostedService<SchedulingInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<JobExecutionDispatcher>());
    }
}
