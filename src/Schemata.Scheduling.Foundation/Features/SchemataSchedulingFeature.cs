using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Schemata.Abstractions;
using Schemata.Common;
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
        services.AddSingleton<IHostedService, ScheduledJobRegistryInitializer>();
        services.TryAddSingleton<JobExecutionDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<JobExecutionDispatcher>());
        services.TryAddSingleton<IScheduler, DefaultScheduler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobLifecycleObserver, SchemataJobAuditObserver>());
        services.AddHostedService<SchedulingInitializer>();
    }

    private sealed class ScheduledJobRegistryInitializer(IScheduledJobRegistry registry) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) {
            registry.RegisterAll(AppDomainTypeCache.Types.Values);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
